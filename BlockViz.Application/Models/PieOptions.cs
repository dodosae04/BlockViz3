using System.ComponentModel;

namespace BlockViz.Applications.Models
{
    public enum PieLabelMode
    {
        PercentOnly,
        Off
    }

    public class PieOptions : INotifyPropertyChanged
    {
        private double smallSliceThreshold = 0.05;
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

        private int maxSlices = 12;
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

        private int topN = 6;
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

        private double innerDiameter = 0.55;
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

        private PieLabelMode labelMode = PieLabelMode.PercentOnly;
        public PieLabelMode LabelMode
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

        private bool useBarChart;
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

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

