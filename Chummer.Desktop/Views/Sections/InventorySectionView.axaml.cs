/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using Avalonia.Controls;
using Avalonia;
using Avalonia.Markup.Xaml;

namespace Chummer.Desktop.Views.Sections
{
    public partial class InventorySectionView : UserControl
    {
        public InventorySectionView()
        {
            AvaloniaXamlLoader.Load(this);
            var layout = this.FindControl<Grid>("InventoryLayout");
            var categorySelector = this.FindControl<ComboBox>("InventoryCategorySelector");
            var categoryTabs = this.FindControl<ItemsControl>("InventoryCategoryTabs");
            SizeChanged += (_, _) =>
            {
                bool compact = Bounds.Width < 1000;
                categorySelector.IsVisible = compact;
                categoryTabs.IsVisible = !compact;
                layout.ColumnDefinitions[1].Width = new GridLength(compact ? 320 : 370);
            };
        }
    }
}
