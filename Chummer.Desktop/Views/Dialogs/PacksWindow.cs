using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Chummer.Selection;

namespace Chummer.Desktop.Views.Dialogs
{
    public sealed class PacksWindow : Window
    {
        private readonly IReadOnlyList<PacksKitOption> _kits;
        private readonly TextBox _search = new TextBox { PlaceholderText = "Search PACKS kits…" };
        private readonly ComboBox _category = new ComboBox { Width = 240 };
        private readonly ListBox _list = new ListBox();
        private readonly TextBlock _details = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        public PacksKitOption Selected { get; private set; }
        public bool AddAgain { get; private set; }

        public PacksWindow(IReadOnlyList<PacksKitOption> kits)
        {
            _kits = kits; Title = "PACKS kits"; Width = 1100; Height = 780; MinWidth = 800; MinHeight = 500;
            var root = new DockPanel { Margin = new Thickness(24) };
            var filters = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12, Margin = new Thickness(0, 0, 0, 14) };
            filters.Children.Add(_search); Grid.SetColumn(_category, 1); filters.Children.Add(_category);
            _category.ItemsSource = new[] { "All categories" }.Concat(kits.Select(x => x.Category).Distinct().OrderBy(x => x)).ToList(); _category.SelectedIndex = 0;
            DockPanel.SetDock(filters, Dock.Top); root.Children.Add(filters);
            var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 16, 0, 0) };
            var add = new Button { Content = "ADD KIT", IsEnabled = false, Classes = { "primary" } };
            var again = new Button { Content = "ADD & PICK ANOTHER", IsEnabled = false };
            var cancel = new Button { Content = "CANCEL" };
            void Accept(bool another) { if (_list.SelectedItem is PacksKitOption kit) { Selected = kit; AddAgain = another; Close(); } }
            add.Click += (_, _) => Accept(false); again.Click += (_, _) => Accept(true); cancel.Click += (_, _) => Close();
            footer.Children.Add(add); footer.Children.Add(again); footer.Children.Add(cancel); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 24 };
            _list.ItemTemplate = new FuncDataTemplate<PacksKitOption>((item, _) => new TextBlock { Text = item?.Name ?? "", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            body.Children.Add(_list); var scroll = new ScrollViewer { Content = _details }; Grid.SetColumn(scroll, 1); body.Children.Add(scroll); root.Children.Add(body); Content = root;
            _list.SelectionChanged += (_, _) =>
            {
                var kit = _list.SelectedItem as PacksKitOption;
                add.IsEnabled = again.IsEnabled = kit != null;
                _details.Text = kit == null ? "No kits match these filters." : kit.Name + "\n" + kit.Category + "\n\n" + kit.Contents;
            };
            _search.TextChanged += (_, _) => Filter(); _category.SelectionChanged += (_, _) => Filter(); Filter();
        }

        public void Search(string text) { _search.Text = text; Filter(); }
        private void Filter()
        {
            string category = _category.SelectedIndex > 0 ? _category.SelectedItem as string : null;
            var words = (_search.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            _list.ItemsSource = _kits.Where(x => (category == null || x.Category == category) && words.All(word => x.Name.Contains(word, StringComparison.CurrentCultureIgnoreCase))).ToList();
            _list.SelectedIndex = 0;
        }
    }
}
