/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Chummer.Desktop.ViewModels;

namespace Chummer.Desktop.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            AvaloniaXamlLoader.Load(this);
            DataContextChanged += (sender, args) =>
            {
                if (DataContext is MainWindowViewModel objViewModel)
                    objViewModel.Palette.PropertyChanged += PaletteOnPropertyChanged;
            };
        }

        private PaletteViewModel Palette => (DataContext as MainWindowViewModel)?.Palette;

        private void PaletteOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PaletteViewModel.IsOpen) && Palette?.IsOpen == true)
                Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("PaletteBox")?.Focus(), DispatcherPriority.Input);
        }

        private void PaletteBox_OnKeyDown(object sender, KeyEventArgs e)
        {
            PaletteViewModel objPalette = Palette;
            if (objPalette == null)
                return;
            switch (e.Key)
            {
                case Key.Escape:
                    objPalette.CloseCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Down:
                    objPalette.Move(1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    objPalette.Move(-1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    objPalette.ActivateCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        }

        private void PaletteList_OnDoubleTapped(object sender, TappedEventArgs e) => Palette?.ActivateCommand.Execute(null);

        private void PaletteBackdrop_OnPointerPressed(object sender, PointerPressedEventArgs e) => Palette?.CloseCommand.Execute(null);

        private void PaletteCard_OnPointerPressed(object sender, PointerPressedEventArgs e) => e.Handled = true;
    }
}
