using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Chummer.Desktop.ViewModels;
using Chummer.Rules;

namespace Chummer.Desktop.Views.Dialogs
{
    public sealed class LimitModifiersWindow : Window
    {
        private readonly CharacterViewModel _owner;
        private readonly ListBox _list = new ListBox();
        private readonly TextBox _name = new TextBox { PlaceholderText = "Modifier name" };
        private readonly ComboBox _limit = new ComboBox { ItemsSource = new[] { "Physical", "Mental", "Social" }, SelectedIndex = 0 };
        private readonly NumericUpDown _bonus = new NumericUpDown { Minimum = -100, Maximum = 100, Value = 1, FormatString = "0" };
        private readonly TextBox _condition = new TextBox { PlaceholderText = "When this modifier applies" };
        private readonly TextBox _notes = new TextBox { AcceptsReturn = true, MinHeight = 100, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        private readonly TextBlock _status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        private readonly Button _save = new Button { Content = "SAVE", Classes = { "primary" } };
        private readonly Button _remove = new Button { Content = "REMOVE" };
        private readonly StackPanel _editor = new StackPanel { Spacing = 12 };
        private LimitModifier _selected;

        public LimitModifiersWindow(CharacterViewModel owner)
        {
            _owner = owner; Title = "Limit modifiers"; Width = 850; Height = 620;
            var root = new Grid { Margin = new Thickness(24), ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 24 };
            var left = new DockPanel();
            var add = new Button { Content = "NEW MODIFIER", Margin = new Thickness(0, 12, 0, 0) };
            DockPanel.SetDock(add, Dock.Bottom); left.Children.Add(add); left.Children.Add(_list); root.Children.Add(left);
            _list.ItemTemplate = new FuncDataTemplate<LimitModifier>((item, _) => new TextBlock { Text = item == null ? "" : item.Name + " · " + item.Limit + " " + item.Bonus.ToString("+0;-0;0"), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            Label("NAME", _name); Label("LIMIT", _limit); Label("BONUS", _bonus); Label("CONDITION", _condition); Label("NOTES", _notes);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 }; actions.Children.Add(_save); actions.Children.Add(_remove);
            _editor.Children.Add(actions); _editor.Children.Add(_status); Grid.SetColumn(_editor, 1); root.Children.Add(_editor); Content = root;
            add.Click += (_, _) => { _list.SelectedItem = null; Select(null); };
            _list.SelectionChanged += (_, _) => Select(_list.SelectedItem as LimitModifier);
            _save.Click += async (_, _) => await SaveAsync(); _remove.Click += async (_, _) => await RemoveAsync();
            Opened += async (_, _) => await ReloadAsync(); Select(null);
        }

        private void Label(string label, Control control) { _editor.Children.Add(new TextBlock { Text = label, Classes = { "label" } }); _editor.Children.Add(control); }
        private void Select(LimitModifier modifier)
        {
            _selected = modifier; _name.Text = modifier?.Name ?? ""; _limit.SelectedItem = modifier?.Limit ?? "Physical";
            _bonus.Value = modifier?.Bonus ?? 1; _condition.Text = modifier?.CurrentDisplayCondition ?? ""; _notes.Text = modifier?.Notes ?? "";
            bool editable = modifier == null || modifier.CanDelete;
            _name.IsReadOnly = _condition.IsReadOnly = !editable; _limit.IsEnabled = _bonus.IsEnabled = editable;
            _notes.IsReadOnly = !editable; _save.IsEnabled = editable; _remove.IsEnabled = modifier?.CanDelete == true;
            _status.Text = editable ? "" : "This modifier belongs to an ability or item.";
        }
        public async Task ReloadAsync()
        {
            var items = await Task.Run(() => _owner.Character.LimitModifiers.ToArray());
            _list.ItemsSource = items;
        }
        public async Task SaveAsync(string name = null, string limit = null, int? bonus = null, string condition = null)
        {
            if (name != null) _name.Text = name; if (limit != null) _limit.SelectedItem = limit;
            if (bonus.HasValue) _bonus.Value = bonus; if (condition != null) _condition.Text = condition;
            string selectedName = _name.Text, selectedLimit = _limit.SelectedItem as string, selectedCondition = _condition.Text, notes = _notes.Text;
            int value = decimal.ToInt32(_bonus.Value ?? 0); var selected = _selected;
            RuleResult result = RuleResult.NotAllowed; _editor.IsEnabled = false;
            try
            {
                await _owner.RunRuleAsync(async t => result = await LimitModifierRules.SaveAsync(_owner.Character, selectedName, selectedLimit, value, selectedCondition, notes, selected, t));
                if (result == RuleResult.Changed) { await ReloadAsync(); Select(null); _status.Text = "Modifier saved."; }
                else _status.Text = "Enter a name and select a limit for an editable modifier.";
            }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { _editor.IsEnabled = true; }
        }
        private async Task RemoveAsync()
        {
            var selected = _selected; if (selected == null || !selected.CanDelete) return;
            _editor.IsEnabled = false;
            try { await _owner.RunRuleAsync(t => LimitModifierRules.RemoveAsync(_owner.Character, selected, t)); await ReloadAsync(); Select(null); }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { _editor.IsEnabled = true; }
        }
    }
}
