using System;
using System.Collections.Generic;
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
    public sealed class CustomDrugWindow : Window
    {
        private sealed class RecipeRow
        {
            public string Name { get; set; }
            public CustomDrugIngredient Ingredient { get; set; }
        }
        private readonly CharacterViewModel _owner;
        private readonly TextBox _name = new TextBox { PlaceholderText = "Custom drug name" };
        private readonly ComboBox _grade = new ComboBox { MinWidth = 160 };
        private readonly TextBox _search = new TextBox { PlaceholderText = "Search components" };
        private readonly ListBox _available = new ListBox();
        private readonly ListBox _chosen = new ListBox();
        private readonly ComboBox _level = new ComboBox { MinWidth = 100 };
        private readonly TextBox _description = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        private readonly TextBlock _status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        private readonly CheckBox _free = new CheckBox { Content = "Free" };
        private readonly NumericUpDown _markup = new NumericUpDown { Minimum = -100, Maximum = 1000, Value = 0, FormatString = "0.##", Width = 100 };
        private readonly Button _buy = new Button { Content = "CREATE / BUY ONE DOSE", Classes = { "primary" }, IsEnabled = false };
        private readonly Grid _form = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 16 };
        private readonly List<RecipeRow> _recipe = new List<RecipeRow>();
        private IReadOnlyList<DrugComponent> _components;
        private Task _load;
        private int _version;
        private bool _changing;
        public CustomDrugQuote Quote { get; private set; }

        public CustomDrugWindow(CharacterViewModel owner)
        {
            _owner = owner; Title = "Create custom drug"; Width = 1150; Height = 760;
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 12 };
            header.Children.Add(_name); Grid.SetColumn(_grade, 1); header.Children.Add(_grade); Grid.SetColumn(_free, 2); header.Children.Add(_free);
            var markup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; markup.Children.Add(new TextBlock { Text = "MARKUP %", VerticalAlignment = VerticalAlignment.Center }); markup.Children.Add(_markup);
            Grid.SetColumn(markup, 3); header.Children.Add(markup); _form.Children.Add(header);
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 16 }; Grid.SetRow(body, 1); _form.Children.Add(body);
            var available = new DockPanel(); DockPanel.SetDock(_search, Dock.Top); available.Children.Add(_search);
            var add = new Button { Content = "ADD COMPONENT", Margin = new Thickness(0, 12, 0, 0) };
            var choose = new StackPanel { Spacing = 8 }; choose.Children.Add(_level); choose.Children.Add(add); DockPanel.SetDock(choose, Dock.Bottom); available.Children.Add(choose); available.Children.Add(_available); body.Children.Add(available);
            var chosen = new DockPanel(); Grid.SetColumn(chosen, 1); body.Children.Add(chosen);
            var remove = new Button { Content = "REMOVE COMPONENT", Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(remove, Dock.Bottom); chosen.Children.Add(remove); chosen.Children.Add(_chosen);
            Grid.SetColumn(_description, 2); body.Children.Add(_description);
            var footer = new StackPanel { Spacing = 12 }; footer.Children.Add(_status); footer.Children.Add(_buy); Grid.SetRow(footer, 2); _form.Children.Add(footer); Content = _form;
            _available.ItemTemplate = new FuncDataTemplate<DrugComponent>((item, _) => new TextBlock { Text = item == null ? "" : item.Name + " · " + item.Category, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            _chosen.ItemTemplate = new FuncDataTemplate<RecipeRow>((item, _) => new TextBlock { Text = item?.Name ?? "", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            _grade.ItemTemplate = new FuncDataTemplate<ListItem>((item, _) => new TextBlock { Text = item.Name });
            _search.TextChanged += (_, _) => Filter();
            _available.SelectionChanged += (_, _) => { _level.ItemsSource = Enumerable.Range(1, (_available.SelectedItem as DrugComponent)?.DrugEffects.Count ?? 0).ToArray(); _level.SelectedIndex = 0; };
            add.Click += async (_, _) =>
            {
                if (!(_available.SelectedItem is DrugComponent component) || _level.SelectedIndex < 0) return;
                _recipe.Add(new RecipeRow { Name = component.Name + " · Level " + (_level.SelectedIndex + 1), Ingredient = new CustomDrugIngredient { ComponentId = component.SourceIDString, Level = _level.SelectedIndex } });
                _chosen.ItemsSource = _recipe.ToArray(); await RefreshAsync();
            };
            remove.Click += async (_, _) => { if (_chosen.SelectedItem is RecipeRow row) { _recipe.Remove(row); _chosen.ItemsSource = _recipe.ToArray(); await RefreshAsync(); } };
            _name.TextChanged += async (_, _) => { if (!_changing) await RefreshAsync(); };
            _grade.SelectionChanged += async (_, _) => { if (!_changing) await RefreshAsync(); };
            _free.IsCheckedChanged += async (_, _) => { if (!_changing) await RefreshAsync(); };
            _markup.ValueChanged += async (_, _) => { if (!_changing) await RefreshAsync(); };
            _buy.Click += async (_, _) => await PurchaseAsync(); Opened += async (_, _) => await LoadAsync(); Closed += (_, _) => ++_version;
        }
        public Task LoadAsync() => _load ??= LoadCoreAsync();
        private async Task LoadCoreAsync()
        {
            _changing = true;
            try
            {
                _components = await Task.Run(() => CustomDrugRules.ComponentsAsync(_owner.Character));
                var grades = await Task.Run(async () => (await _owner.Character.GetGradesListAsync(Improvement.ImprovementSource.Drug)).Select(x => new ListItem(x.SourceIDString, x.CurrentDisplayName)).ToArray());
                _grade.ItemsSource = grades.Length == 0 ? new[] { new ListItem("", "Default") } : grades; _grade.SelectedIndex = 0; Filter();
            }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { _changing = false; }
            await RefreshAsync();
        }
        private void Filter()
        {
            if (_components == null) return; string query = _search.Text ?? "";
            _available.ItemsSource = _components.Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Category.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        private CustomDrugDesign Design()
        {
            var design = new CustomDrugDesign { Name = _name.Text, GradeId = (_grade.SelectedItem as ListItem?)?.Value?.ToString(), Free = _free.IsChecked == true, Markup = _markup.Value ?? 0 };
            design.Ingredients.AddRange(_recipe.Select(x => new CustomDrugIngredient { ComponentId = x.Ingredient.ComponentId, Level = x.Ingredient.Level })); return design;
        }
        public async Task ConfigureAsync(CustomDrugDesign design)
        {
            await LoadAsync(); _changing = true; _name.Text = design.Name; _free.IsChecked = design.Free; _markup.Value = design.Markup; _recipe.Clear();
            if (!string.IsNullOrEmpty(design.GradeId)) _grade.SelectedIndex = _grade.ItemsSource.OfType<ListItem>().ToList().FindIndex(x => x.Value?.ToString() == design.GradeId || x.Name == design.GradeId);
            foreach (var ingredient in design.Ingredients)
            {
                var component = _components.First(x => x.SourceIDString == ingredient.ComponentId || x.Name == ingredient.ComponentId);
                _recipe.Add(new RecipeRow { Name = component.Name + " · Level " + (ingredient.Level + 1), Ingredient = new CustomDrugIngredient { ComponentId = component.SourceIDString, Level = ingredient.Level } });
            }
            _chosen.ItemsSource = _recipe.ToArray(); _changing = false; await RefreshAsync();
        }
        public async Task RefreshAsync()
        {
            int version = ++_version; _buy.IsEnabled = false; Quote = null; var design = Design();
            try
            {
                await Task.Delay(120); if (version != _version) return;
                var quote = await Task.Run(() => CustomDrugRules.QuoteAsync(_owner.Character, design)); if (version != _version) return;
                Quote = quote; _description.Text = quote.Description; _status.Text = "One dose: " + quote.Cost.ToString("N2") + "¥ · Availability " + quote.Availability; _buy.IsEnabled = true;
            }
            catch (Exception ex) { if (version == _version) { _description.Text = ""; _status.Text = ex.Message; } }
        }
        public async Task PurchaseAsync()
        {
            var design = Design(); _form.IsEnabled = false; ++_version;
            try
            {
                PurchaseResult result = null; await _owner.RunRuleAsync(async t => { result = await CustomDrugRules.PurchaseAsync(_owner.Character, design, t); return result.Result; });
                if (result?.Result == RuleResult.Changed) Close(); else _status.Text = "The custom drug could not be purchased.";
            }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { _form.IsEnabled = true; }
        }
    }
}
