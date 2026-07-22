using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dither_ex_machina.Rendering;

namespace dither_ex_machina.Effects
{
    public class IsolinesModulation : ISequentialDitherEffect
    {
        public string DisplayName => "Contour Modulation (Контурная модуляция)";

        public IReadOnlyList<ParameterDefinition> Parameters { get; } = new List<ParameterDefinition>
        {
            new ParameterDefinition("Levels", "Количество изолиний (уровней)", 2, 30, 8, "F1"),
            new ParameterDefinition("LineWidth", "Ширина зоны влияния линии", 0.05, 0.5, 0.25, "F2"),
            new ParameterDefinition("PhaseShift", "Сдвиг фазы линий", 0, 1, 0, "F2"),
            new ParameterDefinition("Amplitude", "Сила модуляции (интенсивность узора)", 0.0, 0.45, 0.22, "F2"),
            new ParameterDefinition("Falloff", "Резкость перехода у краёв линии", 0.5, 6.0, 2.0, "F2"),
            new ParameterDefinition("Polarity", "Полярность узора (-1 линии тёмные / +1 линии светлые)", -1, 1, 1, "F1"),
            new ParameterDefinition("PhaseSmoothness", "Сглаживание линий (px)", 0, 25, 4, "F0"),
        };

        public RenderSettings DefaultSettings { get; } = new RenderSettings
        {
            MedianRadius = 0,
            BlurRadius = 0,
            ContrastCenter = 0.5,
            ContrastSteepness = 1,
            PostContrastSmoothRadius = 0,
            ShadowCutoff = 0,
            HighlightCutoff = 1,
            Supersample = 1,
            Invert = false
        };

        public byte ComputePixel(EffectPixelContext ctx) => 255;

        public byte[] ComputeFullImage(
            double[] brightness, int width, int height,
            IReadOnlyDictionary<string, double> parameters,
            CancellationToken token)
        {
            double levels = parameters["Levels"];
            double lineWidth = Math.Clamp(parameters["LineWidth"], 0.01, 0.49);
            double phaseShift = parameters["PhaseShift"];
            double amplitude = Math.Clamp(parameters["Amplitude"], 0.0, 0.45);
            double falloff = parameters["Falloff"];
            double polarity = Math.Clamp(parameters["Polarity"], -1.0, 1.0);
            int phaseSmoothness = (int)parameters["PhaseSmoothness"];

            var kernel = new[]
                {
                    new KernelTap(1, 0, 1.0 / 8),
                    new KernelTap(2, 0, 1.0 / 8),
                    new KernelTap(-1, 1, 1.0 / 8),
                    new KernelTap(0, 1, 1.0 / 8),
                    new KernelTap(1, 1, 1.0 / 8),
                    new KernelTap(0, 2, 1.0 / 8),
                };

            double[] phaseSource = phaseSmoothness > 0
                ? BoxBlurDouble(brightness, width, height, phaseSmoothness)
                : brightness;

            //предрасчитываем локальный сдвиг порога для каждого пикселя
            double[] thresholdShift = new double[width * height];
            Parallel.For(0, height, y =>
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int idx = row + x;
                    double b = phaseSource[idx];

                    double phase = b * levels + phaseShift;
                    double frac = phase - Math.Floor(phase);
                    double distToLine = Math.Min(frac, 1.0 - frac);

                    double norm = Math.Clamp(1.0 - distToLine / lineWidth, 0.0, 1.0);
                    double shaped = Math.Pow(norm, falloff);

                    thresholdShift[idx] = shaped * amplitude * polarity;
                }
            });

            double[] work = (double[])brightness.Clone();
            byte[] output = new byte[width * height];

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

                    double threshold = 0.5 + thresholdShift[idx];
                    threshold = Math.Clamp(threshold, 0.02, 0.98);

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

            return output;
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