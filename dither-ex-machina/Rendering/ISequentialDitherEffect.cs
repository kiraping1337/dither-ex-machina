using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace dither_ex_machina.Rendering
{

    //для эффектов, которые не могут быть посчитаны независимо по пикселям
    public interface ISequentialDitherEffect : IDitherEffect
    {
        byte[] ComputeFullImage(
            double[] brightness, int width, int height,
            IReadOnlyDictionary<string, double> parameters,
            CancellationToken token);
    }
}
