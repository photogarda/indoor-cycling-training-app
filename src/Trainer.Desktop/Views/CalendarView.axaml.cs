using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Trainer.Desktop.ViewModels;

namespace Trainer.Desktop.Views;

/// <summary>Calendar interactions: double-click to open, drag a workout to move it, right-click a day to block it.</summary>
public partial class CalendarView : UserControl
{
    /// <summary>In-app drag format carrying the workout id as text.</summary>
    private static readonly DataFormat<string> WorkoutFormat = DataFormat.CreateStringApplicationFormat("trainer-workout-id");
    private Point _pressAt;
    private CalendarWorkout? _pressed;
    private Border? _dropTarget;

    public CalendarView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPressed, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnMoved, handledEventsToo: true);
        AddHandler(DoubleTappedEvent, OnDoubleTapped);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetDropTarget(null));
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private CalendarViewModel? Vm => DataContext as CalendarViewModel;

    /// <summary>Walks up from the element under the pointer to the first data context of type T.</summary>
    private static T? Find<T>(object? source) where T : class
    {
        for (var v = source as Visual; v is not null; v = v.GetVisualParent())
            if (v is StyledElement { DataContext: T t }) return t;
        return null;
    }

    private static Border? DayBorder(object? source)
    {
        for (var v = source as Visual; v is not null; v = v.GetVisualParent())
            if (v is Border b && b.Classes.Contains("day")) return b;
        return null;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        _pressAt = point.Position;
        _pressed = Find<CalendarWorkout>(e.Source);
        if (point.Properties.IsRightButtonPressed && Find<DayCell>(e.Source) is { } cell && DayBorder(e.Source) is { } border)
        {
            _pressed = null;
            ShowDayMenu(cell, border);
            e.Handled = true;
        }
    }

    private async void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var d = e.GetPosition(this) - _pressAt;
        if (Math.Abs(d.X) < 6 && Math.Abs(d.Y) < 6) return;
        var workout = _pressed;
        _pressed = null;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(WorkoutFormat, workout.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        SetDropTarget(null);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Find<CalendarWorkout>(e.Source) is { } w) Vm?.OpenCommand.Execute(w.Id);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var cell = Find<DayCell>(e.Source);
        var ok = e.DataTransfer.Contains(WorkoutFormat) && cell is { IsPast: false, IsBlocked: false };
        e.DragEffects = ok ? DragDropEffects.Move : DragDropEffects.None;
        SetDropTarget(ok ? DayBorder(e.Source) : null);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        SetDropTarget(null);
        if (Find<DayCell>(e.Source) is { } cell && int.TryParse(e.DataTransfer.TryGetValue(WorkoutFormat), out var id))
            Vm?.MoveWorkout(id, cell.Date);
    }

    private void SetDropTarget(Border? b)
    {
        if (_dropTarget == b) return;
        _dropTarget?.Classes.Remove("drop");
        _dropTarget = b;
        _dropTarget?.Classes.Add("drop");
    }

    private void ShowDayMenu(DayCell cell, Control target)
    {
        if (Vm is not { } vm) return;
        var items = new List<MenuItem>();
        if (cell.IsBlocked)
        {
            var unblock = new MenuItem { Header = "Unblock this day" };
            unblock.Click += (_, _) => vm.UnblockDay(cell.Date);
            items.Add(unblock);
        }
        else
        {
            var block = new MenuItem { Header = "Block this day…", IsEnabled = !cell.IsPast };
            block.Click += async (_, _) => await vm.BlockDay(cell.Date);
            items.Add(block);
        }
        foreach (var w in cell.Workouts)
        {
            // One submenu per workout: open, move to another day, remove or restore.
            var sub = new MenuItem { Header = w.IsSkipped ? $"{w.Name} (removed)" : w.Name };
            var open = new MenuItem { Header = "Open" };
            open.Click += (_, _) => vm.OpenCommand.Execute(w.Id);
            sub.Items.Add(open);
            if (w.CanEdit && !w.IsSkipped)
            {
                var move = new MenuItem { Header = "Move to" };
                foreach (var d in vm.MoveTargets(w))
                {
                    var day = d;
                    var item = new MenuItem { Header = day.ToString("ddd d MMM", System.Globalization.CultureInfo.CurrentCulture) };
                    item.Click += (_, _) => vm.MoveWorkout(w.Id, day);
                    move.Items.Add(item);
                }
                sub.Items.Add(move);
                var remove = new MenuItem { Header = "Remove from plan…" };
                remove.Click += async (_, _) => await vm.SkipWorkout(w);
                sub.Items.Add(remove);
            }
            if (w.CanEdit && w.IsSkipped)
            {
                var restore = new MenuItem { Header = "Restore" };
                restore.Click += (_, _) => vm.RestoreWorkout(w);
                sub.Items.Add(restore);
            }
            items.Add(sub);
        }
        var menu = new ContextMenu { ItemsSource = items };
        menu.Open(target);
    }
}
