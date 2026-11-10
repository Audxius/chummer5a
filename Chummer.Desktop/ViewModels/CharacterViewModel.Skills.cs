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
using Chummer.Backend.Skills;
using Chummer.Desktop.Localization;
using Chummer.Desktop.Views.Dialogs;
using Chummer.Rolls;
using Chummer.Rules;

namespace Chummer.Desktop.ViewModels
{
    public enum SkillSortMode
    {
        DicePool,
        Name,
        Rating,
        Category
    }

    /// <summary>
    /// Skills section: one aligned table of active, knowledge and language skills (sortable, optionally grouped by category),
    /// skill groups and point budgets beside it, and every edit the legacy skills tab offers (via <see cref="SkillRules"/>).
    /// </summary>
    public sealed partial class CharacterViewModel
    {
        [ObservableProperty] private SkillSortMode _skillSort = SkillSortMode.DicePool;
        [ObservableProperty] private bool _skillSortDescending = true;
        [ObservableProperty] private bool _groupSkillsByCategory;
        [ObservableProperty] private bool _isSkillsCareer;
        [ObservableProperty] private string _skillCountText = string.Empty;
        [ObservableProperty] private IReadOnlyList<ItemStat> _skillBudget = Array.Empty<ItemStat>();

        /// <summary>Rows of the skills table: <see cref="SkillHeaderRow"/>s (grouped mode) and <see cref="SkillViewModel"/>s.</summary>
        public ObservableCollection<object> SkillRows { get; } = new ObservableCollection<object>();

        public ObservableCollection<SkillGroupViewModel> SkillGroups { get; } = new ObservableCollection<SkillGroupViewModel>();

        private List<SkillViewModel> _lstAllSkills = new List<SkillViewModel>();
        private List<SkillGroupViewModel> _lstAllSkillGroups = new List<SkillGroupViewModel>();

        /// <summary>Every skill of the runner (unfiltered).</summary>
        public IReadOnlyList<SkillViewModel> AllSkills => _lstAllSkills;

        public bool HasSkillGroups => SkillGroups.Count > 0;
        public bool HasSkillBudget => SkillBudget.Count > 0;
        public bool IsSkillsCreate => !IsSkillsCareer;
        public bool SortIsPool => SkillSort == SkillSortMode.DicePool;
        public bool SortIsName => SkillSort == SkillSortMode.Name;
        public bool SortIsRating => SkillSort == SkillSortMode.Rating;
        public bool SortIsCategory => SkillSort == SkillSortMode.Category;
        public string SortArrow => SkillSortDescending ? "▼" : "▲";
        public string PoolSortLabel => "DICE POOL" + (SortIsPool ? "  " + SortArrow : string.Empty);
        public string NameSortLabel => "NAME" + (SortIsName ? "  " + SortArrow : string.Empty);
        public string RatingSortLabel => "RATING" + (SortIsRating ? "  " + SortArrow : string.Empty);
        public string CategorySortLabel => "CATEGORY" + (SortIsCategory ? "  " + SortArrow : string.Empty);

        partial void OnSkillSortChanged(SkillSortMode value) => OnSkillViewChanged();

        partial void OnSkillSortDescendingChanged(bool value) => OnSkillViewChanged();

        partial void OnGroupSkillsByCategoryChanged(bool value) => ApplySkillFilter();

        partial void OnIsSkillsCareerChanged(bool value) => OnPropertyChanged(nameof(IsSkillsCreate));

        partial void OnSkillBudgetChanged(IReadOnlyList<ItemStat> value) => OnPropertyChanged(nameof(HasSkillBudget));

        private void OnSkillViewChanged()
        {
            OnPropertyChanged(nameof(SortIsPool));
            OnPropertyChanged(nameof(SortIsName));
            OnPropertyChanged(nameof(SortIsRating));
            OnPropertyChanged(nameof(SortIsCategory));
            OnPropertyChanged(nameof(SortArrow));
            OnPropertyChanged(nameof(PoolSortLabel));
            OnPropertyChanged(nameof(NameSortLabel));
            OnPropertyChanged(nameof(RatingSortLabel));
            OnPropertyChanged(nameof(CategorySortLabel));
            ApplySkillFilter();
        }

        /// <summary>Clicking the active sort flips its direction; a new sort starts in its natural direction (big numbers first, A→Z).</summary>
        [RelayCommand]
        private void SortSkills(SkillSortMode eMode)
        {
            if (SkillSort == eMode)
            {
                SkillSortDescending = !SkillSortDescending;
                return;
            }
            _blnSuppressSkillRefresh = true;
            SkillSortDescending = eMode == SkillSortMode.DicePool || eMode == SkillSortMode.Rating;
            _blnSuppressSkillRefresh = false;
            SkillSort = eMode;
        }

        private bool _blnSuppressSkillRefresh;

        // ---------- snapshot ----------

        internal sealed class SkillsSnapshot
        {
            public bool Career;
            public List<SkillViewModel> Skills;
            public List<SkillGroupViewModel> Groups;
            public List<ItemStat> Budget;
        }

        /// <summary>Reads every skill from the engine (background thread, like the rest of LoadSnapshot).</summary>
        private SkillsSnapshot BuildSkillsSnapshot()
        {
            bool blnCareer = Character.Created;
            SkillsSection objSection = Character.SkillsSection;
            List<SkillViewModel> lstSkills = objSection.Skills.ToList().Select(x => new SkillViewModel(this, x, blnCareer)).ToList();
            lstSkills.AddRange(objSection.KnowledgeSkills.ToList().Select(x => new SkillViewModel(this, x, blnCareer)));
            List<SkillGroupViewModel> lstGroups = objSection.SkillGroups.ToList().Where(x => !x.IsDisabled)
                                                            .Select(x => new SkillGroupViewModel(this, x, blnCareer)).ToList();
            List<ItemStat> lstBudget = new List<ItemStat>();
            if (!blnCareer)
            {
                System.Globalization.CultureInfo objCulture = GlobalSettings.CultureInfo;
                if (Character.EffectiveBuildMethodUsesPriorityTables)
                {
                    lstBudget.Add(new ItemStat("SKILL POINTS", objSection.SkillPoints.ToString(objCulture) + " / " + objSection.SkillPointsMaximum.ToString(objCulture)));
                    lstBudget.Add(new ItemStat("GROUP POINTS", objSection.SkillGroupPoints.ToString(objCulture) + " / " + objSection.SkillGroupPointsMaximum.ToString(objCulture)));
                }
                if (objSection.HasKnowledgePoints)
                    lstBudget.Add(new ItemStat("KNOWLEDGE POINTS", objSection.KnowledgeSkillPointsRemain.ToString(objCulture) + " / " + objSection.KnowledgeSkillPoints.ToString(objCulture)));
                lstBudget.Add(new ItemStat("KARMA LEFT", _creationKarma.ToString(objCulture)));
            }
            return new SkillsSnapshot { Career = blnCareer, Skills = lstSkills, Groups = lstGroups, Budget = lstBudget };
        }

        /// <summary>UI thread: take a snapshot built by <see cref="BuildSkillsSnapshot"/>.</summary>
        private void ApplySkillsSnapshot(SkillsSnapshot objSnapshot)
        {
            IsSkillsCareer = objSnapshot.Career;
            _lstAllSkills = objSnapshot.Skills;
            _lstAllSkillGroups = objSnapshot.Groups;
            SkillBudget = objSnapshot.Budget;
            ApplySkillFilter();
        }

        private void ApplySkillFilter()
        {
            if (_blnSuppressSkillRefresh)
                return;
            string strFilter = SkillFilter?.Trim() ?? string.Empty;
            bool blnShowAll = ShowUntrainedSkills;
            List<SkillViewModel> lstShown = _lstAllSkills.Where(x =>
                strFilter.Length > 0
                    ? x.SearchText.IndexOf(strFilter, StringComparison.CurrentCultureIgnoreCase) >= 0
                    : blnShowAll || x.Rating > 0 || x.IsNative || x.IsKnowledge && x.IsNew).ToList();
            IEnumerable<SkillViewModel> lstSorted = SortSkillRows(lstShown);
            List<object> lstRows = new List<object>(lstShown.Count + 12);
            if (GroupSkillsByCategory)
            {
                foreach (IGrouping<int, SkillViewModel> objGroup in lstSorted.GroupBy(x => x.CategorySort).OrderBy(x => x.Key))
                {
                    List<SkillViewModel> lstMembers = objGroup.ToList();
                    lstRows.Add(new SkillHeaderRow(lstMembers[0].CategoryName, lstMembers[0].IconKey, lstMembers.Count));
                    lstRows.AddRange(lstMembers);
                }
            }
            else
                lstRows.AddRange(lstSorted);
            Reset(SkillRows, lstRows);
            Reset(SkillGroups, _lstAllSkillGroups.Where(x => blnShowAll || x.Rating > 0 || !IsSkillsCareer)
                                                 .Where(x => strFilter.Length == 0 || x.SearchText.IndexOf(strFilter, StringComparison.CurrentCultureIgnoreCase) >= 0)
                                                 .OrderByDescending(x => x.Rating).ThenBy(x => x.Name, StringComparer.CurrentCulture));
            OnPropertyChanged(nameof(HasSkillGroups));
            SkillCountText = lstShown.Count.ToString(GlobalSettings.CultureInfo) + " / " + _lstAllSkills.Count.ToString(GlobalSettings.CultureInfo) + " SKILLS";
        }

        private IEnumerable<SkillViewModel> SortSkillRows(IEnumerable<SkillViewModel> lstSkills)
        {
            StringComparer objNames = StringComparer.CurrentCulture;
            bool blnDesc = SkillSortDescending;
            switch (SkillSort)
            {
                case SkillSortMode.Name:
                    return blnDesc ? lstSkills.OrderByDescending(x => x.Name, objNames) : lstSkills.OrderBy(x => x.Name, objNames);
                case SkillSortMode.Rating:
                    return (blnDesc ? lstSkills.OrderByDescending(x => x.SortRating) : lstSkills.OrderBy(x => x.SortRating))
                           .ThenByDescending(x => x.Pool).ThenBy(x => x.Name, objNames);
                case SkillSortMode.Category:
                    return (blnDesc ? lstSkills.OrderByDescending(x => x.CategorySort) : lstSkills.OrderBy(x => x.CategorySort))
                           .ThenByDescending(x => x.Pool).ThenBy(x => x.Name, objNames);
                default:
                    return (blnDesc ? lstSkills.OrderByDescending(x => x.SortPool) : lstSkills.OrderBy(x => x.SortPool))
                           .ThenByDescending(x => x.SortRating).ThenBy(x => x.Name, objNames);
            }
        }

        // ---------- edits ----------

        /// <summary>Runs a rule off the UI thread and re-reads the sheet when it changed something.</summary>
        public Task RunSkillRuleAsync(Func<CancellationToken, Task<RuleResult>> funcRule) => RunRuleAsync(funcRule);

        [RelayCommand]
        private Task AddKnowledgeSkill() => RunSkillRuleAsync(async t =>
        {
            if (await Character.GetCreatedAsync(t).ConfigureAwait(false))
                return await SkillRules.AddKnowledgeSkillCareerAsync(Character,
                    (x, _) => ChooseOrTypeWindow.ShowAsync(Tr.Get("Label_Options_NewKnowledgeSkill"), x, true, null,
                                                           "Pick a knowledge skill or language, or type your own. It is added at rating 0; raise it with karma afterwards."),
                    (x, _) => ChooseOrTypeWindow.ShowAsync("Knowledge skill type", x, false), t).ConfigureAwait(false);
            // Create mode: legacy adds an empty row to fill in; we ask for the name and type up front.
            string strName = await ChooseOrTypeWindow.ShowAsync(Tr.Get("Label_Options_NewKnowledgeSkill"),
                await (await Character.GetSkillsSectionAsync(t).ConfigureAwait(false)).GetMyDefaultKnowledgeSkillsAsync(t).ConfigureAwait(false), true).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(strName))
                return RuleResult.Cancelled;
            KnowledgeSkill objNew = await SkillRules.AddKnowledgeSkillCreateAsync(Character, strName, null, t).ConfigureAwait(false);
            if (objNew != null && string.IsNullOrEmpty(await objNew.GetTypeAsync(t).ConfigureAwait(false)))
            {
                string strType = await ChooseOrTypeWindow.ShowAsync("Knowledge skill type",
                    await (await Character.GetSkillsSectionAsync(t).ConfigureAwait(false)).GetMyKnowledgeTypesAsync(t).ConfigureAwait(false), false).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(strType))
                    await SkillRules.SetKnowledgeTypeAsync(objNew, strType, t).ConfigureAwait(false);
            }
            return objNew != null ? RuleResult.Changed : RuleResult.NotAllowed;
        });

        [RelayCommand]
        private Task AddExoticSkill() => RunSkillRuleAsync(async t =>
        {
            string strSkill = await ChooseOrTypeWindow.ShowAsync(Tr.Get("Title_SelectExoticSkill"), SkillRules.GetExoticSkillOptions(Character), false).ConfigureAwait(false);
            if (string.IsNullOrEmpty(strSkill))
                return RuleResult.Cancelled;
            string strSpecific = await ChooseOrTypeWindow.ShowAsync(strSkill, SkillRules.GetExoticSpecificOptions(Character, strSkill), true, null,
                                                                    "Which weapon or vehicle? Pick one or type your own.").ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(strSpecific))
                return RuleResult.Cancelled;
            return await SkillRules.AddExoticSkillAsync(Character, strSkill, strSpecific, t).ConfigureAwait(false);
        });
    }

    /// <summary>Category heading inside the skills table (grouped mode).</summary>
    public sealed class SkillHeaderRow
    {
        public SkillHeaderRow(string strName, string strIconKey, int intCount)
        {
            Name = (strName ?? string.Empty).ToUpper(GlobalSettings.CultureInfo);
            IconKey = strIconKey;
            CountText = intCount.ToString(GlobalSettings.CultureInfo);
        }

        public string Name { get; }
        public string IconKey { get; }
        public string CountText { get; }
    }

    /// <summary>One specialization on a skill row; clicking it rolls the skill with that specialization.</summary>
    public sealed class SkillSpecChip
    {
        public SkillSpecChip(string strName, string strRollName, int intPool, int intBonus, bool blnFree)
        {
            Name = strName;
            RollName = strRollName;
            Pool = intPool;
            Bonus = intBonus;
            Tip = intPool > 0
                ? "Roll " + intPool.ToString(GlobalSettings.CultureInfo) + " dice with " + strName + " (+" + intBonus.ToString(GlobalSettings.CultureInfo) + ")"
                  + (blnFree ? "  //  granted for free" : string.Empty)
                : strName;
        }

        public string Name { get; }
        public string RollName { get; }
        public int Pool { get; }
        public int Bonus { get; }
        public string Tip { get; }
    }

    public sealed class SkillViewModel
    {
        private readonly CharacterViewModel _objOwner;
        private readonly Skill _objSkill;

        public SkillViewModel(CharacterViewModel objOwner, Skill objSkill, bool blnCareer)
        {
            _objOwner = objOwner;
            _objSkill = objSkill;
            CultureInfo = GlobalSettings.CultureInfo;
            IsCareer = blnCareer;
            KnowledgeSkill objKnowledge = objSkill as KnowledgeSkill;
            IsKnowledge = objKnowledge != null;
            IsLanguage = objSkill.IsLanguage;
            IsNative = objSkill.IsNativeLanguage;
            IsExotic = objSkill.IsExoticSkill;
            Name = objSkill.CurrentDisplayName;
            if (string.IsNullOrEmpty(Name) && IsKnowledge)
            {
                Name = "(unnamed)";
                IsNew = true;
            }
            Rating = objSkill.Rating;
            int intPool = objSkill.Pool;
            Pool = intPool;
            Attribute = objSkill.DisplayAttribute;
            PoolTip = objSkill.PoolToolTip;
            RollName = RollCatalog.SkillRollName(objSkill.CurrentDisplayName);
            IsRollable = intPool > 0 && !IsNative;

            string strCategory = IsLanguage ? "Language" : IsKnowledge ? "Knowledge" : objSkill.SkillCategory;
            (IconKey, CategorySort) = MapCategory(strCategory);
            CategoryName = IsLanguage
                ? "Languages"
                : IsKnowledge
                    ? Tr.Get("Label_KnowledgeSkills")
                    : objSkill.DisplayCategory(GlobalSettings.Language);
            if (IsKnowledge)
            {
                string strType = objKnowledge.Type;
                TypeText = string.IsNullOrEmpty(strType)
                    ? "NO TYPE"
                    : (objSkill.CharacterObject.SkillsSection.MyKnowledgeTypes.FirstOrDefault(x => x.Value?.ToString() == strType).Name ?? strType)
                    .ToUpper(GlobalSettings.CultureInfo);
                CanRename = objKnowledge.AllowNameChange;
                CanChangeType = objKnowledge.AllowTypeChange;
            }

            // Specializations: every one rolls on its own (the top one also has its own column).
            bool blnSpecsCount = !IsExotic && objSkill.CanHaveSpecs && objSkill.TotalBaseRating > 0;
            List<SkillSpecChip> lstSpecs = new List<SkillSpecChip>();
            foreach (SkillSpecialization objSpec in objSkill.Specializations.ToList())
            {
                string strSpec = objSpec.CurrentDisplayName;
                int intBonus = blnSpecsCount ? objSkill.GetSpecializationBonus(objSpec.Name) : 0;
                lstSpecs.Add(new SkillSpecChip(strSpec, RollCatalog.SkillRollName(objSkill.CurrentDisplayName, strSpec), intBonus > 0 ? intPool + intBonus : 0, intBonus, objSpec.Free));
            }
            Specs = lstSpecs;
            SkillSpecChip objTop = lstSpecs.Where(x => x.Pool > 0).OrderByDescending(x => x.Pool).FirstOrDefault();
            if (objTop != null && intPool > 0)
            {
                SpecPool = objTop.Pool;
                SpecRollName = objTop.RollName;
                TopSpecName = objTop.Name;
            }

            CanHaveSpecs = objSkill.CanHaveSpecs && !IsExotic;
            CanDelete = objSkill.AllowDelete;
            if (blnCareer)
            {
                bool blnUpgradable = !(objKnowledge != null && !objKnowledge.AllowUpgrade);
                ImproveCost = objSkill.UpgradeKarmaCost;
                IsAtMaximum = ImproveCost < 0 || objSkill.TotalBaseRating >= objSkill.RatingMaximum;
                ShowImprove = blnUpgradable && !IsNative;
                CanImprove = ShowImprove && objSkill.CanUpgradeCareer;
                ImproveTip = !ShowImprove
                    ? string.Empty
                    : IsAtMaximum
                        ? Name + " is at its maximum rating (" + objSkill.RatingMaximum.ToString(CultureInfo) + ")"
                        : "Raise " + Name + " to " + (objSkill.TotalBaseRating + 1).ToString(CultureInfo) + " for " + ImproveCost.ToString(CultureInfo) + " karma"
                          + (CanImprove ? string.Empty : "  //  you have " + objSkill.CharacterObject.Karma.ToString(CultureInfo));
                if (CanHaveSpecs)
                {
                    SpecCost = SkillRules.GetSpecializationKarmaCost(objSkill.CharacterObject, objSkill);
                    CanAddSpec = objSkill.CanAffordSpecialization;
                    AddSpecTip = "Learn a new specialization for " + SpecCost.ToString(CultureInfo) + " karma"
                                 + (CanAddSpec ? string.Empty : "  //  you have " + objSkill.CharacterObject.Karma.ToString(CultureInfo));
                }
            }
            else
            {
                BasePoints = objSkill.Base;
                KarmaPoints = objSkill.Karma;
                bool blnBase = SkillRules.CanChangeBase(objSkill.CharacterObject, objSkill);
                bool blnKarma = SkillRules.CanChangeKarma(objSkill.CharacterObject, objSkill);
                int intMax = objSkill.RatingMaximum;
                ShowBase = IsKnowledge ? objSkill.CharacterObject.SkillsSection.HasKnowledgePoints : objSkill.CharacterObject.EffectiveBuildMethodUsesPriorityTables;
                CanBaseUp = blnBase && BasePoints < intMax && BasePoints + KarmaPoints < intMax;
                CanBaseDown = blnBase && BasePoints > objSkill.FreeBase;
                CanKarmaUp = blnKarma && KarmaPoints < intMax && BasePoints + KarmaPoints < intMax;
                CanKarmaDown = blnKarma && KarmaPoints > objSkill.FreeKarma;
                BuyWithKarma = objSkill.BuyWithKarma;
                ShowBuyWithKarma = CanHaveSpecs && objSkill.CharacterObject.EffectiveBuildMethodUsesPriorityTables;
                CanToggleNative = IsLanguage && (IsNative || objSkill.CharacterObject.SkillsSection.HasAvailableNativeLanguageSlots);
                AddSpecTip = HasSpecs ? "Change or clear the specialization" : "Choose a specialization";
            }

            SearchText = Name + " " + string.Join(" ", lstSpecs.Select(x => x.Name)) + " " + CategoryName + " " + Attribute + " " + TypeText;

            ImproveCommand = new AsyncRelayCommand(() => _objOwner.RunSkillRuleAsync(t => SkillRules.ImproveCareerAsync(_objSkill.CharacterObject, _objSkill, t)));
            AddSpecCommand = new AsyncRelayCommand(AddSpecAsync);
            DeleteCommand = new AsyncRelayCommand(() => _objOwner.RunSkillRuleAsync(t => SkillRules.DeleteAsync(_objSkill.CharacterObject, _objSkill, t)));
            BaseUpCommand = new AsyncRelayCommand(() => _objOwner.RunSkillRuleAsync(t => SkillRules.ChangeBaseAsync(_objSkill.CharacterObject, _objSkill, 1, t)));
            BaseDownCommand = new AsyncRelayCommand(() => _objOwner.RunSkillRuleAsync(t => SkillRules.ChangeBaseAsync(_objSkill.CharacterObject, _objSkill, -1, t)));
            KarmaUpCommand = new AsyncRelayCommand(() => _objOwner.RunSkillRuleAsync(t => SkillRules.ChangeKarmaAsync(_objSkill.CharacterObject, _objSkill, 1, t)));
            KarmaDownCommand = new AsyncRelayCommand(() => _objOwner.RunSkillRuleAsync(t => SkillRules.ChangeKarmaAsync(_objSkill.CharacterObject, _objSkill, -1, t)));
            ToggleBuyWithKarmaCommand = new AsyncRelayCommand(() => _objOwner.RunSkillRuleAsync(t => SkillRules.SetBuyWithKarmaAsync(_objSkill.CharacterObject, _objSkill, !BuyWithKarma, t)));
            ToggleNativeCommand = new AsyncRelayCommand(() => _objOwner.RunSkillRuleAsync(t => SkillRules.SetNativeLanguageAsync(_objSkill.CharacterObject, (KnowledgeSkill)_objSkill, !IsNative, t)));
            RenameCommand = new AsyncRelayCommand(RenameAsync);
            ChangeTypeCommand = new AsyncRelayCommand(ChangeTypeAsync);
        }

        private System.Globalization.CultureInfo CultureInfo { get; }

        /// <summary>The engine skill (for hosts and tools).</summary>
        public Skill Skill => _objSkill;

        public string Name { get; }
        public string SearchText { get; }
        public string IconKey { get; }
        public int CategorySort { get; }
        public string CategoryName { get; }
        public string DisplayCategory => CategoryName;
        public string Attribute { get; }
        public int Rating { get; }
        public int Pool { get; }
        public string PoolTip { get; }
        public bool IsKnowledge { get; }
        public bool IsLanguage { get; }
        public bool IsNative { get; }
        public bool IsExotic { get; }
        public bool IsNew { get; }
        public string TypeText { get; } = string.Empty;
        public bool HasTypeText => TypeText.Length > 0;
        public IReadOnlyList<SkillSpecChip> Specs { get; }
        public bool HasSpecs => Specs.Count > 0;
        public string Specialization => string.Join(", ", Specs.Select(x => x.Name));
        public bool HasSpecialization => HasSpecs;

        public string RatingText => IsNative ? "N" : Rating > 0 ? Rating.ToString(CultureInfo) : "–";
        public string RatingTip => IsNative ? "Native language" : Rating > 0 ? "Rating " + Rating.ToString(CultureInfo) : "Untrained" + (IsRollable ? " (defaulting)" : string.Empty);
        public string PoolText => IsNative ? "N" : Pool.ToString(CultureInfo);

        /// <summary>Natives sort as the best rating/pool (they speak it perfectly).</summary>
        public int SortRating => IsNative ? 99 : Rating;
        public int SortPool => IsNative ? -1 : Pool;

        public string RollName { get; }
        public string SpecRollName { get; } = string.Empty;
        public int SpecPool { get; }
        public string TopSpecName { get; } = string.Empty;
        public bool HasSpecPool => SpecPool > 0;
        public string SpecPoolText => HasSpecPool ? SpecPool.ToString(CultureInfo) : string.Empty;
        public string SpecPoolTip => HasSpecPool ? "Roll " + SpecPoolText + " dice with " + TopSpecName : string.Empty;
        public bool IsRollable { get; }
        public bool IsDefaulting => Rating == 0;
        public string RollTip => IsNative
            ? Name + ": native language"
            : IsRollable
                ? "Roll " + Name + "  //  " + Pool.ToString(CultureInfo) + " dice (" + Attribute + ")"
                : Name + ": no dice (untrained, no defaulting)";

        // ---------- editing ----------

        public bool IsCareer { get; }
        public bool IsCreate => !IsCareer;
        public bool CanHaveSpecs { get; }
        public bool CanDelete { get; }
        public bool CanRename { get; }
        public bool CanChangeType { get; }

        public bool ShowImprove { get; }
        public bool CanImprove { get; }
        public bool IsAtMaximum { get; }
        public int ImproveCost { get; }
        public string ImproveTip { get; } = string.Empty;
        public string ImproveText => IsAtMaximum ? "MAX" : "+" + ImproveCost.ToString(CultureInfo);

        public bool ShowAddSpec => CanHaveSpecs && (IsCareer || Rating > 0 || HasSpecs);
        public bool CanAddSpec { get; } = true;
        public int SpecCost { get; }
        public string AddSpecText => IsCareer ? "+ SPEC  " + SpecCost.ToString(CultureInfo) : HasSpecs ? "EDIT SPEC" : "+ SPEC";
        public string AddSpecTip { get; } = string.Empty;

        public int BasePoints { get; }
        public int KarmaPoints { get; }
        public string BaseText => BasePoints.ToString(CultureInfo);
        public string KarmaText => KarmaPoints.ToString(CultureInfo);
        public bool ShowBase { get; }
        public bool CanBaseUp { get; }
        public bool CanBaseDown { get; }
        public bool CanKarmaUp { get; }
        public bool CanKarmaDown { get; }
        public bool BuyWithKarma { get; }
        public bool ShowBuyWithKarma { get; }
        public bool CanToggleNative { get; }
        public bool ShowNativeToggle => IsCreate && IsLanguage;
        public string NativeToggleText => IsNative ? "NATIVE  ✓" : "MAKE NATIVE";

        public IAsyncRelayCommand ImproveCommand { get; }
        public IAsyncRelayCommand AddSpecCommand { get; }
        public IAsyncRelayCommand DeleteCommand { get; }
        public IAsyncRelayCommand BaseUpCommand { get; }
        public IAsyncRelayCommand BaseDownCommand { get; }
        public IAsyncRelayCommand KarmaUpCommand { get; }
        public IAsyncRelayCommand KarmaDownCommand { get; }
        public IAsyncRelayCommand ToggleBuyWithKarmaCommand { get; }
        public IAsyncRelayCommand ToggleNativeCommand { get; }
        public IAsyncRelayCommand RenameCommand { get; }
        public IAsyncRelayCommand ChangeTypeCommand { get; }

        private Task AddSpecAsync()
        {
            Character objCharacter = _objSkill.CharacterObject;
            if (IsCareer)
                return _objOwner.RunSkillRuleAsync(t => SkillRules.AddSpecializationCareerAsync(objCharacter, _objSkill,
                    (x, _) => ChooseOrTypeWindow.ShowAsync("Specialization: " + Name, x, true, null,
                                                           "Pick a specialization or type a custom one. Costs " + SpecCost.ToString(CultureInfo) + " karma."), t));
            return _objOwner.RunSkillRuleAsync(async t =>
            {
                string strSpec = await ChooseOrTypeWindow.ShowAsync("Specialization: " + Name, SkillRules.GetSpecializationOptions(objCharacter, _objSkill), true,
                                                                     Specs.LastOrDefault()?.Name, "Pick or type a specialization. Clear the box and press OK to remove it.")
                                                     .ConfigureAwait(false);
                if (strSpec == null)
                    return RuleResult.Cancelled;
                return await SkillRules.SetSpecializationCreateAsync(objCharacter, _objSkill, strSpec, t).ConfigureAwait(false);
            });
        }

        private Task RenameAsync() => _objOwner.RunSkillRuleAsync(async t =>
        {
            string strName = await ChooseOrTypeWindow.ShowAsync("Rename knowledge skill",
                await (await _objSkill.CharacterObject.GetSkillsSectionAsync(t).ConfigureAwait(false)).GetMyDefaultKnowledgeSkillsAsync(t).ConfigureAwait(false),
                true, IsNew ? string.Empty : Name).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(strName))
                return RuleResult.Cancelled;
            return await SkillRules.RenameKnowledgeSkillAsync((KnowledgeSkill)_objSkill, strName, t).ConfigureAwait(false);
        });

        private Task ChangeTypeAsync() => _objOwner.RunSkillRuleAsync(async t =>
        {
            string strType = await ChooseOrTypeWindow.ShowAsync("Knowledge skill type: " + Name,
                await (await _objSkill.CharacterObject.GetSkillsSectionAsync(t).ConfigureAwait(false)).GetMyKnowledgeTypesAsync(t).ConfigureAwait(false), false)
                                                     .ConfigureAwait(false);
            if (string.IsNullOrEmpty(strType))
                return RuleResult.Cancelled;
            return await SkillRules.SetKnowledgeTypeAsync((KnowledgeSkill)_objSkill, strType, t).ConfigureAwait(false);
        });

        internal static (string, int) MapCategory(string strCategory)
        {
            switch (strCategory)
            {
                case "Combat Active": return ("Icon.SkillCombat", 0);
                case "Physical Active": return ("Icon.SkillPhysical", 1);
                case "Social Active": return ("Icon.SkillSocial", 2);
                case "Magical Active":
                case "Pseudo-Magical Active": return ("Icon.SkillMagical", 3);
                case "Resonance Active": return ("Icon.SkillResonance", 4);
                case "Technical Active": return ("Icon.SkillTechnical", 5);
                case "Vehicle Active": return ("Icon.SkillVehicle", 6);
                case "Knowledge": return ("Icon.SkillKnowledge", 9);
                case "Language": return ("Icon.SkillLanguage", 10);
                default: return ("Icon.Skills", 8);
            }
        }
    }

    public sealed class SkillGroupViewModel
    {
        private readonly CharacterViewModel _objOwner;
        private readonly SkillGroup _objGroup;

        public SkillGroupViewModel(CharacterViewModel objOwner, SkillGroup objGroup, bool blnCareer)
        {
            _objOwner = objOwner;
            _objGroup = objGroup;
            System.Globalization.CultureInfo objCulture = GlobalSettings.CultureInfo;
            IsCareer = blnCareer;
            Name = objGroup.CurrentDisplayName;
            IsBroken = objGroup.IsBroken;
            Rating = IsBroken ? 0 : objGroup.Rating;
            RatingText = IsBroken ? "BROKEN" : Rating.ToString(objCulture);
            Members = string.Join(", ", objGroup.SkillList.Select(x => x.CurrentDisplayName));
            SearchText = Name + " " + Members;
            Tip = Name + ": " + Members + (IsBroken ? "\nBroken: its skills are raised individually now." : string.Empty);
            if (blnCareer)
            {
                int intMax = objGroup.RatingMaximum;
                ImproveCost = objGroup.UpgradeKarmaCost;
                IsAtMaximum = ImproveCost < 0 || Rating >= intMax;
                ShowImprove = !IsBroken;
                CanImprove = ShowImprove && !IsAtMaximum && objGroup.CareerCanIncrease;
                ImproveTip = IsAtMaximum
                    ? Name + " is at its maximum rating"
                    : "Raise " + Name + " to " + (Rating + 1).ToString(objCulture) + " for " + ImproveCost.ToString(objCulture) + " karma"
                      + (CanImprove ? string.Empty : "  //  you have " + objGroup.CharacterObject.Karma.ToString(objCulture));
            }
            else
            {
                BasePoints = objGroup.Base;
                KarmaPoints = objGroup.Karma;
                int intMax = objGroup.RatingMaximum;
                ShowBase = objGroup.CharacterObject.EffectiveBuildMethodUsesPriorityTables;
                CanBaseUp = ShowBase && objGroup.BaseUnbroken && BasePoints + KarmaPoints < intMax;
                CanBaseDown = ShowBase && objGroup.BaseUnbroken && BasePoints > 0;
                CanKarmaUp = objGroup.KarmaUnbroken && BasePoints + KarmaPoints < intMax;
                CanKarmaDown = objGroup.KarmaUnbroken && KarmaPoints > 0;
            }
            ImproveCommand = new AsyncRelayCommand(() => _objOwner.RunSkillRuleAsync(t => SkillRules.ImproveGroupCareerAsync(_objGroup.CharacterObject, _objGroup, t)));
            BaseUpCommand = new AsyncRelayCommand(() => _objOwner.RunSkillRuleAsync(t => SkillRules.ChangeGroupBaseAsync(_objGroup.CharacterObject, _objGroup, 1, t)));
            BaseDownCommand = new AsyncRelayCommand(() => _objOwner.RunSkillRuleAsync(t => SkillRules.ChangeGroupBaseAsync(_objGroup.CharacterObject, _objGroup, -1, t)));
            KarmaUpCommand = new AsyncRelayCommand(() => _objOwner.RunSkillRuleAsync(t => SkillRules.ChangeGroupKarmaAsync(_objGroup.CharacterObject, _objGroup, 1, t)));
            KarmaDownCommand = new AsyncRelayCommand(() => _objOwner.RunSkillRuleAsync(t => SkillRules.ChangeGroupKarmaAsync(_objGroup.CharacterObject, _objGroup, -1, t)));
        }

        public SkillGroup Group => _objGroup;
        public string Name { get; }
        public int Rating { get; }
        public string RatingText { get; }
        public bool IsBroken { get; }
        public string Members { get; }
        public string SearchText { get; }
        public string Tip { get; }
        public bool IsCareer { get; }
        public bool IsCreate => !IsCareer;
        public bool ShowImprove { get; }
        public bool CanImprove { get; }
        public bool IsAtMaximum { get; }
        public int ImproveCost { get; }
        public string ImproveText => IsAtMaximum ? "MAX" : "+" + ImproveCost.ToString(GlobalSettings.CultureInfo);
        public string ImproveTip { get; } = string.Empty;
        public int BasePoints { get; }
        public int KarmaPoints { get; }
        public string BaseText => BasePoints.ToString(GlobalSettings.CultureInfo);
        public string KarmaText => KarmaPoints.ToString(GlobalSettings.CultureInfo);
        public bool ShowBase { get; }
        public bool CanBaseUp { get; }
        public bool CanBaseDown { get; }
        public bool CanKarmaUp { get; }
        public bool CanKarmaDown { get; }
        public IAsyncRelayCommand ImproveCommand { get; }
        public IAsyncRelayCommand BaseUpCommand { get; }
        public IAsyncRelayCommand BaseDownCommand { get; }
        public IAsyncRelayCommand KarmaUpCommand { get; }
        public IAsyncRelayCommand KarmaDownCommand { get; }
    }
}
