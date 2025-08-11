using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using System.Timers;
using BlockViz.Applications.Helpers;
using BlockViz.Applications.Models;
using BlockViz.Applications.Views;
using BlockViz.Domain.Models;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using System.Waf.Applications;

namespace BlockViz.Applications.ViewModels
{
    [Export]
    public class PiViewModel : ViewModel<IPiView>
    {
        private readonly List<Block> blocks = new();
        private readonly Timer rebuildTimer = new(100) { AutoReset = false };

        public ObservableCollection<PlotModel> PieModels { get; }
        public PieOptions Options { get; }

        [ImportingConstructor]
        public PiViewModel(IPiView view) : base(view)
        {
            PieModels = new ObservableCollection<PlotModel>();
            Options = new PieOptions();
            view.PieModels = PieModels;

            Options.PropertyChanged += (_, __) => Debounce();
            rebuildTimer.Elapsed += (_, __) => RebuildAll();
        }

        public void SetBlocks(IEnumerable<Block> source)
        {
            blocks.Clear();
            if (source != null)
                blocks.AddRange(source);
            Debounce();
        }

        public void RebuildAll()
        {
            rebuildTimer.Stop();
            PieModels.Clear();
            for (int wp = 1; wp <= 6; wp++)
            {
                PieModels.Add(BuildModel(wp));
            }
        }

        private PlotModel BuildModel(int workplace)
        {
            var items = blocks.Where(b => b.DeployWorkplace == workplace)
                .GroupBy(b => string.IsNullOrWhiteSpace(b.Name) ? "Unknown" : b.Name)
                .Select(g => new NameVal { Name = g.Key, Val = g.Sum(b => Math.Max((b.End - b.Start).TotalHours, 0)) })
                .Where(nv => nv.Val > 0)
                .OrderByDescending(nv => nv.Val)
                .ToList();

            double total = items.Sum(i => i.Val);
            var model = new PlotModel { Title = $"작업장 {workplace}" };
            model.Legends.Add(new Legend { LegendPosition = LegendPosition.RightTop });
            if (total <= 0)
                return model;

            if (Options.UseBarChart)
                BuildBarChart(model, items, total);
            else
                BuildPieChart(model, items, total);
            return model;
        }

        private void BuildPieChart(PlotModel model, List<NameVal> items, double total)
        {
            var keep = new List<NameVal>();
            double others = 0;
            foreach (var x in items)
            {
                if (x.Val / total < Options.SmallSliceThreshold) others += x.Val;
                else keep.Add(x);
            }
            while (keep.Count > Options.MaxSlices)
            {
                var last = keep[keep.Count - 1];
                keep.RemoveAt(keep.Count - 1);
                others += last.Val;
            }
            if (others > 0)
                keep.Add(new NameVal { Name = "기타", Val = others });

            var series = new PieSeries
            {
                StartAngle = 180,
                AngleSpan = 360,
                InnerDiameter = Options.InnerDiameter,
                InsideLabelPosition = 0.7,
                InsideLabelFormat = Options.LabelMode == LabelMode.PercentOnly ? "{2:0.#}%" : null,
                OutsideLabelFormat = null,
                Stroke = OxyColors.White,
                StrokeThickness = 1,
                TickHorizontalLength = 0,
                TickRadialLength = 0,
                LegendFormat = "{0}",
                TrackerFormatString = "{0}: {2:0.#}% ({1:0.#})"
            };

            int index = 0;
            foreach (var k in keep)
            {
                var color = BlockColorMap.GetColor(k.Name).ToOxyColor();
                var slice = new PieSlice(k.Name, k.Val) { Fill = color };
                slice.TextColor = index < Options.TopN ? OxyColors.Black : OxyColors.Transparent;
                series.Slices.Add(slice);
                index++;
            }
            model.Series.Add(series);
        }

        private void BuildBarChart(PlotModel model, List<NameVal> items, double total)
        {
            var top = items.Take(Options.TopN).ToList();
            var categoryAxis = new CategoryAxis { Position = AxisPosition.Left };
            foreach (var item in top)
                categoryAxis.Labels.Add(Shorten(item.Name));
            model.Axes.Add(categoryAxis);
            model.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Minimum = 0, Maximum = 100, MajorStep = 10 });

            int idx = 0;
            foreach (var item in top)
            {
                var color = BlockColorMap.GetColor(item.Name).ToOxyColor();
                var bar = new BarSeries
                {
                    Title = item.Name,
                    FillColor = color,
                    TrackerFormatString = "{0}: {1:0.#}%"
                };
                bar.Items.Add(new BarItem(item.Val / total * 100) { CategoryIndex = idx });
                model.Series.Add(bar);
                idx++;
            }
        }

        private static string Shorten(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unknown";
            string noParen = Regex.Replace(name, "\([^)]*\)", "");
            string normalized = Regex.Replace(noParen, "\s+", " ").Trim();
            return normalized.Length > 12 ? normalized.Substring(0, 12) + "…" : normalized;
        }

        private void Debounce()
        {
            rebuildTimer.Stop();
            rebuildTimer.Start();
        }

        private class NameVal
        {
            public string Name { get; set; }
            public double Val { get; set; }
        }
    }
}
