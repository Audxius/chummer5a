using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Chummer.Desktop.ViewModels;

namespace Chummer.Desktop.Views.Dialogs
{
    public sealed class CharacterSettingsWindow : Window
    {
        private readonly TextBox _search = new TextBox { PlaceholderText = "Search rules and karma costs…", Margin = new Thickness(0, 0, 0, 12) };
        public void Search(string text) => _search.Text = text;
        public CharacterSettingsWindow(CharacterSettingsEditorViewModel editor, Func<string, Task<bool>> save)
        {
            Title = "Character rules";
            Width = 1100; Height = 800; MinWidth = 750; MinHeight = 500;
            var root = new DockPanel { Margin = new Thickness(24) };
            var footer = new StackPanel { Spacing = 10 };
            var name = new TextBox { Text = editor.Settings.Name, PlaceholderText = "Settings name" };
            var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            var apply = new Button { Content = "SAVE AS NEW PRESET", Classes = { "primary" } };
            var cancel = new Button { Content = "CANCEL" };
            cancel.Click += (_, _) => Close(false);
            apply.Click += async (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(name.Text)) { error.Text = "Enter a settings name."; return; }
                if (!editor.Validate()) { error.Text = "Correct invalid expressions before saving."; return; }
                apply.IsEnabled = false;
                try { if (await save(name.Text.Trim())) Close(true); else error.Text = "The settings could not be saved."; }
                catch (Exception ex) { error.Text = ex.Message; }
                finally { apply.IsEnabled = true; }
            };
            buttons.Children.Add(apply); buttons.Children.Add(cancel);
            footer.Children.Add(name); footer.Children.Add(error); footer.Children.Add(buttons);
            DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            var search = _search;
            DockPanel.SetDock(search, Dock.Top); root.Children.Add(search);
            var content = new StackPanel { Spacing = 16 };
            foreach (var group in editor.Groups)
            {
                var card = new StackPanel { Spacing = 8 };
                card.Children.Add(new TextBlock { Text = group.Title, Classes = { "h2" } });
                foreach (var option in group.Options) card.Children.Add(Row(option));
                var border = new Border { Child = card, Padding = new Thickness(16), Classes = { "panel" } };
                search.TextChanged += (_, _) => border.IsVisible = string.IsNullOrWhiteSpace(search.Text)
                    || group.Title.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)
                    || group.Options.Any(x => x.Label.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)
                        || x is FlagsSettingOption flags && flags.Flags.Any(flag => flag.Label.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase))
                        || x is CustomDirectorySettingOption directories && directories.Directories.Any(directory => directory.Name.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)));
                content.Children.Add(border);
            }
            root.Children.Add(new ScrollViewer { Content = content });
            Content = root;
        }

        internal static Control Row(SettingOption option)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,380"), ColumnSpacing = 20, DataContext = option };
            row.Bind(IsVisibleProperty, new Binding(nameof(SettingOption.IsVisible)));
            row.Bind(IsEnabledProperty, new Binding(nameof(SettingOption.IsEnabled)));
            row.Children.Add(new TextBlock { Text = option.Label, TextWrapping = Avalonia.Media.TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            Control edit;
            switch (option)
            {
                case BoolSettingOption boolean:
                    edit = new CheckBox(); edit.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(boolean.Value)) { Mode = BindingMode.TwoWay }); break;
                case NumberSettingOption number: edit = Number(number); break;
                case TextSettingOption text:
                    edit = new TextBox(); edit.Bind(TextBox.TextProperty, new Binding(nameof(text.Value)) { Mode = BindingMode.TwoWay });
                    text.PropertyChanged += (_, _) => edit.Classes.Set("invalid", text.IsInvalid); break;
                case ChoiceSettingOption choice:
                    edit = new ComboBox { ItemsSource = choice.Items, HorizontalAlignment = HorizontalAlignment.Stretch };
                    ((ComboBox)edit).ItemTemplate = new FuncDataTemplate<ListItem>((item, _) => new TextBlock { Text = item.Name });
                    edit.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(choice.Selected)) { Mode = BindingMode.TwoWay }); break;
                case ToggleNumberSettingOption toggle:
                    var pair = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                    var check = new CheckBox(); check.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(toggle.Value)) { Mode = BindingMode.TwoWay });
                    pair.Children.Add(check); pair.Children.Add(Number(toggle.Number)); edit = pair; break;
                case RangeSettingOption range:
                    var bounds = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                    bounds.Children.Add(Number(range.Minimum)); bounds.Children.Add(new TextBlock { Text = range.BetweenText }); bounds.Children.Add(Number(range.Maximum)); edit = bounds; break;
                case FlagsSettingOption flags:
                    var flagList = new ItemsControl { ItemsSource = flags.Flags, ItemsPanel = new FuncTemplate<Panel>(() => new WrapPanel()) };
                    flagList.ItemTemplate = new FuncDataTemplate<FlagItem>((flag, _) =>
                    {
                        var box = new CheckBox { Content = flag.Label, DataContext = flag, Margin = new Thickness(0, 0, 12, 0) };
                        box.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(flag.Value)) { Mode = BindingMode.TwoWay });
                        box.Bind(IsEnabledProperty, new Binding(nameof(flag.IsEnabled)));
                        return box;
                    });
                    edit = flagList; break;
                case CustomDirectorySettingOption directories:
                    var directoryList = new ItemsControl { ItemsSource = directories.Directories };
                    directoryList.ItemTemplate = new FuncDataTemplate<CustomDirectoryRow>((item, _) =>
                    {
                        var directory = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 10, Margin = new Thickness(0, 4) };
                        var enabled = new CheckBox { Content = item.Name, DataContext = item };
                        enabled.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(item.Value)) { Mode = BindingMode.TwoWay });
                        ToolTip.SetTip(enabled, item.Path); directory.Children.Add(enabled);
                        var up = new Button { Content = "↑", Command = item.MoveUpCommand };
                        var down = new Button { Content = "↓", Command = item.MoveDownCommand };
                        Grid.SetColumn(up, 1); directory.Children.Add(up); Grid.SetColumn(down, 2); directory.Children.Add(down);
                        return directory;
                    });
                    edit = directoryList; break;
                default: edit = new TextBlock(); break;
            }
            if (option.HasSuffix)
            {
                var withUnit = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                withUnit.Children.Add(edit);
                withUnit.Children.Add(new TextBlock { Text = option.Suffix, VerticalAlignment = VerticalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                edit = withUnit;
            }
            if (option is FlagsSettingOption || option is CustomDirectorySettingOption)
            {
                row.RowDefinitions = new RowDefinitions("Auto,Auto");
                row.RowSpacing = 10;
                Grid.SetRow(edit, 1); Grid.SetColumnSpan(edit, 2);
            }
            else Grid.SetColumn(edit, 1);
            row.Children.Add(edit);
            if (!string.IsNullOrEmpty(option.Tip)) ToolTip.SetTip(row, option.Tip);
            return row;
        }

        private static NumericUpDown Number(NumberSettingOption option)
        {
            var control = new NumericUpDown { DataContext = option, FormatString = option.FormatString, MinWidth = 100 };
            control.Bind(NumericUpDown.MinimumProperty, new Binding(nameof(option.Minimum)));
            control.Bind(NumericUpDown.MaximumProperty, new Binding(nameof(option.Maximum)));
            control.Bind(NumericUpDown.ValueProperty, new Binding(nameof(option.Value)) { Mode = BindingMode.TwoWay });
            control.Bind(IsEnabledProperty, new Binding(nameof(option.IsEnabled)));
            return control;
        }
    }
}
