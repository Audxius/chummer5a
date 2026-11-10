using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Chummer.Desktop.Views.Dialogs;
using Chummer.Rules;

namespace Chummer.Desktop.ViewModels
{
    public sealed partial class CharacterViewModel
    {
        public bool IsImprovements => Section == CharacterSection.Improvements;
        public ObservableCollection<CustomImprovementRow> CustomImprovements { get; } = new ObservableCollection<CustomImprovementRow>();
        [ObservableProperty] private string _improvementFilter = string.Empty;
        private List<CustomImprovementRow> _allCustomImprovements = new List<CustomImprovementRow>();
        partial void OnImprovementFilterChanged(string value) => FilterCustomImprovements();

        private List<CustomImprovementRow> BuildCustomImprovements() => Character.Improvements.Where(x => x.Custom)
            .GroupBy(x => x.SourceName).Select(x => new CustomImprovementRow(this, x.First()))
            .OrderBy(x => x.Group).ThenBy(x => x.Improvement.SortOrder).ThenBy(x => x.Name).ToList();

        private void FilterCustomImprovements() => Reset(CustomImprovements, _allCustomImprovements.Where(x => string.IsNullOrWhiteSpace(ImprovementFilter)
            || (x.Name + " " + x.Group + " " + x.Effect).Contains(ImprovementFilter, System.StringComparison.CurrentCultureIgnoreCase)));

        [RelayCommand]
        private Task AddCustomImprovement() => EditCustomImprovementAsync(null);

        internal async Task EditCustomImprovementAsync(Improvement improvement)
        {
            var definitions = await Task.Run(() => CustomImprovementRules.Definitions(Character));
            var window = new CustomImprovementWindow(Character, definitions, improvement);
            await DialogHost.ShowModalAsync(window, null);
            if (window.Result != null)
                await RunRuleAsync(t => CustomImprovementRules.SaveAsync(Character, window.Result, improvement, t));
        }
    }

    public sealed partial class CustomImprovementRow : ObservableObject
    {
        private readonly CharacterViewModel _owner;
        public CustomImprovementRow(CharacterViewModel owner, Improvement improvement)
        {
            _owner = owner; Improvement = improvement;
            _notesDraft = improvement.Notes.RtfToPlainText();
        }
        public Improvement Improvement { get; }
        public string Name => Improvement.CustomName;
        public string Group => string.IsNullOrEmpty(Improvement.CustomGroup) ? "Ungrouped" : Improvement.CustomGroup;
        public string Effect => Improvement.ImproveType + "  " + Improvement.ImprovedName + "  "
            + (Improvement.ImproveType == Improvement.ImprovementType.Attribute ? Improvement.Augmented : Improvement.Value).ToString("+0.##;-0.##;0", GlobalSettings.CultureInfo);
        public bool IsEnabled => Improvement.Enabled;
        public string ToggleLabel => IsEnabled ? "DISABLE" : "ENABLE";
        [ObservableProperty] private string _notesDraft;

        [RelayCommand]
        private Task Edit() => _owner.EditCustomImprovementAsync(Improvement);
        [RelayCommand]
        private Task Remove() => _owner.RunRuleAsync(t => CustomImprovementRules.RemoveAsync(_owner.Character, Improvement, t));
        [RelayCommand]
        private Task Toggle() => _owner.RunRuleAsync(async t =>
        {
            await CustomImprovementRules.SetEnabledAsync(_owner.Character, Improvement, !Improvement.Enabled, t);
            return RuleResult.Changed;
        });
        [RelayCommand]
        private Task SaveNotes() => _owner.RunRuleAsync(async t =>
        {
            await Improvement.SetNotesAsync(NotesDraft ?? string.Empty, t);
            return RuleResult.Changed;
        });
    }
}
