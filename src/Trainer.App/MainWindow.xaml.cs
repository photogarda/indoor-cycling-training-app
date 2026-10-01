using System.Windows;
using Trainer.App.ViewModels;

namespace Trainer.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
