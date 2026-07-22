using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dither_ex_machina.Rendering;

namespace dither_ex_machina.Effects
{

    public class OscilloscopeEffect : ISequentialDitherEffect
    {
        public string DisplayName => "Oscilloscope (Осциллограф)";

        public IReadOnlyList<ParameterDefinition> Parameters { get; } = new List<ParameterDefinition>
        {
            new ParameterDefinition("LineSpacing", "Расстояние между линиями (px)", 4, 60, 10, "F0"),
            new ParameterDefinition("Angle", "Угол линий (0=вертикальные, 90=горизонтальные)", -90, 90, 0, "F0"),        
            new ParameterDefinition("Displacement", "Сила выгибания (объем)", -100, 100, 30, "F1"),
            new ParameterDefinition("Smoothness", "Сглаживание формы объекта (px)", 0, 40, 5, "F0"),
            new ParameterDefinition("Sharpness", "Тонкость линий (больше = тоньше)", 0.1, 10.0, 2.5, "F1"),
            new ParameterDefinition("Masking", "Скрыть фон (0=линии везде, 1=только на объекте)", 0.0, 1.0, 0.85, "F2"),

            new ParameterDefinition("PhaseShift", "Сдвиг фазы", 0, 1, 0, "F2"),

        };

        public RenderSettings DefaultSettings { get; } = new RenderSettings
        {
            MedianRadius = 0,
            BlurRadius = 0,
            ContrastCenter = 0.5,
            ContrastSteepness = 1.3,
            PostContrastSmoothRadius = 0,
            ShadowCutoff = 0.02,
            HighlightCutoff = 0.98,
            Supersample = 1,
            Invert = false
        };

        public byte ComputePixel(EffectPixelContext ctx) => 255;

        public byte[] ComputeFullImage(
            double[] brightness, int width, int height,
            IReadOnlyDictionary<string, double> parameters,
            CancellationToken token)
        {
            double lineSpacing = Math.Max(2.0, parameters["LineSpacing"]);
            double angleDeg = parameters["Angle"];
            double displacement = parameters["Displacement"];
            int smoothness = (int)parameters["Smoothness"];
            double sharpness = Math.Max(0.1, parameters["Sharpness"]);
            double masking = Math.Clamp(parameters["Masking"], 0.0, 1.0);
            double phaseShift = parameters["PhaseShift"];


            var kernel = new[]
                {
                    new KernelTap(1, 0, 1.0 / 8), new KernelTap(2, 0, 1.0 / 8),
                    new KernelTap(-1, 1, 1.0 / 8), new KernelTap(0, 1, 1.0 / 8), new KernelTap(1, 1, 1.0 / 8),
                    new KernelTap(0, 2, 1.0 / 8),
                };

            double angleRad = angleDeg * Math.PI / 180.0;
            double cosA = Math.Cos(angleRad);
            double sinA = Math.Sin(angleRad);

            int n = width * height;

            double[] smoothedMap = smoothness > 0
                ? BoxBlurDouble(brightness, width, height, smoothness)
                : brightness;

            double[] work = new double[n];

            //процедурная генерация искаженных линий
            Parallel.For(0, height, y =>
            {
                if (token.IsCancellationRequested) return;
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    double b = smoothedMap[idx];
                    double proj = x * cosA + y * sinA;
                    double displacedProj = proj - (b * displacement);
                    double phase = (displacedProj / lineSpacing) + phaseShift;
                    double wave = (Math.Cos(2.0 * Math.PI * phase) + 1.0) / 2.0;
                    wave = Math.Pow(wave, sharpness);
                    double maskFactor = 1.0 - masking + (masking * brightness[idx]);
                    work[idx] = wave * maskFactor;
                }
            });

            byte[] output = new byte[n];

            //дизеринг сгенерированной волны
            for (int y = 0; y < height; y++)
            {
                if (token.IsCancellationRequested) return output;

                bool leftToRight = y % 2 != 0;
                int xStart = leftToRight ? 0 : width - 1;
                int xEnd = leftToRight ? width : -1;
                int xStep = leftToRight ? 1 : -1;

                for (int x = xStart; x != xEnd; x += xStep)
                {
                    int idx = y * width + x;
                    double oldValue = work[idx];

                    double threshold = 0.5;
                    double newValue = oldValue < threshold ? 0.0 : 1.0;

                    output[idx] = newValue > 0.5 ? (byte)255 : (byte)0;

                    double error = oldValue - newValue;
                    if (error == 0.0) continue;

                    foreach (var tap in kernel)
                    {
                        int dx = leftToRight ? tap.Dx : -tap.Dx;
                        int nx = x + dx;
                        int ny = y + tap.Dy;
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                        work[ny * width + nx] += error * tap.Weight;
                    }
                }
            }

            output = DilateInk(output, width, height, token);

            return output;
        }

        private static byte[] DilateInk(byte[] binary, int width, int height, CancellationToken token)
        {
            int r = 0;
            double r2 = 0.25;
            byte[] result = new byte[binary.Length];

            Parallel.For(0, height, y =>
            {
                if (token.IsCancellationRequested) return;
                for (int x = 0; x < width; x++)
                {
                    bool ink = false;
                    for (int dy = -r; dy <= r && !ink; dy++)
                    {
                        int ny = y + dy;
                        if (ny < 0 || ny >= height) continue;
                        int rowBase = ny * width;
                        for (int dx = -r; dx <= r; dx++)
                        {
                            if (dx * dx + dy * dy > r2) continue;
                            int nx = x + dx;
                            if (nx < 0 || nx >= width) continue;
                            if (binary[rowBase + nx] == 0)
                            {
                                ink = true;
                                break;
                            }
                        }
                    }
                    result[y * width + x] = ink ? (byte)0 : (byte)255;
                }
            });
            return result;
        }

        private static double[] BoxBlurDouble(double[] src, int width, int height, int radius)
        {
            double[] temp = new double[src.Length];
            double[] result = new double[src.Length];

            Parallel.For(0, height, y =>
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    double sum = 0;
                    int count = 0;
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int nx = x + dx;
                        if (nx < 0 || nx >= width) continue;
                        sum += src[row + nx];
                        count++;
                    }
                    temp[row + x] = sum / count;
                }
            });

            Parallel.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    double sum = 0;
                    int count = 0;
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        int ny = y + dy;
                        if (ny < 0 || ny >= height) continue;
                        sum += temp[ny * width + x];
                        count++;
                    }
                    result[y * width + x] = sum / count;
                }
            });

            return result;
        }

        private readonly struct KernelTap
        {
            public readonly int Dx, Dy;
            public readonly double Weight;
            public KernelTap(int dx, int dy, double weight)
            {
                Dx = dx;
                Dy = dy;
                Weight = weight;
            }
        }
    }
}