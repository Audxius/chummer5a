/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Chummer.Desktop.Views
{
    public partial class DiceTerminalView : UserControl
    {
        public DiceTerminalView()
        {
            AvaloniaXamlLoader.Load(this);
            SizeChanged += (_, _) => this.FindControl<Grid>("RollWorkspace").ColumnDefinitions[1].Width = new GridLength(Bounds.Width < 1000 ? 360 : 440);
        }
    }
}
