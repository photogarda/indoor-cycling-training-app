using System.ComponentModel;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using Trainer.App.Infrastructure;
using Trainer.App.ViewModels;
using Trainer.Core.Models;

namespace Trainer.App.Views;

public partial class PlanOverviewView : UserControl
{
    public PlanOverviewView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged old) old.PropertyChanged -= OnVmChanged;
            if (e.NewValue is INotifyPropertyChanged vm) vm.PropertyChanged += OnVmChanged;
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
            span.FillStyle.Color = Palette.Plot(Palette.Phase(w.Phase)).WithAlpha(40);
            span.LineStyle.Width = 0;
        }

        if (vm.Weeks.Count > 0)
        {
            var xs = vm.Weeks.Select(w => w.Week.WeekStart.AddDays(6).ToDateTime(TimeOnly.MinValue).ToOADate()).ToArray();
            var ys = vm.Weeks.Select(w => w.Week.PlannedCtl).ToArray();
            var planned = plot.Add.Scatter(xs, ys);
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

/// <summary>Width of a phase band: 18 px per week.</summary>
public sealed class WeekWidth : IValueConverter
{
    public static readonly WeekWidth Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is int w ? Math.Max(8, w * 18.0) : 18.0;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
