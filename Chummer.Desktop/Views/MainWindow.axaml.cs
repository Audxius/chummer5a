/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Chummer.Desktop.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
