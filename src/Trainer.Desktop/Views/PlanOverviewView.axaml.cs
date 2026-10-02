using System.ComponentModel;
using Avalonia.Controls;
using Trainer.Desktop.Infrastructure;
using Trainer.Desktop.ViewModels;

namespace Trainer.Desktop.Views;

public partial class PlanOverviewView : UserControl
{
    private INotifyPropertyChanged? _vm;

    public PlanOverviewView()
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
        if (e.PropertyName == nameof(PlanOverviewViewModel.ChartVersion)) Draw();
    }

    /// <summary>Planned CTL by week end, actual CTL so far, phase backgrounds and races.</summary>
    private void Draw()
    {
        if (DataContext is not PlanOverviewViewModel vm) return;
        var plot = Chart.Plot;
        plot.Clear();
        ChartStyle.Apply(plot);

        foreach (var w in vm.Weeks)
        {
            var span = plot.Add.HorizontalSpan(w.Week.WeekStart.ToDateTime(TimeOnly.MinValue).ToOADate(),
                w.Week.WeekStart.AddDays(7).ToDateTime(TimeOnly.MinValue).ToOADate());
            span.FillStyle.Color = Palette.Plot(Palette.PhaseHex(w.Phase)).WithAlpha(40);
            span.LineStyle.Width = 0;
        }
        if (vm.Weeks.Count > 0)
        {
            var planned = plot.Add.Scatter(
                vm.Weeks.Select(w => w.Week.WeekStart.AddDays(6).ToDateTime(TimeOnly.MinValue).ToOADate()).ToArray(),
                vm.Weeks.Select(w => w.Week.PlannedCtl).ToArray());
            planned.LegendText = "Planned CTL";
            planned.Color = Palette.Plot("#2563A8");
            planned.LineWidth = 2;
            planned.MarkerSize = 4;
        }
        if (vm.ActualLoads.Count > 1)
        {
            var actual = plot.Add.Scatter(vm.ActualLoads.Select(l => l.Date.ToDateTime(TimeOnly.MinValue).ToOADate()).ToArray(),
                vm.ActualLoads.Select(l => l.Ctl).ToArray());
            actual.LegendText = "Actual CTL";
            actual.Color = Palette.Plot("#1E2A33");
            actual.LineWidth = 2;
            actual.MarkerSize = 0;
        }
        ChartStyle.MarkRaces(plot, vm.Races);
        plot.Axes.DateTimeTicksBottom();
        plot.ShowLegend(ScottPlot.Alignment.UpperLeft);
        plot.Axes.AutoScale();
        Chart.Refresh();
    }
}
