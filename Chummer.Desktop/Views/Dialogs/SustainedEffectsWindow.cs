using System;
using System.Collections.Generic;
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
    public sealed class SustainedEffectsWindow : Window
    {
        private sealed class Choice { public IHasInternalId Source { get; set; } public string Name { get; set; } }
        private sealed class Entry
        {
            public SustainedObject Model { get; set; }
            public string Name { get; set; }
            public int Force { get; set; }
            public int Hits { get; set; }
            public bool Self { get; set; }
        }
        private readonly CharacterViewModel _owner;
        private readonly ListBox _list = new ListBox();
        private readonly ComboBox _source = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly NumericUpDown _force = Number();
        private readonly NumericUpDown _hits = Number();
        private readonly CheckBox _self = new CheckBox { Content = "Self sustained (apply sustaining penalty)", IsChecked = true };
        private readonly CheckBox _psyche = new CheckBox { Content = "Psyche active" };
        private readonly TextBlock _penalty = new TextBlock { FontWeight = Avalonia.Media.FontWeight.Bold };
        private readonly TextBlock _status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        private readonly Button _remove = new Button { Content = "STOP SUSTAINING", IsEnabled = false };
        private readonly Grid _form = new Grid { Margin = new Thickness(24), ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 24 };
        private Entry _selected;
        private bool _changing;
        private Task _load;

        public SustainedEffectsWindow(CharacterViewModel owner)
        {
            _owner = owner; Title = "Sustained effects"; Width = 960; Height = 650;
            var left = new DockPanel(); var add = new Button { Content = "NEW EFFECT", Margin = new Thickness(0, 12, 0, 0) };
            DockPanel.SetDock(add, Dock.Bottom); left.Children.Add(add); left.Children.Add(_list); _form.Children.Add(left);
            var editor = new StackPanel { Spacing = 14 }; Grid.SetColumn(editor, 1); _form.Children.Add(editor);
            void Field(string label, Control control) { editor.Children.Add(new TextBlock { Text = label, Classes = { "label" } }); editor.Children.Add(control); }
            Field("SPELL / COMPLEX FORM / CRITTER POWER", _source); Field("FORCE / LEVEL", _force); Field("NET HITS", _hits);
            editor.Children.Add(_self); editor.Children.Add(_psyche); editor.Children.Add(_penalty);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var save = new Button { Content = "SAVE EFFECT", Classes = { "primary" } }; actions.Children.Add(save); actions.Children.Add(_remove); editor.Children.Add(actions);
            editor.Children.Add(_status); var close = new Button { Content = "CLOSE" }; close.Click += (_, _) => Close(); editor.Children.Add(close); Content = _form;
            _source.ItemTemplate = new FuncDataTemplate<Choice>((item, _) => new TextBlock { Text = item?.Name ?? "", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            _list.ItemTemplate = new FuncDataTemplate<Entry>((item, _) => new TextBlock { Text = item == null ? "" : item.Name + "\nForce/level " + item.Force + " · " + item.Hits + " net hits · "
                + (item.Model.LinkedObjectType == Improvement.ImprovementSource.CritterPower ? "No sustaining penalty" : item.Self ? "Self sustained" : "Externally sustained"), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            _list.SelectionChanged += (_, _) => { if (!_changing) Select(_list.SelectedItem as Entry); };
            _source.SelectionChanged += (_, _) => _self.IsVisible = !((_source.SelectedItem as Choice)?.Source is CritterPower);
            add.Click += (_, _) => { _list.SelectedItem = null; Select(null); };
            save.Click += async (_, _) => await SaveAsync(); _remove.Click += async (_, _) => await RemoveAsync();
            _psyche.IsCheckedChanged += async (_, _) =>
            {
                if (_changing) return; bool active = _psyche.IsChecked == true; _form.IsEnabled = false;
                try { await _owner.RunRuleAsync(t => SustainingRules.SetPsycheAsync(_owner.Character, active, t)); await ReloadAsync(); }
                catch (Exception ex) { _status.Text = ex.Message; }
                finally { _form.IsEnabled = true; }
            };
            Opened += async (_, _) => await LoadAsync();
        }
        private static NumericUpDown Number() => new NumericUpDown { Minimum = 0, Maximum = 100, Value = 0, FormatString = "0" };
        public Task LoadAsync() => _load ??= ReloadAsync();
        public async Task ReloadAsync(string desiredId = null)
        {
            string selectedId = desiredId ?? _selected?.Model.InternalId;
            var snapshot = await Task.Run(async () =>
            {
                var choices = new List<Choice>(); var entries = new List<Entry>(); var character = _owner.Character;
                foreach (var spell in await character.Spells.ToListAsync()) choices.Add(new Choice { Source = spell, Name = await spell.GetCurrentDisplayNameAsync() + " · Spell" });
                foreach (var form in await character.ComplexForms.ToListAsync()) choices.Add(new Choice { Source = form, Name = await form.GetCurrentDisplayNameAsync() + " · Complex form" });
                foreach (var power in await character.CritterPowers.ToListAsync()) choices.Add(new Choice { Source = power, Name = await power.GetCurrentDisplayNameAsync() + " · Critter power" });
                foreach (var model in await character.SustainedCollection.ToListAsync()) entries.Add(new Entry { Model = model, Name = await model.DisplayNameShortAsync(GlobalSettings.Language),
                    Force = await model.GetForceAsync(), Hits = await model.GetNetHitsAsync(), Self = await model.GetSelfSustainedAsync() });
                return (Choices: choices.OrderBy(x => x.Name).ToArray(), Entries: entries.ToArray(), Psyche: await character.GetPsycheActiveAsync(), Penalty: await character.GetSustainingPenaltyAsync());
            });
            _changing = true; _source.ItemsSource = snapshot.Choices; _source.SelectedIndex = snapshot.Choices.Length == 0 ? -1 : 0;
            _list.ItemsSource = snapshot.Entries; _psyche.IsChecked = snapshot.Psyche; _penalty.Text = "Sustaining dice-pool modifier: " + snapshot.Penalty;
            var selected = snapshot.Entries.FirstOrDefault(x => x.Model.InternalId == selectedId); _list.SelectedItem = selected; _changing = false; Select(selected);
            if (snapshot.Choices.Length == 0) _status.Text = "Add a spell, complex form or critter power to track an effect.";
        }
        private void Select(Entry entry)
        {
            _selected = entry; _source.IsEnabled = entry == null; _remove.IsEnabled = entry != null;
            _force.Value = entry?.Force ?? 0; _hits.Value = entry?.Hits ?? 0; _self.IsChecked = entry?.Self ?? true;
            if (entry != null) _source.SelectedItem = _source.ItemsSource.OfType<Choice>().FirstOrDefault(x => ReferenceEquals(x.Source, entry.Model.LinkedObject));
            _status.Text = "";
        }
        public async Task SaveAsync(IHasInternalId source = null, int? force = null, int? hits = null, bool? selfSustained = null)
        {
            await LoadAsync();
            if (source != null) { Select(null); _source.SelectedItem = _source.ItemsSource.OfType<Choice>().FirstOrDefault(x => ReferenceEquals(x.Source, source)); }
            if (force.HasValue) _force.Value = force; if (hits.HasValue) _hits.Value = hits; if (selfSustained.HasValue) _self.IsChecked = selfSustained;
            var selected = _selected?.Model; var ability = (_source.SelectedItem as Choice)?.Source;
            int level = decimal.ToInt32(_force.Value ?? 0), successes = decimal.ToInt32(_hits.Value ?? 0); bool self = _self.IsChecked == true;
            _form.IsEnabled = false;
            try
            {
                RuleResult result = RuleResult.NotAllowed;
                string savedId = selected?.InternalId;
                await _owner.RunRuleAsync(async t =>
                {
                    result = selected == null ? await SustainingRules.AddAsync(_owner.Character, ability, level, successes, self, t)
                        : await SustainingRules.UpdateAsync(_owner.Character, selected, level, successes, self, t);
                    if (result == RuleResult.Changed && selected == null)
                        savedId = (await _owner.Character.SustainedCollection.ToListAsync(t)).LastOrDefault(x => ReferenceEquals(x.LinkedObject, ability))?.InternalId;
                    return result;
                });
                await ReloadAsync(savedId); _status.Text = result == RuleResult.Changed ? "Sustained effect saved." : "Select an owned ability and valid force/level and net hits.";
            }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { _form.IsEnabled = true; }
        }
        private async Task RemoveAsync()
        {
            var model = _selected?.Model; if (model == null) return; _form.IsEnabled = false;
            try { await _owner.RunRuleAsync(t => SustainingRules.RemoveAsync(_owner.Character, model, t)); _selected = null; await ReloadAsync(); }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { _form.IsEnabled = true; }
        }
    }
}
