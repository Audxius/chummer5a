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

        /// <summary>Runs an engine rule off the UI thread and refreshes the sheet when it changed the character.</summary>
        public async Task RunRuleAsync(Func<CancellationToken, Task<RuleResult>> funcRule)
        {
            RuleResult eResult;
            try
            {
                eResult = await Task.Run(() => funcRule(CancellationToken.None)).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (eResult == RuleResult.Changed)
            {
                MarkDirty();
                Reload();
            }
        }

        private static Task<T> OffUi<T>(Func<Task<T>> funcRun) => Task.Run(funcRun);

        // ---------- qualities ----------

        [ObservableProperty] private string _positiveQualitySummary = string.Empty;
        [ObservableProperty] private string _negativeQualitySummary = string.Empty;
        [ObservableProperty] private string _qualityHint = string.Empty;

        private void RefreshQualitySummary(List<QualityViewModel> lstQualities)
        {
            int intPositive = lstQualities.Where(x => x.IsPositive).Sum(x => x.Karma);
            int intNegative = -lstQualities.Where(x => !x.IsPositive).Sum(x => x.Karma);
            int intLimit = Character.Settings.QualityKarmaLimit;
            if (Character.Created)
            {
                PositiveQualitySummary = intPositive.ToString(GlobalSettings.CultureInfo) + " karma";
                NegativeQualitySummary = intNegative.ToString(GlobalSettings.CultureInfo) + " karma";
                QualityHint = "Career mode: new positive qualities cost "
                              + (Character.Settings.DontDoubleQualityPurchases ? "their" : "twice their")
                              + " karma value; buying off a negative quality costs "
                              + (Character.Settings.DontDoubleQualityRefunds ? "its" : "twice its") + " value.";
            }
            else
            {
                PositiveQualitySummary = Character.PositiveQualityLimitKarma.ToString(GlobalSettings.CultureInfo) + " / " + intLimit.ToString(GlobalSettings.CultureInfo) + " karma";
                NegativeQualitySummary = (-Character.NegativeQualityLimitKarma).ToString(GlobalSettings.CultureInfo) + " / " + intLimit.ToString(GlobalSettings.CultureInfo) + " karma";
                QualityHint = "Creation: up to " + intLimit.ToString(GlobalSettings.CultureInfo) + " karma each of positive and negative qualities.";
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
