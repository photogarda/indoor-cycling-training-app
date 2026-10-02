using System.ComponentModel;
using Avalonia.Controls;
using Trainer.Desktop.Infrastructure;
using Trainer.Desktop.ViewModels;
using Trainer.Core.Training;

namespace Trainer.Desktop.Views;

public partial class AnalysisView : UserControl
{
    private INotifyPropertyChanged? _vm;

    public AnalysisView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as INotifyPropertyChanged;
            if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
            Draw();
        };
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AnalysisViewModel.ChartVersion)) Draw();
    }

    private void Draw()
    {
        if (DataContext is not AnalysisViewModel vm) return;
        DrawPmc(vm);
        DrawCurve(vm);
    }

    private void DrawPmc(AnalysisViewModel vm)
    {
        var plot = Pmc.Plot;
        plot.Clear();
        ChartStyle.Apply(plot);
        if (vm.Loads.Count > 1)
        {
            var xs = vm.Loads.Select(l => l.Date.ToDateTime(TimeOnly.MinValue).ToOADate()).ToArray();
            var tss = plot.Add.Bars(xs, vm.Loads.Select(l => l.Tss).ToArray());
            tss.Color = Palette.Plot("#C9D3DB");
            tss.LegendText = "Daily TSS";
            foreach (var bar in tss.Bars)
            {
                bar.Size = 0.8;
                bar.LineWidth = 0;
            }
            // TSB below −30 is where the fatigue guard steps in.
            var fatigueZone = plot.Add.VerticalSpan(-30, -60);
            fatigueZone.FillStyle.Color = Palette.Plot("#DE3B3B").WithAlpha(18);
            fatigueZone.LineStyle.Width = 0;

            Line(plot, xs, vm.Loads.Select(l => l.Ctl).ToArray(), "CTL (fitness)", "#2563A8", 2.5f);
            Line(plot, xs, vm.Loads.Select(l => l.Atl).ToArray(), "ATL (fatigue)", "#DE3B3B", 1.5f);
            Line(plot, xs, vm.Loads.Select(l => l.Tsb).ToArray(), "TSB (form)", "#E9A100", 1.5f);
            var zero = plot.Add.HorizontalLine(0);
            zero.Color = Palette.Plot("#9EA7AD");
            zero.LineWidth = 1;
        }
        else
        {
            plot.Add.Annotation("Import rides to see fitness, fatigue and form.");
        }
        ChartStyle.MarkRaces(plot, vm.Races);
        plot.Axes.DateTimeTicksBottom();
        plot.ShowLegend(ScottPlot.Alignment.UpperLeft);
        plot.Axes.AutoScale();
        Pmc.Refresh();
    }

    private static void Line(ScottPlot.Plot plot, double[] xs, double[] ys, string label, string hex, float width)
    {
        var s = plot.Add.Scatter(xs, ys);
        s.LegendText = label;
        s.Color = Palette.Plot(hex);
        s.LineWidth = width;
        s.MarkerSize = 0;
    }

    private void DrawCurve(AnalysisViewModel vm)
    {
        var plot = Curve.Plot;
        plot.Clear();
        ChartStyle.Apply(plot);
        var durations = PowerCurve.Durations;

        void Series(Dictionary<int, double> curve, string label, string hex)
        {
            var pts = durations.Select((d, i) => (i, ok: curve.TryGetValue(d, out var w), w)).Where(p => p.ok).ToList();
            if (pts.Count == 0) return;
            var s = plot.Add.Scatter(pts.Select(p => (double)p.i).ToArray(), pts.Select(p => p.w).ToArray());
            s.LegendText = label;
            s.Color = Palette.Plot(hex);
            s.LineWidth = 2;
            s.MarkerSize = 5;
        }
        Series(vm.CurveAllTime, "All time", "#9EA7AD");
        Series(vm.CurveSixWeeks, "Last 6 weeks", "#2563A8");
        // Label a readable subset; every duration still has a point.
        int[] labelled = [5, 30, 60, 300, 1200, 3600];
        var tickIdx = Enumerable.Range(0, durations.Length).Where(i => labelled.Contains(durations[i])).ToArray();
        plot.Axes.Bottom.SetTicks(tickIdx.Select(i => (double)i).ToArray(), tickIdx.Select(i => PowerCurve.Label(durations[i])).ToArray());
        plot.Axes.Left.Label.Text = "Watts";
        plot.ShowLegend(ScottPlot.Alignment.UpperRight);
        plot.Axes.AutoScale();
        Curve.Refresh();
    }
}
