using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Chummer.Selection;

namespace Chummer.Desktop.Views.Dialogs
{
    /// <summary>Native front end for the shared priority and karma metatype selector.</summary>
    public sealed class MetatypeWindow : Window
    {
        private readonly MetatypeSelector _selector;
        private readonly StackPanel _choices = new StackPanel { Spacing = 12 };
        private readonly TextBlock _details = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        private readonly Button _apply = new Button { Content = "CREATE RUNNER", Classes = { "primary" } };
        public bool Accepted { get; private set; }

        public MetatypeWindow(MetatypeSelector selector, bool editing = false)
        {
            _selector = selector;
            Title = (editing ? "Metatype and priorities // " : "New runner // ") + selector.BuildMethod;
            if (editing) _apply.Content = "APPLY CHANGES";
            Width = 960; Height = 780; MinWidth = 700; MinHeight = 500;
            var root = new DockPanel { Margin = new Thickness(24) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = new Button { Content = "CANCEL" };
            cancel.Click += (_, _) => Close(false);
            _apply.Click += async (_, _) =>
            {
                _apply.IsEnabled = false;
                try { if (await Task.Run(() => selector.ApplyAsync())) { Accepted = true; Close(true); } }
                catch (Exception ex) { _details.Text = ex.Message; }
                finally { _apply.IsEnabled = true; }
            };
            buttons.Children.Add(_apply); buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 28 };
            grid.Children.Add(new ScrollViewer { Content = _choices });
            Grid.SetColumn(_details, 1); grid.Children.Add(_details);
            root.Children.Add(grid); Content = root;
            selector.OnHeritageOrTalentChanged();
            Refresh();
        }

        private void Choice(string label, IReadOnlyList<ListItem> items, string selected, Action<string> set)
        {
            var combo = new ComboBox { ItemsSource = items, HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.ItemTemplate = new FuncDataTemplate<ListItem>((item, _) => new TextBlock { Text = item.Name });
            combo.SelectedItem = items.FirstOrDefault(x => x.Value?.ToString() == selected);
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is not ListItem item || item.Value == null) return;
                set(item.Value.ToString()); Refresh();
            };
            _choices.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), Classes = { "label" } });
            _choices.Children.Add(combo);
        }

        private void Refresh()
        {
            _choices.Children.Clear();
            if (_selector.UsesPriorityTables)
                foreach (string category in MetatypeSelector.PriorityCategories)
                {
                    string captured = category;
                    Choice(category, _selector.GetPriorityOptions(category), _selector.GetPriority(category), value => _selector.SetPriority(captured, value));
                }
            Choice("Category", _selector.GetCategories(), _selector.Category, value => { _selector.Category = value; _selector.OnCategoryChanged(); });
            Choice("Metatype", _selector.GetMetatypes(), _selector.Metatype, value => { _selector.Metatype = value; _selector.OnMetatypeChanged(); });
            Choice("Metavariant", _selector.GetMetavariants(), _selector.Metavariant, value => { _selector.Metavariant = value; _selector.OnMetavariantChanged(); });
            var force = _selector.GetForceInfo();
            if (force != null)
            {
                _choices.Children.Add(new TextBlock { Text = force.Value.Label });
                var number = new NumericUpDown { Minimum = 1, Maximum = force.Value.Maximum, Value = _selector.Force };
                number.ValueChanged += (_, _) => _selector.Force = (int)(number.Value ?? 1);
                _choices.Children.Add(number);
                var possession = new CheckBox { Content = "Possession based", IsChecked = _selector.PossessionBased };
                possession.IsCheckedChanged += (_, _) => _selector.PossessionBased = possession.IsChecked == true;
                _choices.Children.Add(possession);
                Choice("Possession method", _selector.PossessionMethods, _selector.PossessionMethod, value => _selector.PossessionMethod = value);
            }
            if (_selector.UsesPriorityTables)
            {
                Choice("Magic / resonance", _selector.GetTalents(), _selector.TalentValue, value => { _selector.TalentValue = value; _selector.NormalizeSkills(); });
                var skills = _selector.GetTalentSkillChoice();
                for (int i = 0; i < skills.Count; i++)
                {
                    int index = i;
                    Choice("Talent skill " + (i + 1), skills.Options, _selector.GetSkill(i), value => { _selector.SetSkill(index, value); _selector.NormalizeSkills(); });
                }
            }
            var details = _selector.GetDetails();
            _details.Text = details.Name + "\n\n" + string.Join("\n", details.Attributes.Select(x => x.Abbrev + "   " + x.Display))
                + "\n\nKarma: " + details.Karma + "\nSpecial attribute points: " + details.SpecialAttributePoints
                + (_selector.IsSumToTen ? "\nSum to ten: " + _selector.SumToTen + " / " + _selector.SumToTenTarget : "")
                + "\n\n" + string.Join("\n", details.Qualities) + "\n\n" + details.Source;
            _apply.IsEnabled = !details.NotSupported && !string.IsNullOrEmpty(_selector.Metatype)
                && (!_selector.IsSumToTen || _selector.SumToTen == _selector.SumToTenTarget);
        }
    }
}
