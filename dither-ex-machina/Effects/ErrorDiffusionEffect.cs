using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using dither_ex_machina.Rendering;

namespace dither_ex_machina.Effects
{
    public class ErrorDiffusionEffect : ISequentialDitherEffect
    {
        public string DisplayName => "Error Diffusion (диффузия ошибки)";

        public IReadOnlyList<ParameterDefinition> Parameters { get; } = new List<ParameterDefinition>
        {
            // 0 = Floyd-Steinberg, 1 = Atkinson, 2 = Jarvis-Judice-Ninke, 3 = Sierra
            new ParameterDefinition("Algorithm",
                "Алгоритм (0=Floyd-Steinberg, 1=Atkinson, 2=JJN, 3=Sierra)",
                0, 3, 0, "F0", tickFrequency: 1),

            // 0 = обычный порядок построчно, 1 = змейкой (чередование направления)
            new ParameterDefinition("Serpentine",
                "Змейка (0=выкл, 1=вкл)", 0, 1, 1, "F0", tickFrequency: 1),
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

        //этот метод формально требуется интерфейсом IDitherEffect, но для последовательных эффектов он не вызывается 
        public byte ComputePixel(EffectPixelContext ctx) => 255;

        public byte[] ComputeFullImage(
            double[] brightness, int width, int height,
            IReadOnlyDictionary<string, double> parameters,
            CancellationToken token)
        {
            int algorithmIndex = (int)parameters["Algorithm"];
            bool serpentine = parameters["Serpentine"] >= 0.5;

            var kernel = GetKernel(algorithmIndex);

            double[] work = (double[])brightness.Clone();
            byte[] output = new byte[width * height];

            for (int y = 0; y < height; y++)
            {
                if (token.IsCancellationRequested) return output;

                bool leftToRight = !serpentine || (y % 2 == 0);
                int xStart = leftToRight ? 0 : width - 1;
                int xEnd = leftToRight ? width : -1;
                int xStep = leftToRight ? 1 : -1;

                for (int x = xStart; x != xEnd; x += xStep)
                {
                    int idx = y * width + x;
                    double oldValue = work[idx];
                    double newValue = oldValue < 0.5 ? 0.0 : 1.0;

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

        private static IReadOnlyList<KernelTap> GetKernel(int algorithmIndex)
        {
            switch (algorithmIndex)
            {
                case 1: //Atkinson
                    return new[]
                    {
                        new KernelTap(1, 0, 1.0 / 8),
                        new KernelTap(2, 0, 1.0 / 8),
                        new KernelTap(-1, 1, 1.0 / 8),
                        new KernelTap(0, 1, 1.0 / 8),
                        new KernelTap(1, 1, 1.0 / 8),
                        new KernelTap(0, 2, 1.0 / 8),
                    };

                case 2: //Jarvis-Judice-Ninke
                    return new[]
                    {
                        new KernelTap(1, 0, 7.0 / 48),
                        new KernelTap(2, 0, 5.0 / 48),

                        new KernelTap(-2, 1, 3.0 / 48),
                        new KernelTap(-1, 1, 5.0 / 48),
                        new KernelTap(0, 1, 7.0 / 48),
                        new KernelTap(1, 1, 5.0 / 48),
                        new KernelTap(2, 1, 3.0 / 48),

                        new KernelTap(-2, 2, 1.0 / 48),
                        new KernelTap(-1, 2, 3.0 / 48),
                        new KernelTap(0, 2, 5.0 / 48),
                        new KernelTap(1, 2, 3.0 / 48),
                        new KernelTap(2, 2, 1.0 / 48),
                    };

                case 3: //Sierra
                    return new[]
                    {
                        new KernelTap(1, 0, 5.0 / 32),
                        new KernelTap(2, 0, 3.0 / 32),

                        new KernelTap(-2, 1, 2.0 / 32),
                        new KernelTap(-1, 1, 4.0 / 32),
                        new KernelTap(0, 1, 5.0 / 32),
                        new KernelTap(1, 1, 4.0 / 32),
                        new KernelTap(2, 1, 2.0 / 32),

                        new KernelTap(-1, 2, 2.0 / 32),
                        new KernelTap(0, 2, 3.0 / 32),
                        new KernelTap(1, 2, 2.0 / 32),
                    };

                default: //Floyd-Steinberg
                    return new[]
                    {
                        new KernelTap(1, 0, 7.0 / 16),
                        new KernelTap(-1, 1, 3.0 / 16),
                        new KernelTap(0, 1, 5.0 / 16),
                        new KernelTap(1, 1, 1.0 / 16),
                    };
            }
        }
    }
}
