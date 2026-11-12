/*  This file is part of Chummer5a.
 *
 *  Chummer5a is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  Chummer5a is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with Chummer5a.  If not, see <http://www.gnu.org/licenses/>.
 *
 *  You can obtain the full source code for Chummer5a at
 *  https://github.com/chummer5a/chummer5a
 */

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Chummer.Backend.Enums;
using Chummer.Desktop.Views;
using Chummer.Desktop.Views.Dialogs;
using Chummer.Rules;
using Chummer.Selection;

namespace Chummer.Desktop.ViewModels
{
    /// <summary>Editing of the Qualities and Abilities sections (pickers in the UI, rules in Chummer.Rules).</summary>
    public sealed partial class CharacterViewModel
    {
        // ---------- shared ----------

        private int _pendingEngineMutations;
        internal Func<Task> CommitInfoEditor { get; set; }

        /// <summary>Serialize multi-step dialog reads with sheet snapshots and engine mutations.</summary>
        internal async Task<T> ReadModelAsync<T>(Func<Task<T>> read)
        {
            await _engineOperationGate.WaitAsync().ConfigureAwait(false);
            try { return await Task.Run(read).ConfigureAwait(false); }
            finally { _engineOperationGate.Release(); }
        }

        /// <summary>Commit metadata without replacing the editor's active field snapshots.</summary>
        internal async Task WriteInfoAsync(Action<Character> write)
        {
            await _engineOperationGate.WaitAsync().ConfigureAwait(false);
            try { await Task.Run(() => write(Character)).ConfigureAwait(false); }
            finally { _engineOperationGate.Release(); }
        }

        /// <summary>Runs an engine rule off the UI thread and refreshes the sheet when it changed the character.</summary>
        public async Task RunRuleAsync(Func<CancellationToken, Task<RuleResult>> funcRule)
        {
            RuleResult eResult;
            Interlocked.Increment(ref _pendingEngineMutations);
            IsMutatingModel = true;
            await _engineOperationGate.WaitAsync().ConfigureAwait(true);
            try
            {
                eResult = await Task.Run(() => funcRule(CancellationToken.None)).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally { _engineOperationGate.Release(); IsMutatingModel = Interlocked.Decrement(ref _pendingEngineMutations) > 0; }
            if (eResult == RuleResult.Changed)
            {
                MarkDirty();
                Reload();
            }
        }

        private static Task<T> OffUi<T>(Func<Task<T>> funcRun) => Task.Run(funcRun);

        [RelayCommand]
        private Task ManageSustainedEffects() => DialogHost.ShowModalAsync(new SustainedEffectsWindow(this), null);

        [RelayCommand]
        private async Task CreateCustomSpell()
        {
            var picker = new CustomSpellWindow();
            await DialogHost.ShowModalAsync(picker, null);
            if (picker.SelectedDesign == null) return;
            RuleResult result = RuleResult.Cancelled;
            await RunRuleAsync(async t => result = await CustomSpellRules.AddAsync(Character, picker.SelectedDesign, t));
            if (result == RuleResult.NotAllowed)
                await MessageBoxes.ShowAsync("The creation spell limit is reached. Raise Spellcasting or Ritual Spellcasting before adding another spell.", "Create spell");
        }

        internal async Task ChooseTraditionAsync()
        {
            var options = await Task.Run(() => TraditionRules.Options(Character));
            var window = new TraditionWindow(Character, options);
            await DialogHost.ShowModalAsync(window, null);
            if (window.Selected != null)
                await RunRuleAsync(t => TraditionRules.SetAsync(Character, window.Selected.Id, window.CustomName, window.CustomDrain, window.CustomSpirits, t));
        }

        internal async Task AllocateMysticPowerPointsAsync()
        {
            decimal? value = await PromptWindow.AskNumberAsync(null, "Mystic adept power points (creation)", 0, Character.MAG.TotalValue, true);
            if (value == null) return;
            await RunRuleAsync(async t =>
            {
                if (Character.Created || !Character.UseMysticAdeptPPs) return RuleResult.NotAllowed;
                await Character.SetMysticAdeptPowerPointsAsync((int)value.Value, t);
                return RuleResult.Changed;
            });
        }

        // ---------- qualities ----------

        [ObservableProperty] private string _positiveQualitySummary = string.Empty;
        [ObservableProperty] private string _negativeQualitySummary = string.Empty;
        [ObservableProperty] private string _qualityHint = string.Empty;

        private (string Positive, string Negative, string Hint) ReadQualitySummary(List<QualityViewModel> lstQualities)
        {
            int intPositive = lstQualities.Where(x => x.IsPositive).Sum(x => x.Karma);
            int intNegative = -lstQualities.Where(x => x.Type == QualityType.Negative).Sum(x => x.Karma);
            int intLimit = Character.Settings.QualityKarmaLimit;
            if (Character.Created)
            {
                string hint = "Career mode: new positive qualities cost "
                              + (Character.Settings.DontDoubleQualityPurchases ? "their" : "twice their")
                              + " karma value; buying off a negative quality costs "
                              + (Character.Settings.DontDoubleQualityRefunds ? "its" : "twice its") + " value.";
                return (intPositive.ToString(GlobalSettings.CultureInfo) + " karma", intNegative.ToString(GlobalSettings.CultureInfo) + " karma", hint);
            }
            else
            {
                return (Character.PositiveQualityLimitKarma.ToString(GlobalSettings.CultureInfo) + " / " + intLimit.ToString(GlobalSettings.CultureInfo) + " karma",
                    (-Character.NegativeQualityLimitKarma).ToString(GlobalSettings.CultureInfo) + " / " + intLimit.ToString(GlobalSettings.CultureInfo) + " karma",
                    "Creation: up to " + intLimit.ToString(GlobalSettings.CultureInfo) + " karma each of positive and negative qualities.");
            }
        }

        /// <summary>Opens the quality picker (repeatedly with "add &amp; pick another") and adds the choice like legacy cmdAddQuality_Click.</summary>
        [RelayCommand]
        private async Task AddQuality()
        {
            while (true)
            {
                QualityPick objPick = await QualityPickerWindow.ShowAsync(null, Character).ConfigureAwait(true);
                if (objPick == null)
                    return;
                await RunRuleAsync(t => QualityRules.AddAsync(Character, objPick.Id, objPick.Rating, objPick.Free, t)).ConfigureAwait(true);
                if (!objPick.AddAgain)
                    return;
            }
        }

        // ---------- abilities ----------

        public ObservableCollection<AbilityGroupViewModel> AbilitySections { get; } = new ObservableCollection<AbilityGroupViewModel>();
        private List<AbilityGroupViewModel> _lstAllAbilitySections = new List<AbilityGroupViewModel>();

        private void ApplyAbilityFilter()
        {
            string[] astrWords = (AbilityFilter ?? string.Empty).ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            AbilitySections.Clear();
            foreach (AbilityGroupViewModel objGroup in _lstAllAbilitySections)
            {
                AbilityGroupViewModel objShown = objGroup.Filtered(astrWords);
                if (objShown != null)
                    AbilitySections.Add(objShown);
            }
            FilterCards(_lstAllAbilities, AbilityGroups, AbilityFilter);
        }

        internal async Task LearnSpellAsync()
        {
            while (true)
            {
                (bool blnCanLearn, bool blnFreeOnly) = await OffUi(() => MagicRules.CheckCanLearnSpellAsync(Character)).ConfigureAwait(true);
                if (!blnCanLearn)
                    return;
                SpellPick objPick = await SpellPickerWindow.ShowAsync(null, Character, blnFreeOnly).ConfigureAwait(true);
                if (objPick == null)
                    return;
                await RunRuleAsync(t => MagicRules.LearnSpellAsync(Character, objPick.Id, objPick.Limited, objPick.Extended, objPick.Alchemical, objPick.Free, t)).ConfigureAwait(true);
                if (!objPick.AddAgain)
                    return;
            }
        }

        internal async Task AddPowerAsync()
        {
            string strId = await DialogHost.BrowseAsync(null, DataBrowser.Power(Character, false, false, string.Empty, 0, 0)).ConfigureAwait(true);
            if (!string.IsNullOrEmpty(strId))
                await RunRuleAsync(t => MagicRules.AddPowerAsync(Character, strId, t)).ConfigureAwait(true);
        }

        internal async Task AddCritterPowerAsync()
        {
            var choices = await Task.Run(() => MagicRules.CritterPowerOptions(Character));
            var selected = await PromptWindow.BrowseAsync(null, "Add critter power", _ => choices.Select(x => new ListItem(x.Id, x.Name)).ToList(),
                item => choices.First(x => x.Id == item.Value?.ToString()).Details);
            if (!selected.HasValue) return;
            var choice = choices.First(x => x.Id == selected.Value.Value?.ToString());
            int rating = 0;
            if (choice.HasRating)
            {
                decimal? value = await PromptWindow.AskNumberAsync(null, choice.Name + " · rating", 1, 100, true);
                if (!value.HasValue) return;
                rating = (int)value.Value;
            }
            await RunRuleAsync(t => MagicRules.AddCritterPowerAsync(Character, choice.Id, rating, t));
        }

        internal async Task AddComplexFormAsync()
        {
            if (!await OffUi(() => MagicRules.CheckCanAddComplexFormAsync(Character)).ConfigureAwait(true))
                return;
            string strId = await DialogHost.BrowseAsync(null, DataBrowser.ComplexForm(Character)).ConfigureAwait(true);
            if (!string.IsNullOrEmpty(strId))
                await RunRuleAsync(t => MagicRules.AddComplexFormAsync(Character, strId, t)).ConfigureAwait(true);
        }

        internal async Task AddAIProgramAsync()
        {
            (bool blnCanAdd, bool blnAdvanced) = await OffUi(() => MagicRules.CheckCanAddAIProgramAsync(Character)).ConfigureAwait(true);
            if (!blnCanAdd)
                return;
            string strId = await DialogHost.BrowseAsync(null, DataBrowser.AIProgram(Character, blnAdvanced, false)).ConfigureAwait(true);
            if (!string.IsNullOrEmpty(strId))
                await RunRuleAsync(t => MagicRules.AddAIProgramAsync(Character, strId, t)).ConfigureAwait(true);
        }

        internal async Task InitiateAsync()
        {
            InitiationPick objPick = await InitiationWindow.ShowAsync(null, Character).ConfigureAwait(true);
            if (objPick == null)
                return;
            int intGradesBefore = Character.InitiationGrades.Count;
            await RunRuleAsync(t => MagicRules.InitiateAsync(Character, objPick.Group, objPick.Ordeal, objPick.Schooling, t)).ConfigureAwait(true);
            if (!objPick.ChooseMetamagic || Character.InitiationGrades.Count <= intGradesBefore)
                return;
            InitiationGrade objGrade = Character.InitiationGrades.ToList().OrderByDescending(x => x.Grade).FirstOrDefault();
            if (objGrade != null)
                await AddMetamagicAsync(objGrade).ConfigureAwait(true);
        }

        internal async Task AddMetamagicAsync(InitiationGrade objGrade)
        {
            (bool blnCanAdd, bool blnPay) = await OffUi(() => MagicRules.CheckCanAddMetamagicAsync(Character, objGrade)).ConfigureAwait(true);
            if (!blnCanAdd)
                return;
            string strId = await DialogHost.BrowseAsync(null, DataBrowser.Metamagic(Character, objGrade)).ConfigureAwait(true);
            if (!string.IsNullOrEmpty(strId))
                await RunRuleAsync(t => MagicRules.AddMetamagicAsync(Character, objGrade, strId, blnPay, t)).ConfigureAwait(true);
        }

        internal Task AddMartialArtAsync()
            => RunRuleAsync(async t => await MartialArt.Purchase(Character, t).ConfigureAwait(false) ? RuleResult.Changed : RuleResult.Cancelled);

        internal async Task AddTechniqueAsync(MartialArt objMartialArt)
        {
            string strId = await DialogHost.BrowseAsync(null, MagicSelectors.Techniques(Character, objMartialArt)).ConfigureAwait(true);
            if (!string.IsNullOrEmpty(strId))
                await RunRuleAsync(t => MagicRules.AddTechniqueAsync(Character, objMartialArt, strId, t)).ConfigureAwait(true);
        }

        internal async Task AddSpiritAsync(SpiritType eType)
        {
            List<ListItem> lstTypes = MagicSelectors.SpiritTypes(Character, eType);
            string strType = string.Empty;
            if (lstTypes.Count > 0)
            {
                ListItem? objChoice = await PromptWindow.ChooseAsync(null, eType == SpiritType.Sprite ? "Compile which sprite?" : "Summon which spirit?", lstTypes, null).ConfigureAwait(true);
                if (objChoice == null)
                    return;
                strType = objChoice.Value.Value?.ToString() ?? string.Empty;
            }
            await RunRuleAsync(t => MagicRules.AddSpiritAsync(Character, eType, strType, t)).ConfigureAwait(true);
        }
    }
}
