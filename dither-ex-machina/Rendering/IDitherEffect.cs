using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace dither_ex_machina.Rendering
{
    public interface IDitherEffect
    {
        string DisplayName { get; }
        IReadOnlyList<ParameterDefinition> Parameters { get; }
        RenderSettings DefaultSettings { get; } 

        byte ComputePixel(EffectPixelContext ctx);
    }
}
