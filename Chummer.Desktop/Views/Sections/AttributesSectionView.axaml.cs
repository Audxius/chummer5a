/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using Avalonia.Controls;
using Avalonia;
using Avalonia.Markup.Xaml;

namespace Chummer.Desktop.Views.Sections
{
    public partial class AttributesSectionView : UserControl
    {
        public AttributesSectionView()
        {
            AvaloniaXamlLoader.Load(this);
            var ratings = this.FindControl<Grid>("AttributeRatings");
            var session = this.FindControl<Grid>("AttributeSession");
            var mental = this.FindControl<Border>("MentalPanel");
            var edge = this.FindControl<Border>("SessionEdgePanel");
            SizeChanged += (_, _) =>
            {
                bool compact = Bounds.Width < 1000;
                foreach (var grid in new[] { ratings, session })
                {
                    grid.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
                    grid.ColumnSpacing = compact ? 0 : 24;
                    grid.RowSpacing = compact ? 20 : 0;
                }
                foreach (var panel in new[] { mental, edge })
                {
                    Grid.SetColumn(panel, compact ? 0 : 1); Grid.SetRow(panel, compact ? 1 : 0);
                }
            };
        }
    }
}
