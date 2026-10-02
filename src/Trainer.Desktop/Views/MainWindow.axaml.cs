using Avalonia.Controls;
using Trainer.Desktop.ViewModels;

namespace Trainer.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
