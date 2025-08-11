using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using BlockViz.Applications.Helpers;
using BlockViz.Applications.Models;
using BlockViz.Applications.Services;
using BlockViz.Applications.Views;
using BlockViz.Domain.Models;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Waf.Applications;

namespace BlockViz.Applications.ViewModels
{
    [Export]
    public class PiViewModel : ViewModel<IPiView>, INotifyPropertyChanged
    {
        private readonly IScheduleService scheduleService;
        private readonly SimulationService simulationService;
        private readonly DispatcherTimer rebuildTimer;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<PlotModel> PieModels { get; }
        public PieOptions Options { get; }

        [ImportingConstructor]
        public PiViewModel(IPiView view,
                           IScheduleService scheduleService,
                           SimulationService simulationService) : base(view)
        {
            this.scheduleService = scheduleService;
            this.simulationService = simulationService;

            PieModels = new ObservableCollection<PlotModel>();
            Options = new PieOptions();

            rebuildTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            rebuildTimer.Tick += (s, e) => { rebuildTimer.Stop(); Rebuild(); };

            Options.PropertyChanged += (s, e) => ScheduleRebuild();
            simulationService.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(simulationService.CurrentDate))
                    ScheduleRebuild();
            };

            view.PieModels = PieModels;
            Rebuild();
        }

        public void SetBlocks(IEnumerable<Block> blocks)
        {
            scheduleService.SetAllBlocks(blocks);
            ScheduleRebuild();
        }

        public void RebuildAll() => ScheduleRebuild();

        private void ScheduleRebuild()
        {
            rebuildTimer.Stop();
            rebuildTimer.Start();
        }

        private void Rebuild()
        {
            PieModels.Clear();
            var blocks = scheduleService.GetAllBlocks().ToList();
            var currentDate = simulationService.CurrentDate;

            for (int wp = 1; wp <= 6; wp++)
            {
                var wsBlocks = blocks.Where(b => b.DeployWorkplace == wp).ToList();
                var model = BuildModel(wsBlocks, wp, currentDate);
                PieModels.Add(model);
            }
        }

        private PlotModel BuildModel(List<Block> wsBlocks, int wp, DateTime currentDate)
        {
            var model = new PlotModel { Title = $"작업장 {wp}" };
            if (!wsBlocks.Any())
                return model;

            var groups = AggregateBlocks(wsBlocks, currentDate);
            var total = groups.Sum(g => g.Val);
            if (total <= 0)
                return model;

            if (Options.UseBarChart)
            {
                var top = groups.Take(Options.TopN).ToList();
                BuildBarModel(model, top, total);
            }
            else
            {
                var keep = FilterSmallSlices(groups, total);
                BuildPieModel(model, keep, total);
            }

            return model;
        }

        private List<NameVal> AggregateBlocks(List<Block> blocks, DateTime currentDate)
        {
            var list = new List<NameVal>();
            foreach (var g in blocks.GroupBy(b => string.IsNullOrWhiteSpace(b.Name) ? "Unknown" : b.Name))
            {
                double sum = 0;
                foreach (var b in g)
                {
                    var start = b.Start;
                    var end = b.End > currentDate ? currentDate : b.End;
                    if (end <= start) continue;
                    sum += Math.Max((end - start).TotalHours, 0);
                }
                if (sum > 0)
                    list.Add(new NameVal { Name = g.Key, Val = sum });
            }
            return list.OrderByDescending(x => x.Val).ToList();
        }

        private List<NameVal> FilterSmallSlices(List<NameVal> items, double total)
        {
            var keep = new List<NameVal>();
            double others = 0;
            foreach (var x in items)
            {
                if (x.Val / total < Options.SmallSliceThreshold)
                    others += x.Val;
                else
                    keep.Add(x);
            }
            while (keep.Count > Options.MaxSlices)
            {
                var last = keep[keep.Count - 1];
                keep.RemoveAt(keep.Count - 1);
                others += last.Val;
            }
            if (others > 0)
                keep.Add(new NameVal { Name = "기타", Val = others });
            return keep;
        }

        private void BuildPieModel(PlotModel model, List<NameVal> items, double total)
        {
            model.Legends.Add(new Legend
            {
                LegendPlacement = LegendPlacement.Outside,
                LegendPosition = LegendPosition.RightTop
            });

            var series = new PieSeries
            {
                StartAngle = 180,
                AngleSpan = 360,
                InnerDiameter = Options.InnerDiameter,
                InsideLabelPosition = 0.7,
                InsideLabelFormat = Options.LabelMode == PieLabelMode.PercentOnly ? "{1:0.#}%" : null,
                OutsideLabelFormat = null,
                Stroke = OxyColors.White,
                StrokeThickness = 1,
                TickHorizontalLength = 0,
                TickRadialLength = 0
            };

            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var mediaColor = BlockColorMap.GetColor(it.Name);
                var color = OxyColor.FromArgb(mediaColor.A, mediaColor.R, mediaColor.G, mediaColor.B);
                var pct = it.Val / total * 100.0;
                var slice = new PieSlice(it.Name, it.Val)
                {
                    Fill = color,
                    ToolTip = $"{it.Name}: {pct:0.#}% ({it.Val:0.#}/{total:0.#})",
                    TextColor = (i < Options.TopN && Options.LabelMode == PieLabelMode.PercentOnly)
                        ? OxyColors.Automatic
                        : OxyColors.Transparent
                };
                series.Slices.Add(slice);
            }

            model.Series.Add(series);
        }

        private void BuildBarModel(PlotModel model, List<NameVal> items, double total)
        {
            var valueAxis = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Minimum = 0,
                Maximum = 100,
                MajorStep = 10,
                MinorStep = 10,
                IsZoomEnabled = false,
                IsPanEnabled = false
            };
            var categoryAxis = new CategoryAxis { Position = AxisPosition.Left };

            model.Axes.Add(valueAxis);
            model.Axes.Add(categoryAxis);

            var series = new BarSeries { LabelFormatString = "{0:0.#}%" };

            foreach (var it in items)
            {
                categoryAxis.Labels.Add(Shorten(it.Name));
                var mediaColor = BlockColorMap.GetColor(it.Name);
                var color = OxyColor.FromArgb(mediaColor.A, mediaColor.R, mediaColor.G, mediaColor.B);
                var pct = it.Val / total * 100.0;
                series.Items.Add(new BarItem
                {
                    Value = pct,
                    Color = color,
                    ToolTip = $"{it.Name}: {pct:0.#}% ({it.Val:0.#}/{total:0.#})"
                });
            }

            model.Series.Add(series);
        }

        private static string Shorten(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unknown";

            bool inParen = false;
            var sb = new StringBuilder();
            foreach (var ch in name)
            {
                if (ch == '(') { inParen = true; continue; }
                if (ch == ')') { inParen = false; continue; }
                if (!inParen) sb.Append(ch);
            }
            var parts = sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var cleaned = string.Join(" ", parts).Trim();
            return cleaned.Length > 12 ? cleaned.Substring(0, 12) + '…' : cleaned;
        }

        private class NameVal
        {
            public string Name { get; set; }
            public double Val { get; set; }
        }
    }
}
