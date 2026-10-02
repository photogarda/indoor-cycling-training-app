using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.TickGenerators;
using Palette = Trainer.Desktop.Infrastructure.Palette;
using Race = Trainer.Core.Models.Race;
using RacePriority = Trainer.Core.Models.RacePriority;

namespace Trainer.Desktop.Views;

/// <summary>Shared ScottPlot styling: readable fonts, a clean legend, short dates, race labels along the top.</summary>
public static class ChartStyle
{
    private static bool Dark => Trainer.Desktop.Infrastructure.ThemeManager.IsDark;
    private static Color Muted => Palette.Plot(Dark ? "#8D9BA7" : "#6A7884");
    private static Color GridLine => Palette.Plot(Dark ? "#26313B" : "#E6EAEE");
    private static Color Surface => Palette.Plot(Dark ? "#1B232B" : "#FFFFFF");
    private static Color Border => Palette.Plot(Dark ? "#2C3742" : "#DCE2E7");
    private static Color Text => Palette.Plot(Dark ? "#E3E9EE" : "#1E2A33");
    /// <summary>The "actual CTL" line: dark ink in light mode, near-white in dark mode.</summary>
    public static Color Ink => Palette.Plot(Dark ? "#E3E9EE" : "#1E2A33");

    /// <summary>One-time control setup: no debug overlay on double-click.</summary>
    public static void Init(AvaPlot control) => control.UserInputProcessor.DoubleLeftClickBenchmark(false);

    public static void Apply(Plot plot)
    {
        plot.Benchmark.IsVisible = false;
        plot.FigureBackground.Color = Surface;
        plot.DataBackground.Color = Surface;
        plot.Grid.MajorLineColor = GridLine;
        plot.Axes.Color(Muted);
        plot.Axes.FrameColor(Border);
        foreach (var axis in new IAxis[] { plot.Axes.Bottom, plot.Axes.Left })
        {
            axis.TickLabelStyle.FontSize = 12;
            axis.TickLabelStyle.ForeColor = Muted;
            axis.Label.FontSize = 12;
            axis.Label.Bold = false;
            axis.Label.ForeColor = Muted;
        }

        // Legend: compact, white, thin border, no shadow, enough padding that text isn't clipped.
        plot.Legend.FontSize = 12;
        plot.Legend.FontColor = Text;
        plot.Legend.BackgroundColor = Surface.WithAlpha(235);
        plot.Legend.OutlineColor = Border;
        plot.Legend.OutlineWidth = 1;
        plot.Legend.ShadowColor = Colors.Transparent;
        plot.Legend.Padding = new PixelPadding(10, 10, 6, 6);
        plot.Legend.InterItemPadding = new PixelPadding(2, 2, 3, 3);
        plot.Legend.Margin = new PixelPadding(8);
    }

    /// <summary>Date axis with short labels: "29 Jul", or "Jul 26" when the chart spans more than ~5 months.</summary>
    public static void DateAxis(Plot plot, DateTime from, DateTime to)
    {
        var axis = plot.Axes.DateTimeTicksBottom();
        if (axis.TickGenerator is DateTimeAutomatic auto)
        {
            var long_ = (to - from).TotalDays > 150;
            auto.LabelFormatter = d => d.ToString(long_ ? "MMM yy" : "d MMM", System.Globalization.CultureInfo.CurrentCulture);
        }
        axis.TickLabelStyle.FontSize = 12;
        axis.TickLabelStyle.ForeColor = Muted;
    }

    /// <summary>Dashed race lines with small rounded labels along the top edge, clear of the date labels.</summary>
    public static void MarkRaces(Plot plot, IEnumerable<Race> races)
    {
        var any = false;
        foreach (var r in races)
        {
            any = true;
            var color = r.Priority == RacePriority.A ? Palette.Plot("#C53B3B") : r.Priority == RacePriority.B ? Palette.Plot("#E08A00") : Palette.Plot("#7A8B97");
            var line = plot.Add.VerticalLine(r.Date.ToDateTime(TimeOnly.MinValue).ToOADate());
            line.Color = color.WithAlpha(200);
            line.LineWidth = r.Priority == RacePriority.A ? 2 : 1.5f;
            line.LinePattern = LinePattern.Dashed;
            line.Text = $"{r.Priority} · {Shorten(r.Name)}";
            line.LabelOppositeAxis = true;
            line.LabelStyle.FontSize = 11;
            line.LabelStyle.Bold = false;
            line.LabelStyle.ForeColor = Colors.White;
            line.LabelStyle.BackgroundColor = color;
            line.LabelStyle.BorderRadius = 3;
            line.LabelStyle.PixelPadding = new PixelPadding(6, 6, 3, 3);
        }
        // Room above the data area for the race labels.
        if (any) plot.Axes.Top.MinimumSize = 26;
    }

    private static string Shorten(string name) => name.Length <= 22 ? name : name[..21] + "…";
}
