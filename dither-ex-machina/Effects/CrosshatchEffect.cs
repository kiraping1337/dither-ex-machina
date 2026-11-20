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
        public string DisplayNameKey => "Effect.Crosshatch";

        public IReadOnlyList<ParameterDefinition> Parameters { get; } = new List<ParameterDefinition>
        {
            new ParameterDefinition("Spacing", "Effect.Crosshatch.Spacing", 2, 40, 10, "F1"),
            new ParameterDefinition("LineThickness", "Effect.Crosshatch.LineThickness", 0.02, 0.5, 0.15, "F2"),
            new ParameterDefinition("Angle", "Effect.Crosshatch.Angle", 0, 90, 15, "F0"),
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

        public byte ComputePixel(EffectPixelContext ctx) => Prepare(ctx.Params)(ctx);

        public Func<EffectPixelContext, byte> Prepare(IReadOnlyDictionary<string, double> parameters)
        {
            double spacing = parameters["Spacing"];
            double thickness = parameters["LineThickness"];
            double baseAngle = parameters["Angle"];

            var directions = LayerAngleOffsets.Select(offset =>
            {
                double radians = (baseAngle + offset) * Math.PI / 180.0;
                return Math.SinCos(radians);
            }).ToArray();

            return ctx =>
            {
                double darkness = 1.0 - ctx.Brightness;

                for (int i = 0; i < LayerAngleOffsets.Length; i++)
                {
                    double bandStart = i / (double)LayerAngleOffsets.Length;
                    double bandEnd = (i + 1) / (double)LayerAngleOffsets.Length;

                    double activation = Math.Clamp(
                        (darkness - bandStart) / (bandEnd - bandStart), 0, 1);

                    if (activation <= 0) continue;

                    var (sin, cos) = directions[i];
                    double u = ctx.Sx * cos + ctx.Sy * sin;

                    double phase = u / spacing;
                    double frac = phase - Math.Floor(phase);
                    double dist = Math.Min(frac, 1.0 - frac);

                    double effectiveThickness = thickness * ctx.Fade * activation;

                    if (dist < effectiveThickness)
                        return 0;
                }

                return 255;
            };
        }
    }
}
