using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Trainer.Desktop.Views;

/// <summary>Simple message or yes/no dialog. Returns true for OK/Yes.</summary>
public class MessageWindow : Window
{
    public MessageWindow() : this("", "", false)
    {
    }

    public MessageWindow(string title, string message, bool askYesNo)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var ok = new Button { Content = askYesNo ? "Yes" : "OK", IsDefault = true, MinWidth = 80, Classes = { "primary" } };
        ok.Click += (_, _) => Close(true);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(ok);
        if (askYesNo)
        {
            var no = new Button { Content = "No", IsCancel = true, MinWidth = 80, Margin = new Thickness(0) };
            no.Click += (_, _) => Close(false);
            buttons.Children.Add(no);
        }
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children = { new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, buttons },
        };
    }
}

/// <summary>One-line text prompt. Returns the text, or null if cancelled.</summary>
public class InputWindow : Window
{
    public InputWindow() : this("", "", "")
    {
    }

    public InputWindow(string title, string message, string initial)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var box = new TextBox { Text = initial };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Classes = { "primary" } };
        ok.Click += (_, _) => Close(box.Text?.Trim() ?? "");
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(0) };
        cancel.Click += (_, _) => Close(null);
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                box,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
            },
        };
        Opened += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
    }
}
