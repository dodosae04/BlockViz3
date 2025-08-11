using System.ComponentModel;

namespace BlockViz.Applications.Models
{
    public enum LabelMode
    {
        PercentOnly,
        Off
    }

    /// <summary>
    /// Options controlling the pie/bar charts in PiView.
    /// </summary>
    public class PieOptions : INotifyPropertyChanged
    {
        private double smallSliceThreshold = 0.05;
        private int maxSlices = 12;
        private int topN = 6;
        private double innerDiameter = 0.55;
        private LabelMode labelMode = LabelMode.PercentOnly;
        private bool useBarChart;

        public event PropertyChangedEventHandler PropertyChanged;

        public double SmallSliceThreshold
        {
            get => smallSliceThreshold;
            set
            {
                if (smallSliceThreshold != value)
                {
                    smallSliceThreshold = value;
                    OnPropertyChanged(nameof(SmallSliceThreshold));
                }
            }
        }

        public int MaxSlices
        {
            get => maxSlices;
            set
            {
                if (maxSlices != value)
                {
                    maxSlices = value;
                    OnPropertyChanged(nameof(MaxSlices));
                }
            }
        }

        public int TopN
        {
            get => topN;
            set
            {
                if (topN != value)
                {
                    topN = value;
                    OnPropertyChanged(nameof(TopN));
                }
            }
        }

        public double InnerDiameter
        {
            get => innerDiameter;
            set
            {
                if (innerDiameter != value)
                {
                    innerDiameter = value;
                    OnPropertyChanged(nameof(InnerDiameter));
                }
            }
        }

        public LabelMode LabelMode
        {
            get => labelMode;
            set
            {
                if (labelMode != value)
                {
                    labelMode = value;
                    OnPropertyChanged(nameof(LabelMode));
                }
            }
        }

        public bool UseBarChart
        {
            get => useBarChart;
            set
            {
                if (useBarChart != value)
                {
                    useBarChart = value;
                    OnPropertyChanged(nameof(UseBarChart));
                }
            }
        }

        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
