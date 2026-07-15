using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace dither_ex_machina.Rendering
{
    public static class DitherRenderer
    {
        private const double FeatherWidth = 0.08;

        public static byte[] Render(
            byte[] grayscale, int width, int height,
            RenderSettings settings,
            IDitherEffect effect,
            IReadOnlyDictionary<string, double> effectParams,
            (byte[] r, byte[] g, byte[] b)? gradientLut,
            CancellationToken token)
        {
            //общая предобработка тоновой карты
            byte[] toneMap = grayscale;

            if (settings.MedianRadius > 0)
                toneMap = ImageOps.MedianFilter(toneMap, width, height, settings.MedianRadius);

            if (settings.BlurRadius > 0)
                toneMap = ImageOps.BoxBlur(toneMap, width, height, settings.BlurRadius);

            int supersample = effect is ISequentialDitherEffect ? 1 : settings.Supersample;

            int sw = width * supersample;
            int sh = height * supersample;

            double[] upsampled = ImageOps.UpsampleBilinear(toneMap, width, height, sw, sh);

            //контрастная кривая
            double[] adjusted = new double[sw * sh];
            Parallel.For(0, sh, sy =>
            {
                for (int sx = 0; sx < sw; sx++)
                {
                    int idx = sy * sw + sx;
                    adjusted[idx] = ImageOps.ApplyContrastCurve(
                        upsampled[idx], settings.ContrastCenter, settings.ContrastSteepness);
                }
            });

            if (settings.PostContrastSmoothRadius > 0)
                adjusted = ImageOps.BoxBlurDouble(adjusted, sw, sh, settings.PostContrastSmoothRadius);

            byte[] hiRes;

            if (effect is ISequentialDitherEffect sequentialEffect)
            {
                double[] clamped = ApplyHardCutoff(
                    adjusted, sw, sh, settings.ShadowCutoff, settings.HighlightCutoff);

                hiRes = sequentialEffect.ComputeFullImage(clamped, sw, sh, effectParams, token);

                if (settings.Invert)
                {
                    Parallel.For(0, hiRes.Length, i => hiRes[i] = (byte)(255 - hiRes[i]));
                }
            }
            else
            {
                hiRes = RenderPerPixel(adjusted, sw, sh, settings, effect, effectParams, token);
            }
            byte[] grayOutput = ImageOps.DownsampleGray(hiRes, sw, sh, width, height, supersample);

            return gradientLut.HasValue
                ? ImageOps.ApplyGradientLut(grayOutput, width, height,
                    gradientLut.Value.r, gradientLut.Value.g, gradientLut.Value.b)
                : ImageOps.GrayToBgra(grayOutput, width, height);
        }

        private static double[] ApplyHardCutoff(
            double[] brightness, int width, int height,
            double shadowCutoff, double highlightCutoff)
        {
            double[] result = new double[brightness.Length];

            Parallel.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    double v = brightness[idx];

                    if (v < shadowCutoff) v = 0.0;
                    else if (v > highlightCutoff) v = 1.0;

                    result[idx] = v;
                }
            });

            return result;
        }

        private static byte[] RenderPerPixel(
            double[] adjusted, int sw, int sh,
            RenderSettings settings,
            IDitherEffect effect,
            IReadOnlyDictionary<string, double> effectParams,
            CancellationToken token)
        {
            byte[] hiRes = new byte[sw * sh];

            Parallel.For(0, sh, sy =>
            {
                if (token.IsCancellationRequested) return;

                for (int sx = 0; sx < sw; sx++)
                {
                    int idx = sy * sw + sx;
                    double brightness = adjusted[idx];

                    byte color;

                    if (brightness < settings.ShadowCutoff)
                    {
                        color = 0;
                    }
                    else if (brightness > settings.HighlightCutoff)
                    {
                        color = 255;
                    }
                    else
                    {
                        double fadeAtShadow = settings.ShadowCutoff <= 0.0
                            ? 1.0
                            : Clamp01((brightness - settings.ShadowCutoff) / FeatherWidth);

                        double fadeAtHighlight = settings.HighlightCutoff >= 1.0
                            ? 1.0
                            : Clamp01((settings.HighlightCutoff - brightness) / FeatherWidth);

                        double fade = System.Math.Min(fadeAtShadow, fadeAtHighlight);

                        var ctx = new EffectPixelContext
                        {
                            Brightness = brightness,
                            Fade = fade,
                            Sx = sx,
                            Sy = sy,
                            Sw = sw,
                            Sh = sh,
                            Params = effectParams
                        };

                        color = effect.ComputePixel(ctx);
                    }

                    if (settings.Invert) color = (byte)(255 - color);

                    hiRes[idx] = color;
                }
            });

            return hiRes;
        }

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
    }
}
