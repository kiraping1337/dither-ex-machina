using dither_ex_machina.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace dither_ex_machina.Effects
{
    public class CrosshatchEffect : IDitherEffect
    {
        public string DisplayName => "Crosshatch (штриховка)";

        public IReadOnlyList<ParameterDefinition> Parameters { get; } = new List<ParameterDefinition>
        {
            new ParameterDefinition("Spacing", "Расстояние между линиями", 2, 40, 10, "F1"),
            new ParameterDefinition("LineThickness", "Толщина линий", 0.02, 0.5, 0.15, "F2"),
            new ParameterDefinition("Angle", "Угол наклона (градусы)", 0, 90, 15, "F0"),
        };
        public RenderSettings DefaultSettings { get; } = new RenderSettings
        {
            MedianRadius = 1,
            BlurRadius = 0,
            ContrastCenter = 0.5,
            ContrastSteepness = 1,
            PostContrastSmoothRadius = 0,
            ShadowCutoff = 0,
            HighlightCutoff = 1,
            Supersample = 2,
            Invert = false
        };
        private static readonly double[] LayerAngleOffsets = { 0, 45, 90, 135 };

        public byte ComputePixel(EffectPixelContext ctx)
        {
            double spacing = ctx.Params["Spacing"];
            double thickness = ctx.Params["LineThickness"];
            double baseAngle = ctx.Params["Angle"];

            double darkness = 1.0 - ctx.Brightness;

            for (int i = 0; i < LayerAngleOffsets.Length; i++)
            {
                double bandStart = i / (double)LayerAngleOffsets.Length;
                double bandEnd = (i + 1) / (double)LayerAngleOffsets.Length;

                double activation = Math.Clamp(
                    (darkness - bandStart) / (bandEnd - bandStart), 0, 1);

                if (activation <= 0) continue;

                double angleDeg = baseAngle + LayerAngleOffsets[i];
                double rad = angleDeg * Math.PI / 180.0;
                double cos = Math.Cos(rad);
                double sin = Math.Sin(rad);

                double u = ctx.Sx * cos + ctx.Sy * sin;

                double phase = u / spacing;
                double frac = phase - Math.Floor(phase);
                double dist = Math.Min(frac, 1.0 - frac);

                double effectiveThickness = thickness * ctx.Fade * activation;

                if (dist < effectiveThickness)
                    return 0;
            }

            return 255;
        }
    }
}
