using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace dither_ex_machina.Models
{
    public class ColorGradient
    {
        private readonly List<GradientStopModel> _stops;

        public ColorGradient(IEnumerable<GradientStopModel> stops)
        {
            _stops = stops.OrderBy(s => s.Position).ToList();
        }

        public Color Sample(double t)
        {
            if (_stops.Count == 0)
            {
                byte v = (byte)(Math.Clamp(t, 0, 1) * 255);
                return Color.FromRgb(v, v, v);
            }
            if (_stops.Count == 1) return _stops[0].Color;

            t = Math.Clamp(t, 0.0, 1.0);

            if (t <= _stops[0].Position) return _stops[0].Color;
            if (t >= _stops[^1].Position) return _stops[^1].Color;

            for (int i = 0; i < _stops.Count - 1; i++)
            {
                var a = _stops[i];
                var b = _stops[i + 1];

                if (t >= a.Position && t <= b.Position)
                {
                    double range = b.Position - a.Position;
                    double local = range < 1e-9 ? 0 : (t - a.Position) / range;

                    byte r = (byte)(a.Color.R + (b.Color.R - a.Color.R) * local);
                    byte g = (byte)(a.Color.G + (b.Color.G - a.Color.G) * local);
                    byte bl = (byte)(a.Color.B + (b.Color.B - a.Color.B) * local);

                    return Color.FromRgb(r, g, bl);
                }
            }

            return _stops[^1].Color;
        }

        public (byte[] r, byte[] g, byte[] b) BuildLut(int size = 256)
        {
            byte[] r = new byte[size];
            byte[] g = new byte[size];
            byte[] b = new byte[size];

            for (int i = 0; i < size; i++)
            {
                double t = i / (double)(size - 1);
                var c = Sample(t);
                r[i] = c.R;
                g[i] = c.G;
                b[i] = c.B;
            }

            return (r, g, b);
        }
    }
}