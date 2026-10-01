using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Trainer.App.ViewModels;

namespace Trainer.App.Views;

public partial class CalendarView : UserControl
{
    private Point _dragStart;

    public CalendarView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => WeekRadio.IsChecked = !(Vm?.MonthMode ?? true);
    }

    private CalendarViewModel? Vm => DataContext as CalendarViewModel;

    private void WeekRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) Vm.MonthMode = false;
    }

    private void Workout_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        if (e.ClickCount == 2 && sender is FrameworkElement { Tag: int id })
        {
            Vm?.OpenCommand.Execute(id);
            e.Handled = true;
        }
    }

    private void Workout_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement { Tag: int id } element) return;
        var delta = e.GetPosition(this) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop(element, new DataObject("workout-id", id), DragDropEffects.Move);
    }

    private void Day_DragOver(object sender, DragEventArgs e)
    {
        var ok = e.Data.GetDataPresent("workout-id") && sender is FrameworkElement { Tag: DayCell { IsPast: false, IsBlocked: false } };
        e.Effects = ok ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Day_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DayCell cell } || e.Data.GetData("workout-id") is not int id) return;
        Vm?.MoveWorkout(id, cell.Date);
    }

    private void Day_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DayCell cell } element || Vm is null) return;
        var menu = new ContextMenu();
        if (cell.IsBlocked)
        {
            var unblock = new MenuItem { Header = "Unblock this day" };
            unblock.Click += (_, _) => Vm.UnblockDay(cell.Date);
            menu.Items.Add(unblock);
        }
        else
        {
            var block = new MenuItem { Header = "Block this day…", IsEnabled = !cell.IsPast };
            block.Click += (_, _) => Vm.BlockDay(cell.Date);
            menu.Items.Add(block);
        }
        foreach (var w in cell.Workouts)
        {
            var open = new MenuItem { Header = $"Open {w.Name}" };
            open.Click += (_, _) => Vm.OpenCommand.Execute(w.Id);
            menu.Items.Add(open);
        }
        menu.PlacementTarget = element;
        menu.IsOpen = true;
        e.Handled = true;
    }
}
