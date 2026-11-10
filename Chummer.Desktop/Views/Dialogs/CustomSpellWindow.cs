using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Chummer.Rules;

namespace Chummer.Desktop.Views.Dialogs
{
    public sealed class CustomSpellWindow : Window
    {
        private readonly CustomSpellDesign _design;
        private readonly TextBox _name = new TextBox { PlaceholderText = "Spell name" };
        private readonly ComboBox _category = new ComboBox();
        private readonly ComboBox _type = new ComboBox();
        private readonly ComboBox _range = new ComboBox();
        private readonly ComboBox _duration = new ComboBox();
        private readonly CheckBox _area = new CheckBox { Content = "Area effect" };
        private readonly CheckBox _limited = new CheckBox { Content = "Limited spell" };
        private readonly ComboBox _restrictionLevel = new ComboBox();
        private readonly TextBox _restriction = new TextBox { PlaceholderText = "Restriction" };
        private readonly NumericUpDown _effects = new NumericUpDown { Minimum = 1, Maximum = 10, Increment = 1, FormatString = "0" };
        private readonly UniformGrid _modifiers = new UniformGrid { Columns = 2 };
        private readonly Dictionary<int, CheckBox> _checks = new Dictionary<int, CheckBox>();
        private readonly TextBlock _drain = new TextBlock { FontSize = 22, FontWeight = Avalonia.Media.FontWeight.Bold };
        private readonly TextBlock _descriptors = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        private readonly TextBlock _errors = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        private readonly Button _accept = new Button { Content = "CREATE SPELL", Classes = { "primary" } };
        private bool _changing;
        public CustomSpellDesign SelectedDesign { get; private set; }

        public CustomSpellWindow(CustomSpellDesign design = null)
        {
            _design = design ?? new CustomSpellDesign(); Title = "Create spell"; Width = 1000; Height = 820; MinWidth = 780; MinHeight = 600;
            var root = new DockPanel { Margin = new Thickness(24) };
            var footer = new StackPanel { Spacing = 12, Margin = new Thickness(0, 18, 0, 0) };
            footer.Children.Add(_drain); footer.Children.Add(_descriptors);
            footer.Children.Add(new ScrollViewer { Content = _errors, MaxHeight = 100 });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            var cancel = new Button { Content = "CANCEL" }; cancel.Click += (_, _) => Close();
            _accept.Click += (_, _) => { Update(); if (CustomSpellRules.Validate(_design).Count == 0) { SelectedDesign = _design; Close(); } };
            buttons.Children.Add(_accept); buttons.Children.Add(cancel); footer.Children.Add(buttons); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            var content = new StackPanel { Spacing = 14 };
            content.Children.Add(new TextBlock { Text = "Design a spell using the legacy spell-creation rules.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            _name.Text = _design.Name; content.Children.Add(_name);
            SetOptions(_category, new[] { "Combat", "Detection", "Health", "Illusion", "Manipulation", "Enchantments", "Rituals" }.Select(x => new ListItem(x, x)).ToList(), _design.Category);
            SetOptions(_type, new[] { new ListItem("P", "Physical"), new ListItem("M", "Mana") }, _design.Type);
            SetOptions(_range, new[] { new ListItem("T", "Touch"), new ListItem("LOS", "Line of sight") }, _design.Range);
            SetOptions(_duration, new[] { new ListItem("I", "Instant"), new ListItem("S", "Sustained"), new ListItem("P", "Permanent") }, _design.Duration);
            var fields = new UniformGrid { Columns = 4 };
            AddField(fields, "CATEGORY", _category); AddField(fields, "TYPE", _type); AddField(fields, "RANGE", _range); AddField(fields, "DURATION", _duration); content.Children.Add(fields);
            _area.IsChecked = _design.Area; _limited.IsChecked = _design.Limited;
            var flags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 }; flags.Children.Add(_area); flags.Children.Add(_limited); content.Children.Add(flags);
            SetOptions(_restrictionLevel, new[] { new ListItem("0", "Unrestricted"), new ListItem("1", "Restricted (−1 drain)"), new ListItem("2", "Very restricted (−2 drain)") }, _design.VeryRestricted ? "2" : _design.Restricted ? "1" : "0");
            _restriction.Text = _design.Restriction;
            var restrictions = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 16 }; restrictions.Children.Add(_restrictionLevel); Grid.SetColumn(_restriction, 1); restrictions.Children.Add(_restriction); content.Children.Add(restrictions);
            _effects.Value = _design.NumberOfEffects;
            var effects = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 }; effects.Children.Add(new TextBlock { Text = "NUMBER OF EFFECTS", VerticalAlignment = VerticalAlignment.Center }); effects.Children.Add(_effects); content.Children.Add(effects);
            content.Children.Add(_modifiers); root.Children.Add(new ScrollViewer { Content = content }); Content = root;
            BuildModifiers();
            _category.SelectionChanged += (_, _) => { if (_changing) return; _design.Category = Value(_category); _design.Modifiers.Clear(); if (_design.Category == "Health") _area.IsChecked = false; BuildModifiers(); Update(); };
            foreach (var combo in new[] { _type, _range, _duration, _restrictionLevel }) combo.SelectionChanged += (_, _) => Update();
            _name.TextChanged += (_, _) => Update(); _restriction.TextChanged += (_, _) => Update();
            _area.IsCheckedChanged += (_, _) => Update(); _limited.IsCheckedChanged += (_, _) => Update(); _effects.ValueChanged += (_, _) => Update();
            Update();
        }
        private static void AddField(Panel parent, string label, Control control)
        {
            var field = new StackPanel { Spacing = 5, Margin = new Thickness(0, 0, 12, 0) };
            field.Children.Add(new TextBlock { Text = label, FontSize = 11 }); field.Children.Add(control); parent.Children.Add(field);
        }
        private static void SetOptions(ComboBox control, IEnumerable<ListItem> values, string selected)
        {
            var options = values.ToList(); control.ItemsSource = options;
            control.ItemTemplate = new FuncDataTemplate<ListItem>((item, _) => new TextBlock { Text = item.Name });
            control.SelectedItem = options.First(x => x.Value.ToString() == selected);
        }
        private static string Value(ComboBox combo) => (combo.SelectedItem as ListItem?)?.Value?.ToString() ?? "";
        private void BuildModifiers()
        {
            _modifiers.Children.Clear(); _checks.Clear();
            foreach (var definition in CustomSpellRules.Modifiers(_design.Category))
            {
                int index = definition.Index;
                var check = new CheckBox { Content = definition.Name + " (" + (definition.Drain >= 0 ? "+" : "") + definition.Drain + ")", IsChecked = _design.Modifiers.Contains(index), Margin = new Thickness(0, 3) };
                _checks[index] = check; _modifiers.Children.Add(check);
                check.IsCheckedChanged += (_, _) =>
                {
                    if (_changing) return;
                    _changing = true;
                    _design.SetModifier(index, check.IsChecked == true);
                    foreach (var pair in _checks) pair.Value.IsChecked = _design.Modifiers.Contains(pair.Key);
                    if (_design.Category == "Combat" && _design.Modifiers.Contains(2)) _type.SelectedItem = ((IEnumerable<ListItem>)_type.ItemsSource).First(x => x.Value.ToString() == "P");
                    _changing = false; Update();
                };
            }
        }
        private void Update()
        {
            if (_changing) return;
            _design.Name = _name.Text ?? ""; _design.Type = Value(_type); _design.Range = Value(_range); _design.Duration = Value(_duration);
            _design.Area = _area.IsChecked == true; _design.Limited = _limited.IsChecked == true;
            _design.Restricted = Value(_restrictionLevel) == "1"; _design.VeryRestricted = Value(_restrictionLevel) == "2";
            _design.Restriction = _restriction.Text ?? ""; _design.NumberOfEffects = (int)(_effects.Value ?? 1);
            _restriction.IsEnabled = _design.Restricted || _design.VeryRestricted;
            _area.IsEnabled = _design.Category != "Health"; _type.IsEnabled = _design.Category != "Combat" || !_design.Modifiers.Contains(2);
            _effects.IsEnabled = _design.Category == "Combat" && _design.Modifiers.Contains(3) || _design.Category == "Manipulation" && _design.Modifiers.Contains(6);
            _drain.Text = "DRAIN  " + CustomSpellRules.Drain(_design); _descriptors.Text = CustomSpellRules.Descriptors(_design);
            var errors = CustomSpellRules.Validate(_design); _errors.Text = string.Join(Environment.NewLine, errors); _accept.IsEnabled = errors.Count == 0;
        }
    }
}
