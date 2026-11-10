using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Chummer.Rules;
using Chummer.Selection;
using Chummer.Backend.Skills;

namespace Chummer.Desktop.Views.Dialogs
{
    public sealed class CustomImprovementWindow : Window
    {
        public CustomImprovementInput Result { get; private set; }

        public CustomImprovementWindow(Character character, IReadOnlyList<CustomImprovementDefinition> definitions, Improvement existing = null)
        {
            Title = existing == null ? "Add custom improvement" : "Edit custom improvement";
            Width = 900; Height = 700; MinWidth = 700; MinHeight = 500;
            var root = new DockPanel { Margin = new Thickness(24) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 16, 0, 0) };
            var save = new Button { Content = "APPLY IMPROVEMENT", Classes = { "primary" } };
            var cancel = new Button { Content = "CANCEL" }; cancel.Click += (_, _) => Close();
            buttons.Children.Add(save); buttons.Children.Add(cancel); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("300,*"), ColumnSpacing = 20 };
            var left = new DockPanel();
            var search = new TextBox { PlaceholderText = "Search improvement types…", Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(search, Dock.Top); left.Children.Add(search);
            var types = new ListBox { ItemsSource = definitions, ItemTemplate = new FuncDataTemplate<CustomImprovementDefinition>((item, _) =>
                new TextBlock { Text = item.Name, TextWrapping = Avalonia.Media.TextWrapping.Wrap }) };
            left.Children.Add(types); grid.Children.Add(left);
            search.TextChanged += (_, _) => types.ItemsSource = definitions.Where(x => string.IsNullOrWhiteSpace(search.Text)
                || x.Name.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)).ToList();
            var body = new StackPanel { Spacing = 12 };
            var name = new TextBox { Text = existing?.CustomName ?? "", PlaceholderText = "Improvement name" };
            var group = new TextBox { Text = existing?.CustomGroup ?? "", PlaceholderText = "Group (optional)" };
            body.Children.Add(new TextBlock { Text = "CUSTOM IMPROVEMENT", Classes = { "h2" } });
            body.Children.Add(name); body.Children.Add(group);
            var fields = new StackPanel { Spacing = 10 };
            var help = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "muted" } };
            var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            body.Children.Add(fields); body.Children.Add(help); body.Children.Add(error);
            var scroller = new ScrollViewer { Content = body }; Grid.SetColumn(scroller, 1); grid.Children.Add(scroller);
            root.Children.Add(grid); Content = root;
            var numbers = new Dictionary<string, NumericUpDown>();
            var applyToRating = new CheckBox { Content = "Apply to rating" };
            var free = new CheckBox { Content = "Free" };
            string selection = "";
            types.SelectionChanged += (_, _) =>
            {
                if (types.SelectedItem is not CustomImprovementDefinition definition) return;
                fields.Children.Clear(); numbers.Clear(); help.Text = definition.Help; error.Text = "";
                bool editing = existing?.CustomId == definition.Id;
                selection = editing ? existing.ImprovedName : "";
                applyToRating.IsChecked = editing && existing.AddToRating;
                free.IsChecked = false;
                foreach (string field in definition.Fields)
                {
                    if (field == "applytorating") { fields.Children.Add(applyToRating); continue; }
                    if (field == "free") { fields.Children.Add(free); continue; }
                    if (field.StartsWith("Select", StringComparison.OrdinalIgnoreCase))
                    {
                        var target = new TextBox { Text = selection, IsReadOnly = true, PlaceholderText = "Choose target…" };
                        var choose = new Button { Content = "CHOOSE TARGET" };
                        choose.Click += async (_, _) =>
                        {
                            var options = await Task.Run(() => CustomImprovementRules.SelectionOptions(character, definition));
                            string value = await ChooseOrTypeWindow.ShowAsync("Improvement target", options, definition.Selection == "SelectKnowSkill", selection);
                            if (value != null && definition.Selection == "SelectSkill" && ExoticSkill.IsExoticSkillName(character, value))
                            {
                                var specifics = await Task.Run(() => SkillSelector.GetExoticSpecifics(character, value, new SkillSelectionFilter()));
                                string specific = await ChooseOrTypeWindow.ShowAsync("Exotic skill specific", specifics, true);
                                value = string.IsNullOrWhiteSpace(specific) ? null : value + " (" + specific + ")";
                            }
                            if (value != null) { selection = value; target.Text = options.FirstOrDefault(x => x.Value?.ToString() == value).Name ?? value; }
                        };
                        fields.Children.Add(target); fields.Children.Add(choose); continue;
                    }
                    decimal initial = 0;
                    if (editing)
                        initial = field switch
                        {
                            "val" => existing.ImproveType == Improvement.ImprovementType.Attribute ? existing.Augmented
                                : existing.ImproveType == Improvement.ImprovementType.AdeptPowerFreeLevels ? existing.Rating : existing.Value,
                            "min" => existing.Minimum, "max" => existing.Maximum,
                            "aug" or "percent" => existing.AugmentedMaximum, _ => 0
                        };
                    var number = new NumericUpDown { Minimum = -100, Maximum = 100, Value = initial, FormatString = field == "val" ? "0.##" : "0" };
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,140") };
                    string label = field switch { "val" => "Value", "min" => "Minimum", "max" => "Maximum", "aug" => "Augmented maximum", "percent" => "Percent", _ => field };
                    row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
                    Grid.SetColumn(number, 1); row.Children.Add(number); fields.Children.Add(row); numbers[field] = number;
                }
            };
            save.Click += (_, _) =>
            {
                if (types.SelectedItem is not CustomImprovementDefinition definition) return;
                if (string.IsNullOrWhiteSpace(name.Text)) { error.Text = "Enter an improvement name."; return; }
                if (!string.IsNullOrEmpty(definition.Selection) && string.IsNullOrEmpty(selection)) { error.Text = "Choose a target."; return; }
                decimal Number(string field) => numbers.TryGetValue(field, out var number) ? number.Value ?? 0 : 0;
                Result = new CustomImprovementInput { DefinitionId = definition.Id, Name = name.Text, Group = group.Text, Selection = selection,
                    Value = Number("val"), Minimum = (int)Number("min"), Maximum = (int)Number("max"), AugmentedMaximum = (int)(numbers.ContainsKey("percent") ? Number("percent") : Number("aug")),
                    ApplyToRating = applyToRating.IsChecked == true, Free = free.IsChecked == true };
                Close();
            };
            types.SelectedItem = definitions.FirstOrDefault(x => x.Id == existing?.CustomId) ?? definitions.FirstOrDefault();
        }
    }
}
