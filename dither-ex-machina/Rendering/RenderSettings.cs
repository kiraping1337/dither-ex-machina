using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace dither_ex_machina.Rendering
{
    public class RenderSettings
    {
        public int MedianRadius { get; set; }
        public int BlurRadius { get; set; }
        public double ContrastCenter { get; set; }
        public double ContrastSteepness { get; set; }
        public int PostContrastSmoothRadius { get; set; }
        public double ShadowCutoff { get; set; }
        public double HighlightCutoff { get; set; }
        public int Supersample { get; set; }
        public bool Invert { get; set; }
        public bool GlowEnabled { get; set; }
        public int GlowRadius { get; set; }
        public double GlowThreshold { get; set; }
        public double GlowIntensity { get; set; }
    }
}
