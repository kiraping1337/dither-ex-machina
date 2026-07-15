using dither_ex_machina.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace dither_ex_machina.Effects
{
    public class BayerDitherEffect : IDitherEffect
    {
        public string DisplayName => "Bayer (упорядоченный дизеринг)";

        public IReadOnlyList<ParameterDefinition> Parameters { get; } = new List<ParameterDefinition>
        {
            new ParameterDefinition("MatrixSizeIndex",
                "Размер матрицы (0=2x2, 1=4x4, 2=8x8, 3=16x16)", 0, 3, 1, "F0", tickFrequency: 1),
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
        private static readonly Dictionary<int, int[,]> _matrixCache = new();

        public byte ComputePixel(EffectPixelContext ctx)
        {
            int sizeIndex = (int)ctx.Params["MatrixSizeIndex"];
            int n = sizeIndex switch
            {
                0 => 2,
                1 => 4,
                2 => 8,
                _ => 16
            };

            int[,] matrix = GetMatrix(n);

            int mx = ctx.Sx % n;
            int my = ctx.Sy % n;

            double threshold = (matrix[my, mx] + 0.5) / (n * n);

            return ctx.Brightness > threshold ? (byte)255 : (byte)0;
        }

        private static int[,] GetMatrix(int n)
        {
            if (_matrixCache.TryGetValue(n, out var cached))
                return cached;

            var matrix = GenerateBayerMatrix(n);
            _matrixCache[n] = matrix;
            return matrix;
        }

        private static int[,] GenerateBayerMatrix(int size)
        {
            if (size == 2)
                return new int[,] { { 0, 2 }, { 3, 1 } };

            int half = size / 2;
            int[,] smaller = GenerateBayerMatrix(half);
            int[,] result = new int[size, size];

            for (int y = 0; y < half; y++)
            {
                for (int x = 0; x < half; x++)
                {
                    int v = smaller[y, x];
                    result[y, x] = 4 * v;
                    result[y, x + half] = 4 * v + 2;
                    result[y + half, x] = 4 * v + 3;
                    result[y + half, x + half] = 4 * v + 1;
                }
            }

            return result;
        }
    }
}
