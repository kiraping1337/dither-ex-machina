using dither_ex_machina.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace dither_ex_machina.Effects
{
    public class ContourLinesEffect : IDitherEffect
    {
        public string DisplayName => "Contour Lines (Изолинии)";

        public IReadOnlyList<ParameterDefinition> Parameters { get; } = new List<ParameterDefinition>
        {
            new ParameterDefinition("Levels", "Количество линий (уровней)", 2, 40, 10, "F1"),
            new ParameterDefinition("LineWidth", "Толщина линий", 0.02, 0.4, 0.18, "F2"),
            new ParameterDefinition("PhaseShift", "Сдвиг фазы линий", 0, 1, 0, "F2"),
            new ParameterDefinition("DarkBoost", "Доп. толщина в тенях", 0, 1, 0.15, "F2"),
        };
        public RenderSettings DefaultSettings { get; } = new RenderSettings
        {
            MedianRadius = 3,
            BlurRadius = 3,
            ContrastCenter = 0.5,
            ContrastSteepness = 1,
            PostContrastSmoothRadius = 2,
            ShadowCutoff = 0.05,
            HighlightCutoff = 0.92,
            Supersample = 3,
            Invert = false
        };

        public byte ComputePixel(EffectPixelContext ctx)
        {
            double levels = ctx.Params["Levels"];
            double phaseShift = ctx.Params["PhaseShift"];
            double lineWidthBase = ctx.Params["LineWidth"];
            double darkBoost = ctx.Params["DarkBoost"];

            double phase = ctx.Brightness * levels + phaseShift;
            double frac = phase - Math.Floor(phase);
            double distToLine = Math.Min(frac, 1.0 - frac);

            double lineWidth = Math.Clamp(
                (lineWidthBase + darkBoost * (1.0 - ctx.Brightness) * 0.5) * ctx.Fade,
                0.0, 0.49);

            return distToLine < lineWidth ? (byte)0 : (byte)255;
        }
    }
}