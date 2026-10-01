using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Trainer.App.Infrastructure;
using Trainer.Core.Models;
using Trainer.Integrations.Fit;

namespace Trainer.App.Controls;

/// <summary>Draws a workout's steps as zone-coloured blocks. Height is % FTP; hover shows watts.</summary>
public sealed class IntervalGraph : FrameworkElement
{
    public static readonly DependencyProperty StepsProperty = DependencyProperty.Register(
        nameof(Steps), typeof(IList<WorkoutStep>), typeof(IntervalGraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FtpProperty = DependencyProperty.Register(
        nameof(Ftp), typeof(int), typeof(IntervalGraph), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
        nameof(Compact), typeof(bool), typeof(IntervalGraph), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public IList<WorkoutStep>? Steps
    {
        get => (IList<WorkoutStep>?)GetValue(StepsProperty);
        set => SetValue(StepsProperty, value);
    }

    public int Ftp
    {
        get => (int)GetValue(FtpProperty);
        set => SetValue(FtpProperty, value);
    }

    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    private List<(double X0, double X1, WorkoutStep Step, int Start)> _hit = [];
    private static readonly Typeface Face = new("Segoe UI");
    private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xDC, 0xE2, 0xE7)), 1));
    private static readonly Pen FtpPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x60, 0x68)), 1) { DashStyle = DashStyles.Dash });

    private static Pen Freeze(Pen p)
    {
        p.Freeze();
        return p;
    }

    private readonly ToolTip _tip = new();

    public IntervalGraph()
    {
        ToolTipService.SetInitialShowDelay(this, 0);
        ToolTip = _tip;
        ToolTipOpening += (_, e) =>
        {
            if (_tip.Content is null) e.Handled = true; // nothing under the mouse yet
        };
    }

    protected override void OnRender(DrawingContext dc)
    {
        _hit = [];
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var steps = Steps is null ? [] : WorkoutStep.Flatten(Steps).ToList();
        var total = steps.Sum(s => s.DurationSec);
        if (total <= 0 || ActualWidth < 10 || ActualHeight < 10) return;

        var left = Compact ? 0 : 42.0;
        var bottom = Compact ? 0 : 18.0;
        var w = ActualWidth - left;
        var h = ActualHeight - bottom;
        var maxPct = Math.Max(1.3, steps.Max(s => s.Kind == StepKind.Free ? 1.3 : s.PowerHigh) + 0.1);
        double Y(double pct) => h - pct / maxPct * h;

        if (!Compact)
        {
            foreach (var pct in new[] { 0.5, 1.0, 1.5 }.Where(p => p <= maxPct))
            {
                dc.DrawLine(pct == 1.0 ? FtpPen : GridPen, new Point(left, Y(pct)), new Point(left + w, Y(pct)));
                var label = Ftp > 0 ? $"{Math.Round(pct * Ftp)} W" : $"{pct * 100:0}%";
                Text(dc, label, new Point(2, Y(pct) - 8), 10);
            }
            var tick = total > 3 * 3600 ? 1800 : total > 5400 ? 900 : 600;
            for (var t = 0; t <= total; t += tick)
                Text(dc, $"{t / 3600}:{t % 3600 / 60:00}", new Point(left + t * w / total - 10, h + 2), 10);
        }

        var x = 0;
        foreach (var s in steps)
        {
            var x0 = left + x * w / total;
            var x1 = left + (x + s.DurationSec) * w / total;
            if (s.Kind == StepKind.Free)
            {
                dc.DrawRectangle(Palette.Free, null, new Rect(new Point(x0, Y(1.3)), new Point(Math.Max(x0 + 1, x1), h)));
            }
            else
            {
                var brush = Palette.ForPower(s.PowerMid);
                dc.DrawRectangle(brush, null, new Rect(new Point(x0, Y(s.PowerMid)), new Point(Math.Max(x0 + 1, x1), h)));
                if (s.PowerHigh - s.PowerLow > 0.04 && !Compact)
                {
                    var range = brush.Clone();
                    range.Opacity = 0.35;
                    dc.DrawRectangle(range, null, new Rect(new Point(x0, Y(s.PowerHigh)), new Point(Math.Max(x0 + 1, x1), Y(s.PowerMid))));
                }
            }
            _hit.Add((x0, x1, s, x));
            x += s.DurationSec;
        }
    }

    private void Text(DrawingContext dc, string s, Point at, double size)
    {
        var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size,
            new SolidColorBrush(Color.FromRgb(0x6A, 0x78, 0x84)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, at);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var px = e.GetPosition(this).X;
        var hit = _hit.FirstOrDefault(h => px >= h.X0 && px <= h.X1);
        if (hit.Step is null)
        {
            _tip.Content = null;
            return;
        }
        var s = hit.Step;
        var at = $"{hit.Start / 3600}:{hit.Start % 3600 / 60:00}:{hit.Start % 60:00}";
        var len = s.DurationSec >= 60 ? $"{s.DurationSec / 60}:{s.DurationSec % 60:00}" : $"{s.DurationSec} s";
        string target;
        if (s.Kind == StepKind.Free) target = "ERG off";
        else
        {
            var pct = Math.Abs(s.PowerHigh - s.PowerLow) < 0.005 ? $"{s.PowerLow * 100:0} %" : $"{s.PowerLow * 100:0}–{s.PowerHigh * 100:0} %";
            target = pct;
            if (Ftp > 0)
            {
                var (lo, hi) = FitWorkoutWriter.Watts(s, Ftp);
                target += $" · {lo}–{hi} W";
            }
        }
        _tip.Content = $"{at}  {len}  {target}{(s.Label is null ? "" : "  — " + s.Label)}";
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 300 : availableSize.Width, double.IsInfinity(availableSize.Height) ? (Compact ? 24 : 180) : availableSize.Height);
}
