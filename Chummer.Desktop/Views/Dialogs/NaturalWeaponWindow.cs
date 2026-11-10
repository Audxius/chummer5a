using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.XPath;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Chummer.Desktop.ViewModels;
using Chummer.Rules;

namespace Chummer.Desktop.Views.Dialogs
{
    public sealed class NaturalWeaponWindow : Window
    {
        private readonly CharacterViewModel _owner;
        private readonly TextBox _name = new TextBox { PlaceholderText = "Natural weapon name" };
        private readonly ComboBox _skill = new ComboBox();
        private readonly ComboBox _damage = new ComboBox();
        private readonly ComboBox _type = new ComboBox { ItemsSource = new[] { "P", "S" }, SelectedIndex = 0 };
        private readonly NumericUpDown _modifier = Number();
        private readonly NumericUpDown _ap = Number();
        private readonly NumericUpDown _reach = Number();
        private readonly Button _add = new Button { Content = "CREATE WEAPON", Classes = { "primary" } };
        private readonly TextBlock _status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        private Task _load;
        public string SelectedSkill => (_skill.SelectedItem as ListItem?)?.Value?.ToString();
        public NaturalWeaponWindow(CharacterViewModel owner)
        {
            _owner = owner; Title = "Create natural weapon"; Width = 580; Height = 650;
            var fields = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
            foreach (var field in new (string, Control)[] { ("NAME", _name), ("COMBAT SKILL", _skill), ("DAMAGE BASE", _damage),
                ("DAMAGE MODIFIER", _modifier), ("DAMAGE TYPE", _type), ("ARMOR PENETRATION", _ap), ("REACH", _reach) })
            {
                fields.Children.Add(new TextBlock { Text = field.Item1, Classes = { "label" } }); fields.Children.Add(field.Item2);
            }
            _skill.ItemTemplate = new FuncDataTemplate<ListItem>((item, _) => new TextBlock { Text = item.Name });
            var bases = new List<string> { "({STR}/2)", "({STR})" }; bases.AddRange(Enumerable.Range(1, 20).Select(x => x.ToString()));
            _damage.ItemsSource = bases; _damage.SelectedIndex = 0;
            fields.Children.Add(_status); fields.Children.Add(_add); Content = new ScrollViewer { Content = fields };
            _add.IsEnabled = false; _add.Click += async (_, _) => await SaveAsync(); Opened += async (_, _) => await LoadAsync();
        }
        private static NumericUpDown Number() => new NumericUpDown { Minimum = -10, Maximum = 10, Value = 0, FormatString = "0" };
        public Task LoadAsync() => _load ??= LoadCoreAsync();
        private async Task LoadCoreAsync()
        {
            try
            {
                var skills = await Task.Run(async () =>
                {
                    var data = await _owner.Character.LoadDataXPathAsync("skills.xml"); var result = new List<ListItem>();
                    foreach (XPathNavigator node in data.Select("/chummer/skills/skill[category='Combat Active']"))
                        result.Add(new ListItem(node.SelectSingleNode("name")?.Value, node.SelectSingleNode("translate")?.Value ?? node.SelectSingleNode("name")?.Value));
                    return result.OrderBy(x => x.Name).ToArray();
                });
                _skill.ItemsSource = skills; _skill.SelectedIndex = Array.FindIndex(skills, x => x.Value?.ToString() == "Unarmed Combat"); _add.IsEnabled = true;
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }
        public async Task SaveAsync(NaturalWeaponDesign design = null)
        {
            design ??= new NaturalWeaponDesign { Name = _name.Text, Skill = SelectedSkill,
                DamageBase = _damage.SelectedItem as string, DamageType = _type.SelectedItem as string,
                DamageModifier = decimal.ToInt32(_modifier.Value ?? 0), ArmorPenetration = decimal.ToInt32(_ap.Value ?? 0), Reach = decimal.ToInt32(_reach.Value ?? 0) };
            _add.IsEnabled = false;
            try
            {
                RuleResult result = RuleResult.NotAllowed;
                await _owner.RunRuleAsync(async t => result = await NaturalWeaponRules.AddAsync(_owner.Character, design, t));
                if (result == RuleResult.Changed) Close(); else _status.Text = "Enter a name and select a combat skill and damage value.";
            }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { _add.IsEnabled = true; }
        }
    }
}
