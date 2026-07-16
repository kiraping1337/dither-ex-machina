using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace dither_ex_machina.Rendering
{
    public static class DitherRenderer
    {
        private const double FeatherWidth = 0.08;
        private const int DefaultTileSize = 2048; //количество исходных пикселей на тайл (больше = меньше границ)
        private const int MinOverlapSrc = 8; // минимальное перекрытие в исходных пикселях
        private static readonly ParallelOptions ParallelOptions = new() { MaxDegreeOfParallelism = Environment.ProcessorCount };

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

            if (effect is ISequentialDitherEffect sequentialEffect)
            {
                return RenderSingleTile(toneMap, width, height, sw, sh, supersample, settings, sequentialEffect, effectParams, gradientLut, token);
            }
            if (width <= DefaultTileSize && height <= DefaultTileSize)
            {
                return RenderSingleTile(toneMap, width, height, sw, sh, supersample, settings, effect, effectParams, gradientLut, token);
            }

            return RenderTiled(toneMap, width, height, sw, sh, supersample, settings, effect, effectParams, gradientLut, token);
        }

        private static byte[] RenderSingleTile(
            byte[] toneMap, int width, int height,
            int sw, int sh, int supersample,
            RenderSettings settings,
            IDitherEffect effect,
            IReadOnlyDictionary<string, double> effectParams,
            (byte[] r, byte[] g, byte[] b)? gradientLut,
            CancellationToken token)
        {
            double[] upsampled = ImageOps.UpsampleBilinear(toneMap, width, height, sw, sh);

            Parallel.For(0, sh, ParallelOptions, sy =>
            {
                int rowOffset = sy * sw;
                for (int sx = 0; sx < sw; sx++)
                {
                    int idx = rowOffset + sx;
                    upsampled[idx] = ImageOps.ApplyContrastCurve(upsampled[idx], settings.ContrastCenter, settings.ContrastSteepness);
                }
            });

            if (settings.PostContrastSmoothRadius > 0)
            {
                double[] blurred = ImageOps.BoxBlurDouble(upsampled, sw, sh, settings.PostContrastSmoothRadius);
                upsampled = blurred;
            }

            byte[] hiRes;

            if (effect is ISequentialDitherEffect sequentialEffect)
            {
                double[] clamped = ApplyHardCutoff(upsampled, sw, sh, settings.ShadowCutoff, settings.HighlightCutoff);
                upsampled = null;
                hiRes = sequentialEffect.ComputeFullImage(clamped, sw, sh, effectParams, token);
                clamped = null;

                if (settings.Invert)
                {
                    Parallel.For(0, hiRes.Length, ParallelOptions, i => hiRes[i] = (byte)(255 - hiRes[i]));
                }
            }
            else
            {
                hiRes = RenderTilePerPixelFull(upsampled, sw, sh, settings, effect, effectParams, token);
                upsampled = null;
            }

            byte[] grayOutput = ImageOps.DownsampleGray(hiRes, sw, sh, width, height, supersample);
            hiRes = null;

            return gradientLut.HasValue
                ? ImageOps.ApplyGradientLut(grayOutput, width, height, gradientLut.Value.r, gradientLut.Value.g, gradientLut.Value.b)
                : ImageOps.GrayToBgra(grayOutput, width, height);
        }
        //тайловая обработка для оптимизации рендера
        private static byte[] RenderTiled(
            byte[] toneMap, int width, int height,
            int sw, int sh, int supersample,
            RenderSettings settings,
            IDitherEffect effect,
            IReadOnlyDictionary<string, double> effectParams,
            (byte[] r, byte[] g, byte[] b)? gradientLut,
            CancellationToken token)
        {
            //размер тайла в исходных пикселях
            int tileSize = DefaultTileSize;
            int tilesX = (width + tileSize - 1) / tileSize;
            int tilesY = (height + tileSize - 1) / tileSize;

            int blurRadius = settings.PostContrastSmoothRadius;
            int blurOverlapSrc = blurRadius > 0 ? (blurRadius + supersample - 1) / supersample : 0;
            int overlapSrc = Math.Max(MinOverlapSrc, 1 + blurOverlapSrc + 4);

            byte[] finalOutput = new byte[width * height * 4];

            int totalTiles = tilesY * tilesX;
            Parallel.For(0, totalTiles, ParallelOptions, tileIdx =>
            {
                if (token.IsCancellationRequested) return;

                int ty = tileIdx / tilesX;
                int tx = tileIdx % tilesX;

                int srcX = tx * tileSize;
                int srcY = ty * tileSize;
                int srcW = Math.Min(tileSize, width - srcX);
                int srcH = Math.Min(tileSize, height - srcY);

                int ovlSrcX = Math.Max(0, srcX - overlapSrc);
                int ovlSrcY = Math.Max(0, srcY - overlapSrc);
                int ovlSrcW = Math.Min(width - ovlSrcX, srcW + 2 * overlapSrc);
                int ovlSrcH = Math.Min(height - ovlSrcY, srcH + 2 * overlapSrc);

                int ssX = ovlSrcX * supersample;
                int ssY = ovlSrcY * supersample;
                int ssW = ovlSrcW * supersample;
                int ssH = ovlSrcH * supersample;

                byte[] tileToneMap = new byte[ovlSrcW * ovlSrcH];
                for (int y = 0; y < ovlSrcH; y++)
                {
                    int srcRow = (ovlSrcY + y) * width + ovlSrcX;
                    int dstRow = y * ovlSrcW;
                    Buffer.BlockCopy(toneMap, srcRow, tileToneMap, dstRow, ovlSrcW);
                }

                double[] upsampled = ImageOps.UpsampleBilinear(tileToneMap, ovlSrcW, ovlSrcH, ssW, ssH);
                tileToneMap = null;

                for (int i = 0; i < upsampled.Length; i++)
                {
                    upsampled[i] = ImageOps.ApplyContrastCurve(upsampled[i], settings.ContrastCenter, settings.ContrastSteepness);
                }

                if (blurRadius > 0)
                {
                    double[] blurred = ImageOps.BoxBlurDouble(upsampled, ssW, ssH, blurRadius);
                    upsampled = blurred;
                }

                int validSsX = (srcX - ovlSrcX) * supersample;
                int validSsY = (srcY - ovlSrcY) * supersample;
                int validSsW = srcW * supersample;
                int validSsH = srcH * supersample;

                byte[] tileHiRes = new byte[validSsW * validSsH];
                RenderTilePerPixel(upsampled, ssW, ssH, validSsX, validSsY, validSsW, validSsH,
                    ssX, ssY,              
                    sw, sh,
                    settings, effect, effectParams, tileHiRes, token);
                upsampled = null;

                byte[] tileGray = ImageOps.DownsampleGray(tileHiRes, validSsW, validSsH, srcW, srcH, supersample, useParallel: false);
                tileHiRes = null;

                int dstBaseY = srcY;
                int dstBaseX = srcX;
                if (gradientLut.HasValue)
                {
                    byte[] lutR = gradientLut.Value.r;
                    byte[] lutG = gradientLut.Value.g;
                    byte[] lutB = gradientLut.Value.b;
                    for (int y = 0; y < srcH; y++)
                    {
                        int srcRow = y * srcW;
                        int dstRow = (dstBaseY + y) * width + dstBaseX;
                        int dstIdx = dstRow * 4;
                        for (int x = 0; x < srcW; x++)
                        {
                            byte v = tileGray[srcRow + x];
                            finalOutput[dstIdx++] = lutB[v];
                            finalOutput[dstIdx++] = lutG[v];
                            finalOutput[dstIdx++] = lutR[v];
                            finalOutput[dstIdx++] = 255;
                        }
                    }
                }
                else
                {
                    for (int y = 0; y < srcH; y++)
                    {
                        int srcRow = y * srcW;
                        int dstRow = (dstBaseY + y) * width + dstBaseX;
                        int dstIdx = dstRow * 4;
                        for (int x = 0; x < srcW; x++)
                        {
                            byte v = tileGray[srcRow + x];
                            finalOutput[dstIdx++] = v;
                            finalOutput[dstIdx++] = v;
                            finalOutput[dstIdx++] = v;
                            finalOutput[dstIdx++] = 255;
                        }
                    }
                }
                tileGray = null;
            });

            return finalOutput;
        }

        private static void RenderTilePerPixel(
            double[] adjusted, int adjW, int adjH,
            int validX, int validY, int validW, int validH,
            int originSsX, int originSsY,   
            int fullSw, int fullSh,
            RenderSettings settings,
            IDitherEffect effect,
            IReadOnlyDictionary<string, double> effectParams,
            byte[] output,
            CancellationToken token)
        {
            double shadowCutoff = settings.ShadowCutoff;
            double highlightCutoff = settings.HighlightCutoff;
            bool invert = settings.Invert;
            double invFeatherWidth = 1.0 / FeatherWidth;

            int totalValid = validW * validH;

            Parallel.For(0, totalValid, ParallelOptions, idx =>
            {
                if (token.IsCancellationRequested) return;

                int vy = idx / validW;
                int vx = idx - vy * validW;

                int sy = validY + vy;
                int sx = validX + vx;
                int adjIdx = sy * adjW + sx;

                double brightness = adjusted[adjIdx];

                byte color;

                if (brightness < shadowCutoff)
                {
                    color = 0;
                }
                else if (brightness > highlightCutoff)
                {
                    color = 255;
                }
                else
                {
                    int globalSx = originSsX + sx;
                    int globalSy = originSsY + sy;

                    double fadeAtShadow = shadowCutoff <= 0.0
                        ? 1.0
                        : Clamp01((brightness - shadowCutoff) * invFeatherWidth);

                    double fadeAtHighlight = highlightCutoff >= 1.0
                        ? 1.0
                        : Clamp01((highlightCutoff - brightness) * invFeatherWidth);

                    double fade = fadeAtShadow < fadeAtHighlight ? fadeAtShadow : fadeAtHighlight;

                    var ctx = new EffectPixelContext
                    {
                        Brightness = brightness,
                        Fade = fade,
                        Sx = globalSx,
                        Sy = globalSy,
                        Sw = fullSw,
                        Sh = fullSh,
                        Params = effectParams
                    };

                    color = effect.ComputePixel(ctx);
                }

                if (invert) color = (byte)(255 - color);

                output[idx] = color;
            });
        }

        private static byte[] RenderTilePerPixelFull(
            double[] adjusted, int sw, int sh,
            RenderSettings settings,
            IDitherEffect effect,
            IReadOnlyDictionary<string, double> effectParams,
            CancellationToken token)
        {
            int totalPixels = sw * sh;
            byte[] hiRes = new byte[totalPixels];

            double shadowCutoff = settings.ShadowCutoff;
            double highlightCutoff = settings.HighlightCutoff;
            bool invert = settings.Invert;
            double invFeatherWidth = 1.0 / FeatherWidth;

            Parallel.For(0, totalPixels, ParallelOptions, idx =>
            {
                if (token.IsCancellationRequested) return;

                double brightness = adjusted[idx];
                byte color;

                if (brightness < shadowCutoff)
                {
                    color = 0;
                }
                else if (brightness > highlightCutoff)
                {
                    color = 255;
                }
                else
                {
                    int sy = idx / sw;
                    int sx = idx - sy * sw;

                    double fadeAtShadow = shadowCutoff <= 0.0
                        ? 1.0
                        : Clamp01((brightness - shadowCutoff) * invFeatherWidth);

                    double fadeAtHighlight = highlightCutoff >= 1.0
                        ? 1.0
                        : Clamp01((highlightCutoff - brightness) * invFeatherWidth);

                    double fade = fadeAtShadow < fadeAtHighlight ? fadeAtShadow : fadeAtHighlight;

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

                if (invert) color = (byte)(255 - color);
                hiRes[idx] = color;
            });

            return hiRes;
        }

        private static double[] ApplyHardCutoff(
            double[] brightness, int width, int height,
            double shadowCutoff, double highlightCutoff)
        {
            double[] result = new double[brightness.Length];

            Parallel.For(0, height, ParallelOptions, y =>
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    int idx = rowOffset + x;
                    double v = brightness[idx];

                    if (v < shadowCutoff) v = 0.0;
                    else if (v > highlightCutoff) v = 1.0;

                    result[idx] = v;
                }
            });

            return result;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
    }
}
