/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using Avalonia.Controls;
using Avalonia;
using Avalonia.Markup.Xaml;

namespace Chummer.Desktop.Views.Sections
{
    public partial class LedgerSectionView : UserControl
    {
        public LedgerSectionView()
        {
            AvaloniaXamlLoader.Load(this);
            var log = this.FindControl<Grid>("LedgerLogBody");
            var account = this.FindControl<ComboBox>("CompactLedgerAccount");
            var karma = this.FindControl<DockPanel>("KarmaAccountPane");
            var nuyen = this.FindControl<DockPanel>("NuyenAccountPane");
            void UpdateAccounts()
            {
                bool compact = Bounds.Width < 1000;
                account.IsVisible = compact;
                log.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 20);
                log.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
                karma.IsVisible = !compact || account.SelectedIndex == 0;
                nuyen.IsVisible = !compact || account.SelectedIndex == 1;
                Grid.SetColumn(nuyen, compact ? 0 : 2);
            }
            SizeChanged += (_, _) => UpdateAccounts();
            account.SelectionChanged += (_, _) => UpdateAccounts();
        }
    }
}
