using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace dither_ex_machina.Rendering
{
    public struct EffectPixelContext
    {
        public double Brightness;   
        public double Fade;         
        public int Sx, Sy;          // координаты в суперсэмплированном пространстве
        public int Sw, Sh;          // размеры суперсэмплированного холста
        public IReadOnlyDictionary<string, double> Params;
    }
}
