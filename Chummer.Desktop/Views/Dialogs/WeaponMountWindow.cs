using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Chummer.Backend.Equipment;
using Chummer.Rules;

namespace Chummer.Desktop.Views.Dialogs
{
    public sealed class WeaponMountWindow : Window
    {
        private readonly Character _character;
        private readonly Vehicle _vehicle;
        private readonly WeaponMount _editing;
        private WeaponMountConfiguration _initial;
        private readonly Func<WeaponMountConfiguration, Task<PurchaseResult>> _purchase;
        private readonly ComboBox _size = new ComboBox { MinWidth = 250 };
        private readonly ComboBox _control = new ComboBox { MinWidth = 250 };
        private readonly ComboBox _flexibility = new ComboBox { MinWidth = 250 };
        private readonly ComboBox _visibility = new ComboBox { MinWidth = 250 };
        private readonly CheckBox _free = new CheckBox { Content = "Free mount" };
        private readonly CheckBox _blackMarket = new CheckBox { Content = "Black market discount (−10%)", IsVisible = false };
        private readonly CheckBox _another = new CheckBox { Content = "Add another after purchase" };
        private readonly NumericUpDown _markup = new NumericUpDown { Minimum = -100, Maximum = 1000, Increment = 5, Value = 0, FormatString = "0.##" };
        private readonly TextBlock _details = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        private readonly TextBlock _status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        private readonly Button _add = new Button { Content = "ADD MOUNT", IsEnabled = false, Classes = { "primary" } };
        private readonly bool _career;
        private readonly string _currency;
        private bool _changing;
        private int _version;
        private Task _load;
        public WeaponMountQuote Quote { get; private set; }
        public WeaponMountConfiguration Configuration => Input();

        public WeaponMountWindow(Character character, Vehicle vehicle, Func<WeaponMountConfiguration, Task<PurchaseResult>> purchase, WeaponMount editing = null)
        {
            _character = character; _vehicle = vehicle; _purchase = purchase; _editing = editing;
            if (editing != null) { _add.Content = "SAVE CHANGES"; _another.IsVisible = false; }
            _career = character.Created; _currency = character.Settings.NuyenFormat;
            Title = "Vehicle weapon mount"; Width = 1000; Height = 730; MinWidth = 800; MinHeight = 550;
            var root = new DockPanel { Margin = new Thickness(24) };
            var footer = new StackPanel { Spacing = 12, Margin = new Thickness(0, 18, 0, 0) };
            footer.Children.Add(_status); footer.Children.Add(_another);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            var cancel = new Button { Content = "CANCEL" }; cancel.Click += (_, _) => Close();
            _add.Click += async (_, _) => await PurchaseAsync();
            buttons.Children.Add(_add); buttons.Children.Add(cancel); footer.Children.Add(buttons); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            var content = new StackPanel { Spacing = 16 };
            content.Children.Add(new TextBlock { Text = "WEAPON MOUNT", FontSize = 22, FontWeight = Avalonia.Media.FontWeight.Bold });
            var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"), ColumnSpacing = 20, RowSpacing = 14 };
            int row = 0;
            foreach (var field in new[] { ("SIZE", _size), ("CONTROL", _control), ("FLEXIBILITY", _flexibility), ("VISIBILITY", _visibility) })
            {
                var label = new TextBlock { Text = field.Item1, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(label, row); fields.Children.Add(label); Grid.SetRow(field.Item2, row); Grid.SetColumn(field.Item2, 1); fields.Children.Add(field.Item2); ++row;
                field.Item2.ItemTemplate = new FuncDataTemplate<WeaponMountChoice>((choice, _) => new TextBlock { Text = choice?.Name ?? "" });
            }
            content.Children.Add(fields); content.Children.Add(_free); content.Children.Add(_blackMarket);
            var markup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, IsVisible = _career };
            markup.Children.Add(new TextBlock { Text = "MARKUP (%)", VerticalAlignment = VerticalAlignment.Center }); markup.Children.Add(_markup); content.Children.Add(markup);
            content.Children.Add(_details); root.Children.Add(new ScrollViewer { Content = content }); Content = root;
            _size.SelectionChanged += async (_, _) => { if (!_changing) await LoadOptionsAsync(); };
            foreach (var combo in new[] { _control, _flexibility, _visibility }) combo.SelectionChanged += async (_, _) => { if (!_changing) await RefreshQuoteAsync(); };
            _free.IsCheckedChanged += async (_, _) => { if (!_changing) await RefreshQuoteAsync(); };
            _blackMarket.IsCheckedChanged += async (_, _) => { if (!_changing) await RefreshQuoteAsync(); };
            _markup.ValueChanged += async (_, _) => { if (!_changing) await RefreshQuoteAsync(); };
            Opened += async (_, _) => await LoadAsync(); Closed += (_, _) => ++_version;
        }
        public Task LoadAsync() => _load ??= InitializeAsync();
        public async Task ConfigureAsync(WeaponMountConfiguration configuration)
        {
            await LoadAsync(); _initial = configuration; _changing = true;
            Populate(_size, ((IEnumerable<WeaponMountChoice>)_size.ItemsSource).ToList(), configuration.SizeId);
            _free.IsChecked = configuration.Free; _blackMarket.IsChecked = configuration.BlackMarket; _markup.Value = configuration.Markup;
            _changing = false; await LoadOptionsAsync();
        }
        public Task SaveAsync() => PurchaseAsync();
        private async Task InitializeAsync()
        {
            try
            {
                _status.Text = "Loading mounts…";
                var sizes = await Task.Run(() => WeaponMountRules.OptionsAsync(_character, _vehicle, "Size"));
                bool blackMarket = await Task.Run(() => WeaponMountRules.CanBlackMarketAsync(_character));
                if (_editing != null) _initial = await Task.Run(() => new WeaponMountConfiguration { SizeId = _editing.SourceIDString,
                    ControlId = _editing.WeaponMountOptions.FirstOrDefault(x => x.Category == "Control")?.SourceIDString,
                    FlexibilityId = _editing.WeaponMountOptions.FirstOrDefault(x => x.Category == "Flexibility")?.SourceIDString,
                    VisibilityId = _editing.WeaponMountOptions.FirstOrDefault(x => x.Category == "Visibility")?.SourceIDString,
                    Free = _editing.FreeCost, BlackMarket = _editing.DiscountCost });
                _changing = true; Populate(_size, sizes, _initial?.SizeId); _blackMarket.IsVisible = blackMarket || _initial?.BlackMarket == true;
                _free.IsChecked = _initial?.Free ?? false; _blackMarket.IsChecked = _initial?.BlackMarket ?? false; _changing = false;
                await LoadOptionsAsync();
            }
            catch (Exception ex) { _changing = false; _status.Text = ex.Message; }
        }
        private static void Populate(ComboBox combo, IReadOnlyList<WeaponMountChoice> options, string desired = null)
        {
            string selected = desired ?? (combo.SelectedItem as WeaponMountChoice)?.Id;
            combo.ItemsSource = options; combo.SelectedItem = options.FirstOrDefault(x => x.Id == selected) ?? options.FirstOrDefault();
        }
        private async Task LoadOptionsAsync()
        {
            int version = ++_version; _add.IsEnabled = false;
            string size = (_size.SelectedItem as WeaponMountChoice)?.Id;
            if (size == null) { _status.Text = "No mount sizes are available for this vehicle and rule preset."; return; }
            try
            {
                var options = await Task.Run(async () => new[] {
                    await WeaponMountRules.OptionsAsync(_character, _vehicle, "Control", size),
                    await WeaponMountRules.OptionsAsync(_character, _vehicle, "Flexibility", size),
                    await WeaponMountRules.OptionsAsync(_character, _vehicle, "Visibility", size) });
                if (version != _version) return;
                _changing = true; Populate(_control, options[0], _initial?.ControlId); Populate(_flexibility, options[1], _initial?.FlexibilityId); Populate(_visibility, options[2], _initial?.VisibilityId); _initial = null; _changing = false;
                await RefreshQuoteAsync();
            }
            catch (Exception ex) { _changing = false; if (version == _version) _status.Text = ex.Message; }
        }
        private WeaponMountConfiguration Input() => new WeaponMountConfiguration { SizeId = (_size.SelectedItem as WeaponMountChoice)?.Id,
            ControlId = (_control.SelectedItem as WeaponMountChoice)?.Id, FlexibilityId = (_flexibility.SelectedItem as WeaponMountChoice)?.Id,
            VisibilityId = (_visibility.SelectedItem as WeaponMountChoice)?.Id, Free = _free.IsChecked == true,
            BlackMarket = _blackMarket.IsChecked == true, Markup = _career ? _markup.Value ?? 0 : 0 };
        public async Task RefreshQuoteAsync()
        {
            int version = ++_version; _add.IsEnabled = false;
            var input = Input();
            if (input.ControlId == null || input.FlexibilityId == null || input.VisibilityId == null) { _status.Text = "Choose a compatible mount configuration."; return; }
            try
            {
                var quote = await Task.Run(() => _editing == null ? WeaponMountRules.QuoteAsync(_character, _vehicle, input) : WeaponMountRules.QuoteEditAsync(_character, _editing, input));
                if (version != _version) return;
                Quote = quote; _add.IsEnabled = quote != null && quote.CanAfford;
                string costLabel = _editing == null ? (quote?.VariableCost == true ? "FROM " : "TOTAL ") : "CHANGE ";
                _details.Text = quote == null ? "This combination is unavailable or cannot hold the mounted weapons." : costLabel + quote.Cost.ToString(_currency, GlobalSettings.CultureInfo) + "¥\n\n"
                    + "Slots: " + quote.Slots + "   Availability: " + quote.Availability + "   Weapon capacity: " + quote.WeaponCapacity
                    + "\n\nAllowed weapons: " + quote.AllowedWeapons + (quote.VariableCost ? "\n\nVariable component costs are chosen before purchase." : "");
                _status.Text = quote != null && !quote.CanAfford ? "Insufficient funds for this mount." : "";
            }
            catch (Exception ex) { if (version == _version) _status.Text = ex.Message; }
        }
        private async Task PurchaseAsync()
        {
            if (Quote == null || !Quote.CanAfford) return;
            var input = Input(); bool another = _another.IsChecked == true; IsEnabled = false;
            try
            {
                var result = await _purchase(input);
                if (result.Result == RuleResult.Changed) { if (!another) Close(); else await RefreshQuoteAsync(); }
                else _status.Text = result.Result == RuleResult.NotEnoughNuyen ? "Insufficient funds." : "The mount could not be added. Check vehicle capacity and the selected options.";
            }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { IsEnabled = true; }
        }
    }
}
