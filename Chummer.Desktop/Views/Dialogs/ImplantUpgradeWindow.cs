using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Chummer.Backend.Equipment;
using Chummer.Desktop.ViewModels;
using Chummer.Rules;

namespace Chummer.Desktop.Views.Dialogs
{
    public sealed class ImplantUpgradeWindow : Window
    {
        private readonly CharacterViewModel _owner;
        private readonly Cyberware _implant;
        private readonly ComboBox _grade = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly NumericUpDown _rating = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 0, FormatString = "0" };
        private readonly NumericUpDown _refund = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 50, Increment = 5, FormatString = "0.##" };
        private readonly CheckBox _free = new CheckBox { Content = "Free upgrade" };
        private readonly TextBlock _name = new TextBlock { FontSize = 22, FontWeight = Avalonia.Media.FontWeight.Bold, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        private readonly TextBlock _details = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        private readonly TextBlock _status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        private readonly Button _apply = new Button { Content = "APPLY CHANGES", Classes = { "primary" }, IsEnabled = false };
        private readonly StackPanel _form = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        private string _currency = "#,0.##";
        private int _version;
        private bool _changing;
        private Task _load;
        public ImplantUpgradeQuote Quote { get; private set; }

        public ImplantUpgradeWindow(CharacterViewModel owner, Cyberware implant)
        {
            _owner = owner; _implant = implant; Title = owner.IsCareerMode ? "Upgrade implant" : "Edit implant"; Width = 700; Height = 720;
            _form.Children.Add(_name);
            void Field(string label, Control control) { _form.Children.Add(new TextBlock { Text = label, Classes = { "label" } }); _form.Children.Add(control); }
            Field("GRADE", _grade); Field("RATING", _rating);
            var resale = new StackPanel { Spacing = 8, IsVisible = owner.IsCareerMode };
            resale.Children.Add(new TextBlock { Text = "TRADE-IN CREDIT (%)", Classes = { "label" } }); resale.Children.Add(_refund); resale.Children.Add(_free); _form.Children.Add(resale);
            _form.Children.Add(_details); _form.Children.Add(_status);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 }; buttons.Children.Add(_apply);
            var cancel = new Button { Content = "CANCEL" }; cancel.Click += (_, _) => Close(); buttons.Children.Add(cancel); _form.Children.Add(buttons); Content = new ScrollViewer { Content = _form };
            _grade.ItemTemplate = new FuncDataTemplate<ListItem>((item, _) => new TextBlock { Text = item.Name });
            _grade.SelectionChanged += async (_, _) => { if (!_changing) await RefreshAsync(); };
            _rating.ValueChanged += async (_, _) => { if (!_changing) await RefreshAsync(); };
            _refund.ValueChanged += async (_, _) => { if (!_changing) await RefreshAsync(); };
            _free.IsCheckedChanged += async (_, _) => { if (!_changing) await RefreshAsync(); };
            _apply.Click += async (_, _) => await ApplyAsync(); Opened += async (_, _) => await LoadAsync(); Closed += (_, _) => ++_version;
        }
        public Task LoadAsync() => _load ??= LoadCoreAsync();
        private async Task LoadCoreAsync()
        {
            _changing = true;
            try
            {
                var snapshot = await Task.Run(async () => (Options: await ImplantUpgradeRules.OptionsAsync(_owner.Character, _implant),
                    Name: await _implant.GetCurrentDisplayNameAsync(), Currency: (await _owner.Character.GetSettingsAsync()).NuyenFormat));
                _name.Text = snapshot.Name; _currency = snapshot.Currency;
                var options = snapshot.Options; if (options == null) { _status.Text = "This implant cannot be edited."; return; }
                _grade.ItemsSource = options.Grades; int current = options.Grades.FindIndex(x => x.Value?.ToString() == options.CurrentGrade);
                _grade.SelectedIndex = current >= 0 ? current : options.Grades.Count > 0 ? 0 : -1; _grade.IsEnabled = options.Grades.Count > 1;
                _rating.Minimum = options.MinimumRating; _rating.Maximum = Math.Max(options.MinimumRating, options.MaximumRating);
                _rating.Value = options.CurrentRating; _rating.IsEnabled = options.MaximumRating > options.MinimumRating;
            }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { _changing = false; }
            await RefreshAsync();
        }
        public async Task ConfigureAsync(string grade, int rating, decimal refundPercent = 50, bool free = false)
        {
            await LoadAsync(); _changing = true;
            var values = _grade.ItemsSource.OfType<ListItem>().ToList(); _grade.SelectedIndex = values.FindIndex(x => x.Value?.ToString() == grade || x.Name == grade);
            _rating.Value = rating; _refund.Value = refundPercent; _free.IsChecked = free; _changing = false; await RefreshAsync();
        }
        public async Task RefreshAsync()
        {
            int version = ++_version; _apply.IsEnabled = false; Quote = null;
            string grade = (_grade.SelectedItem as ListItem?)?.Value?.ToString(); int rating = decimal.ToInt32(_rating.Value ?? 0);
            decimal refund = (_refund.Value ?? 0) / 100m; bool free = _free.IsChecked == true;
            try
            {
                var quote = await Task.Run(() => ImplantUpgradeRules.QuoteAsync(_owner.Character, _implant, grade, rating, refund, free));
                if (version != _version) return; Quote = quote;
                _apply.IsEnabled = quote?.CanApply == true;
                if (quote == null) { _details.Text = ""; _status.Text = "Select an available grade and rating."; return; }
                string Money(decimal value) => value.ToString(_currency, GlobalSettings.CultureInfo) + "¥";
                _details.Text = quote.OldGrade + " → " + quote.NewGrade + "\nImplant value: " + Money(quote.OldValue) + " → " + Money(quote.NewValue)
                    + "\nTrade-in credit: " + Money(quote.TradeIn) + "\n" + (quote.Cost < 0 ? "Refund: " + Money(-quote.Cost) : "Charged now: " + Money(quote.Cost))
                    + "\nEssence: " + quote.OldEssence.ToString("0.##") + " → " + quote.NewEssence.ToString("0.##")
                    + "\nCapacity: " + quote.OldCapacity + " → " + quote.NewCapacity;
                _status.Text = quote.Message;
            }
            catch (Exception ex) { if (version == _version) _status.Text = ex.Message; }
        }
        public async Task ApplyAsync()
        {
            if (Quote?.CanApply != true) return;
            string grade = (_grade.SelectedItem as ListItem?)?.Value?.ToString(); int rating = decimal.ToInt32(_rating.Value ?? 0);
            decimal refund = (_refund.Value ?? 0) / 100m; bool free = _free.IsChecked == true; _form.IsEnabled = false; ++_version;
            try
            {
                PurchaseResult result = null;
                await _owner.RunRuleAsync(async t => { result = await ImplantUpgradeRules.ApplyAsync(_owner.Character, _implant, grade, rating, refund, free, t); return result.Result; });
                if (result?.Result == RuleResult.Changed) Close(); else { await RefreshAsync(); _status.Text = "The implant could not be changed."; }
            }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { _form.IsEnabled = true; }
        }
    }
}
