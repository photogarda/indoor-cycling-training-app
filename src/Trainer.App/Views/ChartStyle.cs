using Trainer.App.Infrastructure;
using Trainer.Core.Models;

namespace Trainer.App.Views;

/// <summary>Shared ScottPlot styling.</summary>
public static class ChartStyle
{
    public static void Apply(ScottPlot.Plot plot)
    {
        plot.FigureBackground.Color = ScottPlot.Colors.White;
        plot.DataBackground.Color = ScottPlot.Colors.White;
        plot.Grid.MajorLineColor = Palette.Plot("#E6EAEE");
        plot.Axes.Color(Palette.Plot("#6A7884"));
    }

    public static void MarkRaces(ScottPlot.Plot plot, IEnumerable<Race> races)
    {
        foreach (var r in races)
        {
            var line = plot.Add.VerticalLine(r.Date.ToDateTime(TimeOnly.MinValue).ToOADate());
            line.Color = r.Priority == RacePriority.A ? Palette.Plot("#C53B3B") : Palette.Plot("#E9A100");
            line.LineWidth = r.Priority == RacePriority.A ? 2 : 1;
            line.LinePattern = ScottPlot.LinePattern.Dashed;
            line.Text = $"{r.Priority}: {r.Name}";
            line.LabelFontSize = 10;
        }
    }
}
