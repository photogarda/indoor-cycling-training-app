using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Trainer.Core.Models;
using Trainer.Desktop.Infrastructure;
using Trainer.Integrations.Fit;

namespace Trainer.Desktop.Controls;

/// <summary>Draws a workout's steps as zone-coloured blocks. Height is % FTP; hover shows watts.</summary>
public sealed class IntervalGraph : Control
{
    public static readonly StyledProperty<IList<WorkoutStep>?> StepsProperty =
        AvaloniaProperty.Register<IntervalGraph, IList<WorkoutStep>?>(nameof(Steps));

    public static readonly StyledProperty<int> FtpProperty = AvaloniaProperty.Register<IntervalGraph, int>(nameof(Ftp));

    public static readonly StyledProperty<bool> CompactProperty = AvaloniaProperty.Register<IntervalGraph, bool>(nameof(Compact));

    static IntervalGraph() => AffectsRender<IntervalGraph>(StepsProperty, FtpProperty, CompactProperty);

    public IList<WorkoutStep>? Steps
    {
        get => GetValue(StepsProperty);
        set => SetValue(StepsProperty, value);
    }

    public int Ftp
    {
        get => GetValue(FtpProperty);
        set => SetValue(FtpProperty, value);
    }

    public bool Compact
    {
        get => GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    private List<(double X0, double X1, WorkoutStep Step, int Start)> _hit = [];
    private static readonly Typeface Face = new("Inter");
    private static readonly IBrush LabelLight = new SolidColorBrush(Color.Parse("#6A7884"));
    private static readonly IBrush LabelDark = new SolidColorBrush(Color.Parse("#8D9BA7"));
    private static readonly IPen GridLight = new Pen(new SolidColorBrush(Color.Parse("#DCE2E7")));
    private static readonly IPen GridDark = new Pen(new SolidColorBrush(Color.Parse("#2C3742")));
    private static readonly IPen FtpLight = new Pen(new SolidColorBrush(Color.Parse("#556068")), dashStyle: DashStyle.Dash);
    private static readonly IPen FtpDark = new Pen(new SolidColorBrush(Color.Parse("#AEBAC4")), dashStyle: DashStyle.Dash);
    private bool Dark => ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
    private IBrush LabelBrush => Dark ? LabelDark : LabelLight;
    private IPen GridPen => Dark ? GridDark : GridLight;
    private IPen FtpPen => Dark ? FtpDark : FtpLight;

    public IntervalGraph() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    public override void Render(DrawingContext dc)
    {
        _hit = [];
        var size = Bounds.Size;
        dc.FillRectangle(Brushes.Transparent, new Rect(size));
        var steps = Steps is null ? [] : WorkoutStep.Flatten(Steps).ToList();
        var total = steps.Sum(s => s.DurationSec);
        if (total <= 0 || size.Width < 10 || size.Height < 10) return;

        var left = Compact ? 0 : 42.0;
        var bottom = Compact ? 0 : 18.0;
        var w = size.Width - left;
        var h = size.Height - bottom;
        var maxPct = Math.Max(1.3, steps.Max(s => s.Kind == StepKind.Free ? 1.3 : s.PowerHigh) + 0.1);
        double Y(double pct) => h - pct / maxPct * h;

        if (!Compact)
        {
            foreach (var pct in new[] { 0.5, 1.0, 1.5 }.Where(p => p <= maxPct))
            {
                dc.DrawLine(pct == 1.0 ? FtpPen : GridPen, new Point(left, Y(pct)), new Point(left + w, Y(pct)));
                Text(dc, Ftp > 0 ? $"{Math.Round(pct * Ftp)} W" : $"{pct * 100:0}%", new Point(2, Y(pct) - 8));
            }
            var tick = total > 3 * 3600 ? 1800 : total > 5400 ? 900 : 600;
            for (var t = 0; t <= total; t += tick)
                Text(dc, $"{t / 3600}:{t % 3600 / 60:00}", new Point(left + t * w / total - 10, h + 2));
        }

        var x = 0;
        foreach (var s in steps)
        {
            var x0 = left + x * w / total;
            var x1 = Math.Max(x0 + 1, left + (x + s.DurationSec) * w / total);
            if (s.Kind == StepKind.Free)
            {
                dc.FillRectangle(Palette.Free, new Rect(new Point(x0, Y(1.3)), new Point(x1, h)));
            }
            else
            {
                var brush = Palette.ForPower(s.PowerMid);
                dc.FillRectangle(brush, new Rect(new Point(x0, Y(s.PowerMid)), new Point(x1, h)));
                if (s.PowerHigh - s.PowerLow > 0.04 && !Compact)
                {
                    using (dc.PushOpacity(0.35))
                        dc.FillRectangle(brush, new Rect(new Point(x0, Y(s.PowerHigh)), new Point(x1, Y(s.PowerMid))));
                }
            }
            _hit.Add((x0, x1, s, x));
            x += s.DurationSec;
        }
    }

    private void Text(DrawingContext dc, string s, Point at) =>
        dc.DrawText(new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, 10, LabelBrush), at);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Compact) return;
        var px = e.GetPosition(this).X;
        var hit = _hit.FirstOrDefault(h => px >= h.X0 && px <= h.X1);
        if (hit.Step is null)
        {
            ToolTip.SetTip(this, null);
            return;
        }
        var s = hit.Step;
        var at = $"{hit.Start / 3600}:{hit.Start % 3600 / 60:00}:{hit.Start % 60:00}";
        var len = s.DurationSec >= 60 ? $"{s.DurationSec / 60}:{s.DurationSec % 60:00}" : $"{s.DurationSec} s";
        string target;
        if (s.Kind == StepKind.Free) target = "ERG off";
        else
        {
            target = Math.Abs(s.PowerHigh - s.PowerLow) < 0.005 ? $"{s.PowerLow * 100:0} %" : $"{s.PowerLow * 100:0}–{s.PowerHigh * 100:0} %";
            if (Ftp > 0)
            {
                var (lo, hi) = FitWorkoutWriter.Watts(s, Ftp);
                target += $" · {lo}–{hi} W";
            }
        }
        ToolTip.SetTip(this, $"{at}  {len}  {target}{(s.Label is null ? "" : "  — " + s.Label)}");
        ToolTip.SetIsOpen(this, true);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ToolTip.SetIsOpen(this, false);
    }
}
