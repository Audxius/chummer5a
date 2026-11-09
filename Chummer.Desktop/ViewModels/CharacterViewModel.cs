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
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Chummer.Backend.Attributes;
using Chummer.Backend.Skills;
using Chummer.Desktop.Localization;

namespace Chummer.Desktop.ViewModels
{
    public enum CharacterSection
    {
        Overview,
        Attributes,
        Skills,
        Qualities,
        Inventory,
        Abilities,
        Network,
        Notes
    }

    /// <summary>
    /// One open character: live summary plus the sections of the character screen.
    /// </summary>
    public sealed partial class CharacterViewModel : ViewModelBase
    {
        private CharacterViewModel(Character objCharacter)
        {
            Character = objCharacter;
        }

        public static async Task<CharacterViewModel> CreateAsync(Character objCharacter)
        {
            CharacterViewModel objReturn = new CharacterViewModel(objCharacter);
            await Task.Run(objReturn.LoadSnapshot).ConfigureAwait(true);
            objCharacter.PropertyChanged += objReturn.CharacterOnPropertyChanged;
            return objReturn;
        }

        public Character Character { get; }

        public string FilePath => Character.FileName;

        // ---------- header / summary ----------

        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private string _realName = string.Empty;
        [ObservableProperty] private string _metatype = string.Empty;
        [ObservableProperty] private string _mode = string.Empty;
        [ObservableProperty] private string _karma = string.Empty;
        [ObservableProperty] private string _nuyen = string.Empty;
        [ObservableProperty] private string _essence = string.Empty;
        [ObservableProperty] private Bitmap _portrait;
        [ObservableProperty] private bool _isDirty;
        [ObservableProperty] private CharacterSection _section = CharacterSection.Overview;
        [ObservableProperty] private string _skillFilter = string.Empty;
        [ObservableProperty] private bool _showUntrainedSkills;
        [ObservableProperty] private string _inventoryFilter = string.Empty;
        [ObservableProperty] private InventoryKind? _inventoryKind;
        [ObservableProperty] private string _inventorySummary = string.Empty;
        [ObservableProperty] private string _abilityFilter = string.Empty;
        [ObservableProperty] private string _networkFilter = string.Empty;

        public string TabTitle => (IsDirty ? "● " : string.Empty) + Name;

        partial void OnNameChanged(string value) => OnPropertyChanged(nameof(TabTitle));

        partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(TabTitle));

        partial void OnSkillFilterChanged(string value) => ApplySkillFilter();

        partial void OnShowUntrainedSkillsChanged(bool value) => ApplySkillFilter();

        partial void OnInventoryFilterChanged(string value) => ApplyInventoryFilter();

        partial void OnInventoryKindChanged(InventoryKind? value) => ApplyInventoryFilter();

        partial void OnAbilityFilterChanged(string value) => FilterCards(_lstAllAbilities, AbilityGroups, value);

        partial void OnNetworkFilterChanged(string value) => FilterCards(_lstAllNetwork, NetworkGroups, value);

        public bool IsOverview => Section == CharacterSection.Overview;
        public bool IsAttributes => Section == CharacterSection.Attributes;
        public bool IsSkills => Section == CharacterSection.Skills;
        public bool IsQualities => Section == CharacterSection.Qualities;
        public bool IsInventory => Section == CharacterSection.Inventory;
        public bool IsAbilities => Section == CharacterSection.Abilities;
        public bool IsNetwork => Section == CharacterSection.Network;
        public bool HasAbilities => _lstAllAbilities.Count > 0;
        public bool IsNotes => Section == CharacterSection.Notes;

        partial void OnSectionChanged(CharacterSection value)
        {
            OnPropertyChanged(nameof(IsOverview));
            OnPropertyChanged(nameof(IsAttributes));
            OnPropertyChanged(nameof(IsSkills));
            OnPropertyChanged(nameof(IsQualities));
            OnPropertyChanged(nameof(IsNotes));
            OnPropertyChanged(nameof(IsInventory));
            OnPropertyChanged(nameof(IsAbilities));
            OnPropertyChanged(nameof(IsNetwork));
        }

        // ---------- sections ----------

        public ObservableCollection<AttributeViewModel> Attributes { get; } = new ObservableCollection<AttributeViewModel>();
        public ObservableCollection<AttributeViewModel> SpecialAttributes { get; } = new ObservableCollection<AttributeViewModel>();
        public ObservableCollection<SkillCategoryViewModel> SkillCategories { get; } = new ObservableCollection<SkillCategoryViewModel>();
        public ObservableCollection<QualityViewModel> PositiveQualities { get; } = new ObservableCollection<QualityViewModel>();
        public ObservableCollection<QualityViewModel> NegativeQualities { get; } = new ObservableCollection<QualityViewModel>();

        [ObservableProperty] private string _concept = string.Empty;
        [ObservableProperty] private string _description = string.Empty;
        [ObservableProperty] private string _background = string.Empty;
        [ObservableProperty] private string _notes = string.Empty;

        private List<SkillCategoryViewModel> _lstAllSkillCategories = new List<SkillCategoryViewModel>();
        private List<InventoryItemViewModel> _lstAllInventory = new List<InventoryItemViewModel>();

        public ObservableCollection<InventoryGroupViewModel> InventoryGroups { get; } = new ObservableCollection<InventoryGroupViewModel>();
        public ObservableCollection<InventoryFilterOption> InventoryKinds { get; } = new ObservableCollection<InventoryFilterOption>();
        public ObservableCollection<InventoryGroupViewModel> AbilityGroups { get; } = new ObservableCollection<InventoryGroupViewModel>();
        public ObservableCollection<InventoryGroupViewModel> NetworkGroups { get; } = new ObservableCollection<InventoryGroupViewModel>();
        private List<InventoryGroupViewModel> _lstAllAbilities = new List<InventoryGroupViewModel>();
        private List<InventoryGroupViewModel> _lstAllNetwork = new List<InventoryGroupViewModel>();

        private void LoadSnapshot()
        {
            RefreshSummary();
            List<AttributeViewModel> lstAttributes = Character.AttributeSection.AttributeList.ToList().Select(x => new AttributeViewModel(x)).ToList();
            List<AttributeViewModel> lstSpecial = Character.AttributeSection.SpecialAttributeList.ToList().Select(x => new AttributeViewModel(x)).ToList();
            List<SkillCategoryViewModel> lstCategories = Character.SkillsSection.Skills.ToList()
                                                                  .GroupBy(x => x.SkillCategory)
                                                                  .Select(x => new SkillCategoryViewModel(x.Key, x))
                                                                  .OrderBy(x => x.SortOrder).ThenBy(x => x.Name, StringComparer.CurrentCulture)
                                                                  .ToList();
            List<KnowledgeSkill> lstKnowledge = Character.SkillsSection.KnowledgeSkills.ToList();
            if (lstKnowledge.Count > 0)
                lstCategories.Add(new SkillCategoryViewModel("Knowledge", lstKnowledge));
            List<QualityViewModel> lstQualities = Character.Qualities.ToList().Select(x => new QualityViewModel(x)).ToList();
            string strConcept = Character.Concept.RtfToPlainText();
            string strDescription = Character.Description.RtfToPlainText();
            string strBackground = Character.Background.RtfToPlainText();
            string strNotes = Character.Notes.RtfToPlainText();
            List<InventoryItemViewModel> lstInventory = InventoryItemViewModel.FromCharacter(Character).ToList();
            List<InventoryGroupViewModel> lstAbilities = AbilityCards.Abilities(Character);
            List<InventoryGroupViewModel> lstNetwork = AbilityCards.Network(Character);
            OnUi(() =>
            {
                Reset(Attributes, lstAttributes);
                Reset(SpecialAttributes, lstSpecial);
                _lstAllSkillCategories = lstCategories;
                ApplySkillFilter();
                Reset(PositiveQualities, lstQualities.Where(x => x.IsPositive));
                Reset(NegativeQualities, lstQualities.Where(x => !x.IsPositive));
                Concept = strConcept;
                Description = strDescription;
                Background = strBackground;
                Notes = strNotes;
                _lstAllInventory = lstInventory;
                RefreshInventoryKinds();
                ApplyInventoryFilter();
                _lstAllAbilities = lstAbilities;
                _lstAllNetwork = lstNetwork;
                OnPropertyChanged(nameof(HasAbilities));
                FilterCards(_lstAllAbilities, AbilityGroups, AbilityFilter);
                FilterCards(_lstAllNetwork, NetworkGroups, NetworkFilter);
            });
        }

        private static void Reset<T>(ObservableCollection<T> lstTarget, IEnumerable<T> lstItems)
        {
            lstTarget.Clear();
            foreach (T objItem in lstItems)
                lstTarget.Add(objItem);
        }

        private void RefreshSummary()
        {
            string strName = !string.IsNullOrWhiteSpace(Character.Alias)
                ? Character.Alias
                : !string.IsNullOrWhiteSpace(Character.CharacterName) ? Character.CharacterName : Tr.Get("String_UnnamedCharacter");
            string strRealName = Character.CharacterName ?? string.Empty;
            string strMetatype = Character.DisplayMetatype(GlobalSettings.Language);
            string strMode = Tr.Get(Character.Created ? "Title_CareerMode" : "Title_CreateMode").ToUpper(GlobalSettings.CultureInfo);
            string strKarma = Character.DisplayKarma;
            string strNuyen = Character.DisplayNuyen;
            string strEssence = Character.DisplayEssence;
            Bitmap objPortrait = null;
            MugshotImage objMugshot = Character.MainMugshot;
            if (objMugshot != null)
            {
                try
                {
                    using (MemoryStream objStream = new MemoryStream(objMugshot.EncodedBytes))
                        objPortrait = Bitmap.DecodeToWidth(objStream, 256);
                }
                catch (Exception)
                {
                    objPortrait = null;
                }
            }
            OnUi(() =>
            {
                Name = strName;
                RealName = strRealName;
                Metatype = strMetatype;
                Mode = strMode;
                Karma = strKarma;
                Nuyen = strNuyen;
                Essence = strEssence;
                Portrait = objPortrait;
            });
        }

        private void ApplySkillFilter()
        {
            string strFilter = SkillFilter?.Trim() ?? string.Empty;
            SkillCategories.Clear();
            foreach (SkillCategoryViewModel objCategory in _lstAllSkillCategories)
            {
                if (objCategory.ApplyFilter(strFilter, ShowUntrainedSkills))
                    SkillCategories.Add(objCategory);
            }
        }

        private static void FilterCards(List<InventoryGroupViewModel> lstAll, ObservableCollection<InventoryGroupViewModel> lstTarget, string strFilter)
        {
            string[] astrWords = (strFilter ?? string.Empty).ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            lstTarget.Clear();
            foreach (InventoryGroupViewModel objGroup in lstAll)
            {
                if (astrWords.Length == 0)
                {
                    lstTarget.Add(objGroup);
                    continue;
                }
                List<InventoryItemViewModel> lstItems = objGroup.Items.Where(x => astrWords.All(w => x.SearchText.Contains(w))).ToList();
                if (lstItems.Count > 0)
                    lstTarget.Add(new InventoryGroupViewModel(objGroup.Name, lstItems));
            }
        }

        private void RefreshInventoryKinds()
        {
            InventoryKinds.Clear();
            InventoryKinds.Add(new InventoryFilterOption(null, "ALL", "Icon.Gear", _lstAllInventory.Count, this));
            foreach (IGrouping<InventoryKind, InventoryItemViewModel> objGroup in _lstAllInventory.GroupBy(x => x.Kind).OrderBy(x => x.Key))
                InventoryKinds.Add(new InventoryFilterOption(objGroup.Key, InventoryGroupViewModel.Title(objGroup.Key), objGroup.First().IconKey, objGroup.Count(), this));
        }

        private void ApplyInventoryFilter()
        {
            string[] astrWords = (InventoryFilter ?? string.Empty).ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            InventoryGroups.Clear();
            int intShown = 0;
            foreach (IGrouping<InventoryKind, InventoryItemViewModel> objGroup in _lstAllInventory
                         .Where(x => InventoryKind == null || x.Kind == InventoryKind.Value)
                         .Where(x => astrWords.All(w => x.SearchText.Contains(w)))
                         .GroupBy(x => x.Kind).OrderBy(x => x.Key))
            {
                InventoryGroupViewModel objViewModel = new InventoryGroupViewModel(objGroup.Key, objGroup.OrderBy(x => x.Category, StringComparer.CurrentCulture).ThenBy(x => x.Name, StringComparer.CurrentCulture));
                intShown += objViewModel.Items.Count;
                InventoryGroups.Add(objViewModel);
            }
            foreach (InventoryFilterOption objOption in InventoryKinds)
                objOption.Refresh();
            InventorySummary = intShown.ToString(GlobalSettings.CultureInfo) + " / " + _lstAllInventory.Count.ToString(GlobalSettings.CultureInfo) + " ITEMS";
        }

        private void CharacterOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            OnUi(() => IsDirty = true);
            // Summary values are cheap to recompute; sections refresh when the user next opens them.
            _ = Task.Run(RefreshSummary);
        }

        // ---------- commands ----------

        [RelayCommand]
        private void ShowSection(CharacterSection eSection)
        {
            Section = eSection;
            if (eSection != CharacterSection.Overview)
                _ = Task.Run(LoadSnapshot);
        }

        [RelayCommand]
        private async Task Save()
        {
            if (await Character.SaveAsync().ConfigureAwait(true))
                IsDirty = false;
        }

        public async Task<bool> ConfirmCloseAsync()
        {
            if (!IsDirty)
                return true;
            MessageResult eResult = await MessageBoxes.ShowAsync(
                string.Format(GlobalSettings.CultureInfo, Tr.Get("Message_UnsavedChanges"), Name),
                Tr.Get("MessageTitle_UnsavedChanges"), MessageButtons.YesNoCancel, MessageIcon.Question).ConfigureAwait(true);
            switch (eResult)
            {
                case MessageResult.Yes:
                    return await Character.SaveAsync().ConfigureAwait(true);
                case MessageResult.No:
                    return true;
                default:
                    return false;
            }
        }

        public async Task DisposeCharacterAsync()
        {
            Character.PropertyChanged -= CharacterOnPropertyChanged;
            if (await CharacterRegistry.OpenCharacters.ContainsAsync(Character).ConfigureAwait(false))
                await CharacterRegistry.OpenCharacters.RemoveAsync(Character).ConfigureAwait(false);
            await Character.DisposeAsync().ConfigureAwait(false);
        }
    }

    public sealed class AttributeViewModel
    {
        public AttributeViewModel(CharacterAttrib objAttribute)
        {
            Abbrev = objAttribute.Abbrev;
            Name = objAttribute.DisplayNameShort(GlobalSettings.Language);
            Value = objAttribute.Value;
            Total = objAttribute.TotalValue;
            Maximum = objAttribute.TotalMaximum;
            Display = objAttribute.DisplayValue;
            IconKey = "Icon." + (Abbrev == "MAGAdept" ? "MAG" : Abbrev);
            Fill = Maximum > 0 ? Math.Min(1.0, Total / (double)Maximum) : 0;
        }

        public string Abbrev { get; }
        public string Name { get; }
        public int Value { get; }
        public int Total { get; }
        public int Maximum { get; }
        public string Display { get; }
        public string IconKey { get; }

        /// <summary>0..1 share of the maximum, drawn as a bar.</summary>
        public double Fill { get; }

        public string MaximumText => "/ " + Maximum.ToString(GlobalSettings.CultureInfo);
    }

    public sealed class SkillCategoryViewModel
    {
        private readonly List<SkillViewModel> _lstAll;

        public SkillCategoryViewModel(string strCategory, IEnumerable<Skill> lstSkills)
        {
            Category = strCategory ?? string.Empty;
            _lstAll = lstSkills.Select(x => new SkillViewModel(x)).OrderBy(x => x.Name, StringComparer.CurrentCulture).ToList();
            Name = Category == "Knowledge"
                ? Tr.Get("Label_KnowledgeSkills")
                : _lstAll.FirstOrDefault()?.DisplayCategory ?? Category;
            (IconKey, SortOrder) = MapCategory(Category);
        }

        public string Category { get; }
        public string Name { get; }
        public string IconKey { get; }
        public int SortOrder { get; }
        public ObservableCollection<SkillViewModel> Skills { get; } = new ObservableCollection<SkillViewModel>();

        public bool ApplyFilter(string strFilter, bool blnShowUntrained)
        {
            Skills.Clear();
            foreach (SkillViewModel objSkill in _lstAll)
            {
                if (!blnShowUntrained && objSkill.Rating == 0 && strFilter.Length == 0)
                    continue;
                if (strFilter.Length > 0 && objSkill.Name.IndexOf(strFilter, StringComparison.CurrentCultureIgnoreCase) < 0)
                    continue;
                Skills.Add(objSkill);
            }
            return Skills.Count > 0;
        }

        private static (string, int) MapCategory(string strCategory)
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
                default: return ("Icon.Skills", 8);
            }
        }
    }

    public sealed class SkillViewModel
    {
        public SkillViewModel(Skill objSkill)
        {
            Name = objSkill.CurrentDisplayName;
            DisplayCategory = objSkill.DisplayCategory(GlobalSettings.Language);
            Rating = objSkill.Rating;
            Pool = objSkill.DisplayPool;
            Attribute = objSkill.Attribute;
            Specialization = objSkill.CurrentDisplaySpecialization;
            IsDefaulting = Rating == 0;
        }

        public string Name { get; }
        public string DisplayCategory { get; }
        public int Rating { get; }
        public string Pool { get; }
        public string Attribute { get; }
        public string Specialization { get; }
        public bool HasSpecialization => !string.IsNullOrEmpty(Specialization);
        public bool IsDefaulting { get; }

        /// <summary>Rating pips 1..12 (filled up to the rating).</summary>
        public IReadOnlyList<bool> Pips => Enumerable.Range(1, Math.Max(6, Rating)).Select(x => x <= Rating).ToList();
    }

    public sealed class QualityViewModel
    {
        public QualityViewModel(Quality objQuality)
        {
            Name = objQuality.CurrentDisplayName;
            Karma = objQuality.BP * Math.Max(1, objQuality.Levels);
            IsPositive = objQuality.Type == QualityType.Positive;
            Source = objQuality.SourceDetail.ToString();
        }

        public string Name { get; }
        public int Karma { get; }
        public bool IsPositive { get; }
        public string Source { get; }
        public string KarmaText => (IsPositive ? "+" : string.Empty) + Karma.ToString(GlobalSettings.CultureInfo);
    }
}
