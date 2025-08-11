using System.Windows.Media;
using OxyPlot;

namespace BlockViz.Applications.Models
{
    public static class OxyColorExtensions
    {
        public static OxyColor ToOxyColor(this Color color)
        {
            return OxyColor.FromArgb(color.A, color.R, color.G, color.B);
        }
    }
}
