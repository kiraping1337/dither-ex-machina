using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace dither_ex_machina.Rendering
{
    public interface IDitherEffect
    {
        string DisplayNameKey { get; }
        IReadOnlyList<ParameterDefinition> Parameters { get; }
        RenderSettings DefaultSettings { get; } 

        Func<EffectPixelContext, byte> Prepare(IReadOnlyDictionary<string, double> parameters)
            => ComputePixel;

        byte ComputePixel(EffectPixelContext ctx);
    }
}
