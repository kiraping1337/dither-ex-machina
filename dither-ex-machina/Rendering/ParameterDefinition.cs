using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace dither_ex_machina.Rendering
{
    public class ParameterDefinition
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double Default { get; set; }
        public double TickFrequency { get; set; } = 0;
        public string Format { get; set; } = "F2";

        public ParameterDefinition(string key, string label, double min, double max, double def,
            string format = "F2", double tickFrequency = 0)
        {
            Key = key;
            Label = label;
            Min = min;
            Max = max;
            Default = def;
            Format = format;
            TickFrequency = tickFrequency;
        }
    }
}
