using dither_ex_machina.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace dither_ex_machina.Effects
{
    public class HalftoneEffect : IDitherEffect
    {
        public string DisplayName => "Halftone (растровые точки)";

        public IReadOnlyList<ParameterDefinition> Parameters { get; } = new List<ParameterDefinition>
        {
            new ParameterDefinition("CellSize", "Размер ячейки", 4, 60, 16, "F0"),
            new ParameterDefinition("Angle", "Угол сетки (градусы)", 0, 90, 15, "F0"),
            new ParameterDefinition("MaxDotScale", "Макс. размер точки", 0.5, 1.0, 0.95, "F2"),
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
            Supersample = 2,
            Invert = false
        };
        public byte ComputePixel(EffectPixelContext ctx)
        {
            double cellSize = ctx.Params["CellSize"];
            double angleDeg = ctx.Params["Angle"];
            double maxDotScale = ctx.Params["MaxDotScale"];

            double rad = angleDeg * Math.PI / 180.0;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);

            double rx = ctx.Sx * cos + ctx.Sy * sin;
            double ry = -ctx.Sx * sin + ctx.Sy * cos;

            double cellX = Math.Floor(rx / cellSize);
            double cellY = Math.Floor(ry / cellSize);
            double centerX = (cellX + 0.5) * cellSize;
            double centerY = (cellY + 0.5) * cellSize;

            double dx = rx - centerX;
            double dy = ry - centerY;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            double darkness = 1.0 - ctx.Brightness;
            double maxRadius = cellSize * 0.5 * maxDotScale;
            double radius = maxRadius * darkness * ctx.Fade;

            return dist < radius ? (byte)0 : (byte)255;
        }
    }
}
