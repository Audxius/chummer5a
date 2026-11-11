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
using System.Threading;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Chummer.Backend.Attributes;
using Chummer.Backend.Skills;
using Chummer.Desktop.Localization;
using Chummer.Rolls;

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
        Notes,
        Ledger,
        Improvements
    }

    /// <summary>
    /// One open character: live summary plus the sections of the character screen.
    /// </summary>
    public sealed partial class CharacterViewModel : ViewModelBase
    {
        private int _summaryRefreshPending;
        private int _summaryRefreshRunning;
        private volatile bool _isCharacterDisposed;
        private readonly SemaphoreSlim _engineOperationGate = new SemaphoreSlim(1, 1);
        private CharacterViewModel(Character objCharacter)
        {
            Character = objCharacter;
            Attributes.CollectionChanged += (sender, args) =>
            {
                OnPropertyChanged(nameof(PhysicalAttributes));
                OnPropertyChanged(nameof(MentalAttributes));
            };
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
        [ObservableProperty] private IReadOnlyList<ConditionBoxViewModel> _physicalBoxes = Array.Empty<ConditionBoxViewModel>();
        [ObservableProperty] private IReadOnlyList<ConditionBoxViewModel> _stunBoxes = Array.Empty<ConditionBoxViewModel>();
        [ObservableProperty] private string _physicalLabel = string.Empty;
        [ObservableProperty] private string _stunLabel = string.Empty;
        [ObservableProperty] private IReadOnlyList<ItemStat> _combatStats = Array.Empty<ItemStat>();
        [ObservableProperty] private IReadOnlyList<ItemStat> _derivedStats = Array.Empty<ItemStat>();
        [ObservableProperty] private CharacterSection _section = CharacterSection.Overview;
        [ObservableProperty] private string _skillFilter = string.Empty;
        [ObservableProperty] private bool _showUntrainedSkills;
        [ObservableProperty] private string _inventoryFilter = string.Empty;
        [ObservableProperty] private InventoryKind? _inventoryKind;
        [ObservableProperty] private string _inventorySummary = string.Empty;
        [ObservableProperty] private string _abilityFilter = string.Empty;
        [ObservableProperty] private string _networkFilter = string.Empty;

        /// <summary>Opens the Dice Terminal on a roll for this runner and copies its Roll20 formula.</summary>
        [RelayCommand]
        private Task Roll(string strRollName) => MainWindowViewModel.Current?.Dice.RollForAsync(this, strRollName) ?? Task.CompletedTask;

        public string TabTitle => (IsDirty ? "● " : string.Empty) + Name;

        partial void OnNameChanged(string value) => OnPropertyChanged(nameof(TabTitle));

        partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(TabTitle));

        partial void OnSkillFilterChanged(string value) => ApplySkillFilter();

        partial void OnShowUntrainedSkillsChanged(bool value) => ApplySkillFilter();

        partial void OnInventoryFilterChanged(string value) => ApplyInventoryFilter();

        partial void OnInventoryKindChanged(InventoryKind? value) => ApplyInventoryFilter();

        partial void OnAbilityFilterChanged(string value) => ApplyAbilityFilter();

        partial void OnNetworkFilterChanged(string value)
        {
            FilterCards(_lstAllNetwork, NetworkGroups, value);
            ApplyContactFilter();
        }

        public ObservableCollection<ContactCardViewModel> ContactCards { get; } = new ObservableCollection<ContactCardViewModel>();
        public ObservableCollection<ContactCardViewModel> EnemyCards { get; } = new ObservableCollection<ContactCardViewModel>();
        public ObservableCollection<ContactCardViewModel> PetCards { get; } = new ObservableCollection<ContactCardViewModel>();
        public bool HasContactCards => ContactCards.Count > 0;
        public bool HasEnemyCards => EnemyCards.Count > 0;
        public bool HasPetCards => PetCards.Count > 0;
        public string ContactCountText => ContactCards.Count.ToString(GlobalSettings.CultureInfo);
        private List<ContactCardViewModel> _lstAllContacts = new List<ContactCardViewModel>();

        private void ApplyContactFilter()
        {
            string strFilter = NetworkFilter?.Trim() ?? string.Empty;
            List<ContactCardViewModel> lstMatches = _lstAllContacts.Where(x => strFilter.Length == 0 || x.SearchText.IndexOf(strFilter, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            Reset(ContactCards, lstMatches.Where(x => !x.IsEnemy && !x.IsPet));
            Reset(EnemyCards, lstMatches.Where(x => x.IsEnemy));
            Reset(PetCards, lstMatches.Where(x => x.IsPet));
            OnPropertyChanged(nameof(HasContactCards));
            OnPropertyChanged(nameof(HasEnemyCards));
            OnPropertyChanged(nameof(HasPetCards));
            OnPropertyChanged(nameof(ContactCountText));
        }

        private CharacterInfoViewModel _objInfo;

        /// <summary>Editable identity / looks / notes (created on first use).</summary>
        public CharacterInfoViewModel Info => _objInfo ??= new CharacterInfoViewModel(this);

        /// <summary>Flags unsaved changes made through objects that do not raise Character.PropertyChanged (contacts...).</summary>
        internal void MarkDirty() => OnUi(() => IsDirty = true);

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
            OnPropertyChanged(nameof(IsLedger));
            OnPropertyChanged(nameof(IsImprovements));
        }

        // ---------- sections ----------

        public ObservableCollection<AttributeViewModel> Attributes { get; } = new ObservableCollection<AttributeViewModel>();
        public ObservableCollection<AttributeViewModel> SpecialAttributes { get; } = new ObservableCollection<AttributeViewModel>();
        public IReadOnlyList<AttributeViewModel> PhysicalAttributes => Attributes.Where(x => x.IsPhysical).ToList();
        public IReadOnlyList<AttributeViewModel> MentalAttributes => Attributes.Where(x => !x.IsPhysical).ToList();
        public ObservableCollection<QualityViewModel> PositiveQualities { get; } = new ObservableCollection<QualityViewModel>();
        public ObservableCollection<QualityViewModel> NegativeQualities { get; } = new ObservableCollection<QualityViewModel>();

        [ObservableProperty] private string _concept = string.Empty;
        [ObservableProperty] private string _description = string.Empty;
        [ObservableProperty] private string _background = string.Empty;
        [ObservableProperty] private string _notes = string.Empty;

        private List<InventoryItemViewModel> _lstAllInventory = new List<InventoryItemViewModel>();

        public ObservableCollection<InventoryGroupViewModel> InventoryGroups { get; } = new ObservableCollection<InventoryGroupViewModel>();
        public ObservableCollection<InventoryFilterOption> InventoryKinds { get; } = new ObservableCollection<InventoryFilterOption>();
        public ObservableCollection<InventoryGroupViewModel> AbilityGroups { get; } = new ObservableCollection<InventoryGroupViewModel>();
        public ObservableCollection<InventoryGroupViewModel> NetworkGroups { get; } = new ObservableCollection<InventoryGroupViewModel>();
        private List<InventoryGroupViewModel> _lstAllAbilities = new List<InventoryGroupViewModel>();
        private List<InventoryGroupViewModel> _lstAllNetwork = new List<InventoryGroupViewModel>();

        /// <summary>Re-reads every section from the engine (after an edit).</summary>
        internal void Reload() => _ = Task.Run(LoadSnapshot);

        private async Task LoadSnapshot()
        {
            await _engineOperationGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_isCharacterDisposed) return;
                await RefreshSummaryCore().ConfigureAwait(false);
                bool careerMode = Character.Created;
                bool lifeModuleMode = !careerMode && Character.EffectiveBuildMethod == Chummer.Backend.Enums.CharacterBuildMethod.LifeModule;
                List<AttributeViewModel> lstAttributes = Character.AttributeSection.AttributeList.ToList().Select(x => new AttributeViewModel(this, x)).ToList();
                bool blnMag = Character.MAGEnabled;
                bool blnMysAd = blnMag && Character.IsMysticAdept;
                bool blnRes = Character.RESEnabled;
                bool blnDep = Character.DEPEnabled;
                // Like the legacy sheet: only the special attributes the runner actually has (essence is in the header).
                List<AttributeViewModel> lstSpecial = Character.AttributeSection.SpecialAttributeList.ToList()
                    .Where(x => x.Abbrev == "EDG" || (x.Abbrev == "MAG" && blnMag) || (x.Abbrev == "MAGAdept" && blnMysAd) || (x.Abbrev == "RES" && blnRes) || (x.Abbrev == "DEP" && blnDep))
                    .Select(x => new AttributeViewModel(this, x)).ToList();
                SkillsSnapshot objSkills = BuildSkillsSnapshot();
                List<QualityViewModel> lstQualities = QualityViewModel.FromCharacter(this);
                var qualitySummary = ReadQualitySummary(lstQualities);
                string strConcept = Character.Concept.RtfToPlainText();
                string strDescription = Character.Description.RtfToPlainText();
                string strBackground = Character.Background.RtfToPlainText();
                string strNotes = Character.Notes.RtfToPlainText();
                List<InventoryItemViewModel> lstInventory = InventoryItemViewModel.FromCharacter(Character).ToList();
                List<AbilityGroupViewModel> lstAbilitySections = AbilityCards.Abilities(this);
                List<InventoryGroupViewModel> lstAbilities = lstAbilitySections.Select(x => x.ToInventoryGroup()).ToList();
                List<InventoryGroupViewModel> lstNetwork = AbilityCards.Network(Character);
                List<ContactCardViewModel> lstContacts = Character.Contacts.ToList().Select(x => new ContactCardViewModel(this, x))
                                                                  .OrderByDescending(x => x.Connection + x.Loyalty).ThenBy(x => x.Name, StringComparer.CurrentCulture).ToList();
                Ledger.Load();
                List<CustomImprovementRow> customImprovements = BuildCustomImprovements();
                OnUi(() =>
                {
                    _careerMode = careerMode;
                    _lifeModuleMode = lifeModuleMode;
                    OnPropertyChanged(nameof(IsCreateMode));
                    OnPropertyChanged(nameof(IsCareerMode));
                    OnPropertyChanged(nameof(IsLifeModuleBuild));
                    Reset(Attributes, lstAttributes);
                    _allCustomImprovements = customImprovements;
                    FilterCustomImprovements();
                    Reset(SpecialAttributes, lstSpecial);
                    ApplySkillsSnapshot(objSkills);
                    Reset(PositiveQualities, lstQualities.Where(x => x.Type == QualityType.Positive));
                    Reset(NegativeQualities, lstQualities.Where(x => x.Type == QualityType.Negative));
                    Reset(LifeModuleQualities, lstQualities.Where(x => x.Type == QualityType.LifeModule));
                    PositiveQualitySummary = qualitySummary.Positive;
                    NegativeQualitySummary = qualitySummary.Negative;
                    QualityHint = qualitySummary.Hint;
                    Concept = strConcept;
                    Description = strDescription;
                    Background = strBackground;
                    Notes = strNotes;
                    _lstAllInventory = lstInventory;
                    RefreshInventoryKinds();
                    ApplyInventoryFilter();
                    _lstAllAbilities = lstAbilities;
                    _lstAllNetwork = lstNetwork;
                    _lstAllContacts = lstContacts;
                    ApplyContactFilter();
                    OnPropertyChanged(nameof(HasAbilities));
                    _lstAllAbilitySections = lstAbilitySections;
                    ApplyAbilityFilter();
                    FilterCards(_lstAllNetwork, NetworkGroups, NetworkFilter);
                });
            }
            finally { _engineOperationGate.Release(); }
        }

        private static void Reset<T>(ObservableCollection<T> lstTarget, IEnumerable<T> lstItems)
        {
            lstTarget.Clear();
            foreach (T objItem in lstItems)
                lstTarget.Add(objItem);
        }

        private async Task RefreshSummary()
        {
            await _engineOperationGate.WaitAsync().ConfigureAwait(false);
            try { if (!_isCharacterDisposed) await RefreshSummaryCore().ConfigureAwait(false); }
            finally { _engineOperationGate.Release(); }
        }

        private async Task RefreshSummaryCore()
        {
            string strName = !string.IsNullOrWhiteSpace(Character.Alias)
                ? Character.Alias
                : !string.IsNullOrWhiteSpace(Character.CharacterName) ? Character.CharacterName : Tr.Get("String_UnnamedCharacter");
            string strRealName = Character.CharacterName ?? string.Empty;
            string strMetatype = Character.DisplayMetatype(GlobalSettings.Language);
            string strMode = Tr.Get(Character.Created ? "Title_CareerMode" : "Title_CreateMode").ToUpper(GlobalSettings.CultureInfo);
            string strKarma = Character.DisplayKarma;
            string strNuyen = Character.DisplayNuyen;
            if (!Character.Created)
                (strKarma, strNuyen) = await ReadCreationBudget().ConfigureAwait(false);
            string strEssence = Character.DisplayEssence;
            int intPhysical = Character.PhysicalCM;
            int intPhysicalFilled = Character.PhysicalCMFilled;
            int intStun = Character.StunCM;
            int intStunFilled = Character.StunCMFilled;
            System.Globalization.CultureInfo objCulture = GlobalSettings.CultureInfo;
            List<ItemStat> lstCombat = new List<ItemStat>
            {
                new ItemStat("Initiative", Character.Initiative, RollCatalog.InitiativeRoll),
                new ItemStat("Matrix init. (cold sim)", Character.MatrixInitiativeCold, RollCatalog.MatrixColdRoll),
                new ItemStat("Matrix init. (hot sim)", Character.MatrixInitiativeHot, RollCatalog.MatrixHotRoll),
                new ItemStat("Armor (damage resistance)", Character.TotalArmorRating.ToString(objCulture), RollCatalog.DamageResistanceRoll),
                new ItemStat("Wound modifier", Character.WoundModifier.ToString(objCulture)),
                new ItemStat("Movement", Character.DisplayMovement),
            };
            if (Character.MAGEnabled)
                lstCombat.Insert(1, new ItemStat("Astral initiative", Character.AstralInitiative, RollCatalog.AstralInitiativeRoll));
            List<ItemStat> lstDerived = new List<ItemStat>
            {
                new ItemStat("PHYSICAL", Character.LimitPhysical.ToString(objCulture)),
                new ItemStat("MENTAL", Character.LimitMental.ToString(objCulture)),
                new ItemStat("SOCIAL", Character.LimitSocial.ToString(objCulture)),
                new ItemStat("COMPOSURE", Character.Composure.ToString(objCulture), RollCatalog.ComposureRoll),
                new ItemStat("JUDGE INTENT", Character.JudgeIntentions.ToString(objCulture), RollCatalog.JudgeIntentionsRoll),
                new ItemStat("MEMORY", Character.Memory.ToString(objCulture), RollCatalog.MemoryRoll),
                new ItemStat("LIFT / CARRY", Character.LiftAndCarry.ToString(objCulture), RollCatalog.LiftCarryRoll),
            };
            if (Character.MAGEnabled)
                lstDerived.Insert(3, new ItemStat("ASTRAL", Character.LimitAstral.ToString(objCulture)));
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
                PhysicalBoxes = Enumerable.Range(1, intPhysical).Select(x => new ConditionBoxViewModel(this, false, x, x <= intPhysicalFilled)).ToList();
                StunBoxes = Enumerable.Range(1, intStun).Select(x => new ConditionBoxViewModel(this, true, x, x <= intStunFilled)).ToList();
                PhysicalLabel = "PHYSICAL  " + intPhysicalFilled.ToString(objCulture) + " / " + intPhysical.ToString(objCulture);
                StunLabel = "STUN  " + intStunFilled.ToString(objCulture) + " / " + intStun.ToString(objCulture);
                CombatStats = lstCombat;
                DerivedStats = lstDerived;
                Portrait = objPortrait;
            });
            RefreshEdge();
        }

        /// <summary>Entries for the global search palette (Ctrl+K).</summary>
        public IEnumerable<PaletteEntry> PaletteEntries()
        {
            foreach (SkillViewModel objSkill in _lstAllSkills.Where(x => x.Rating > 0))
                yield return new PaletteEntry(objSkill.Name, Name + "  //  skill " + objSkill.Pool, "Icon.Skills", () => Navigate(CharacterSection.Skills, objSkill.Name));
            foreach (InventoryItemViewModel objItem in _lstAllInventory)
                yield return new PaletteEntry(objItem.Name, Name + "  //  " + objItem.KindLabel.ToLowerInvariant() + "  " + objItem.Headline, objItem.IconKey, () => Navigate(CharacterSection.Inventory, objItem.Name));
            foreach (InventoryItemViewModel objItem in _lstAllAbilities.SelectMany(x => x.Items))
                yield return new PaletteEntry(objItem.Name, Name + "  //  " + objItem.Category, objItem.IconKey, () => Navigate(CharacterSection.Abilities, objItem.Name));
            foreach (InventoryItemViewModel objItem in _lstAllNetwork.SelectMany(x => x.Items))
                yield return new PaletteEntry(objItem.Name, Name + "  //  " + objItem.Category, objItem.IconKey, () => Navigate(CharacterSection.Network, objItem.Name));
            foreach (ContactCardViewModel objContact in _lstAllContacts)
                yield return new PaletteEntry(objContact.Name, Name + "  //  " + (objContact.IsEnemy ? "enemy" : objContact.IsPet ? "pet" : "contact") + (objContact.HasSubtitle ? "  " + objContact.Subtitle : string.Empty), "Icon.Contact", () => Navigate(CharacterSection.Network, objContact.Name));
            foreach (QualityViewModel objQuality in PositiveQualities.Concat(NegativeQualities))
                yield return new PaletteEntry(objQuality.Name, Name + "  //  quality " + objQuality.KarmaText, "Icon.Quality", () => Navigate(CharacterSection.Qualities, null));
        }

        public void Navigate(CharacterSection eSection, string strFilter)
        {
            MainWindowViewModel.Current.CurrentPage = this;
            switch (eSection)
            {
                case CharacterSection.Skills:
                    SkillFilter = strFilter ?? string.Empty;
                    break;
                case CharacterSection.Inventory:
                    InventoryKind = null;
                    InventoryFilter = strFilter ?? string.Empty;
                    break;
                case CharacterSection.Abilities:
                    AbilityFilter = strFilter ?? string.Empty;
                    break;
                case CharacterSection.Network:
                    NetworkFilter = strFilter ?? string.Empty;
                    break;
            }
            Section = eSection;
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
            foreach (InventoryKind kind in Enum.GetValues<InventoryKind>())
                InventoryKinds.Add(new InventoryFilterOption(kind, InventoryGroupViewModel.Title(kind),
                    _lstAllInventory.FirstOrDefault(x => x.Kind == kind)?.IconKey ?? "Icon.Gear", _lstAllInventory.Count(x => x.Kind == kind), this));
        }

        private void ApplyInventoryFilter()
        {
            string[] astrWords = (InventoryFilter ?? string.Empty).ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            InventoryGroups.Clear();
            int intShown = 0;
            foreach (InventoryKind kind in Enum.GetValues<InventoryKind>().Where(x => InventoryKind == null || x == InventoryKind.Value))
            {
                var items = _lstAllInventory.Where(x => x.Kind == kind && astrWords.All(w => x.SearchText.Contains(w))).ToList();
                if (items.Count == 0 && astrWords.Length > 0) continue;
                InventoryGroupViewModel objViewModel = new InventoryGroupViewModel(kind, items.OrderBy(x => x.SortOrder).ThenBy(x => x.Category, StringComparer.CurrentCulture).ThenBy(x => x.Name, StringComparer.CurrentCulture));
                intShown += objViewModel.Items.Count;
                InventoryGroups.Add(objViewModel);
            }
            foreach (InventoryFilterOption objOption in InventoryKinds)
                objOption.Refresh();
            InventorySummary = intShown.ToString(GlobalSettings.CultureInfo) + " / " + _lstAllInventory.Count.ToString(GlobalSettings.CultureInfo) + " ITEMS";
            OnInventoryRefreshed();
        }

        private void CharacterOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            OnUi(() => IsDirty = true);
            // Summary values are cheap to recompute; sections refresh when the user next opens them.
            QueueSummaryRefresh();
        }

        private void QueueSummaryRefresh()
        {
            if (_isCharacterDisposed) return;
            Interlocked.Exchange(ref _summaryRefreshPending, 1);
            if (Interlocked.CompareExchange(ref _summaryRefreshRunning, 1, 0) != 0) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    do
                    {
                        // A single equipment grant raises many dependent properties.
                        // Coalesce them instead of calculating a full build budget for each.
                        await Task.Delay(25).ConfigureAwait(false);
                        Interlocked.Exchange(ref _summaryRefreshPending, 0);
                        if (_isCharacterDisposed) return;
                        await RefreshSummary().ConfigureAwait(false);
                    } while (Volatile.Read(ref _summaryRefreshPending) != 0 && !_isCharacterDisposed);
                }
                catch (ObjectDisposedException) when (_isCharacterDisposed) { }
                finally
                {
                    Interlocked.Exchange(ref _summaryRefreshRunning, 0);
                    if (Volatile.Read(ref _summaryRefreshPending) != 0 && !_isCharacterDisposed) QueueSummaryRefresh();
                }
            });
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
            if (string.IsNullOrEmpty(Character.FileName))
            {
                await SaveAs();
                return;
            }
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
                    await Save();
                    return !IsDirty;
                case MessageResult.No:
                    return true;
                default:
                    return false;
            }
        }

        public async Task DisposeCharacterAsync()
        {
            _isCharacterDisposed = true;
            Character.PropertyChanged -= CharacterOnPropertyChanged;
            if (await CharacterRegistry.OpenCharacters.ContainsAsync(Character).ConfigureAwait(false))
                await CharacterRegistry.OpenCharacters.RemoveAsync(Character).ConfigureAwait(false);
            await Character.DisposeAsync().ConfigureAwait(false);
        }
    }

    public sealed class AttributeViewModel
    {
        private readonly CharacterViewModel _objOwner;
        private readonly CharacterAttrib _objAttribute;

        public AttributeViewModel(CharacterViewModel objOwner, CharacterAttrib objAttribute)
        {
            _objOwner = objOwner;
            _objAttribute = objAttribute;
            IsCareer = objOwner.Character.Created;
            IsEdge = objAttribute.Abbrev == "EDG";
            if (IsCareer)
            {
                CanImprove = objAttribute.CanUpgradeCareer;
                ImproveCost = objAttribute.UpgradeKarmaCost;
                ImproveTip = objAttribute.UpgradeToolTip;
                IsAtMaximum = objAttribute.TotalMaximum <= objAttribute.Value;
                KarmaAvailable = objOwner.Character.Karma;
            }
            else
            {
                BasePoints = objAttribute.Base;
                KarmaPoints = objAttribute.Karma;
                BaseUnlocked = objAttribute.BaseUnlocked;
            }
            ImproveCommand = new AsyncRelayCommand(() => RunAsync(t => Chummer.Rules.AttributeRules.ImproveCareerAsync(_objOwner.Character, _objAttribute, t)));
            BurnEdgeCommand = new AsyncRelayCommand(() => RunAsync(t => Chummer.Rules.AttributeRules.BurnEdgeAsync(_objAttribute, t)));
            RaiseCommand = new AsyncRelayCommand(() => RunAsync(t => Chummer.Rules.AttributeRules.RaiseCreateAsync(_objOwner.Character, _objAttribute, t)));
            LowerCommand = new AsyncRelayCommand(() => RunAsync(t => Chummer.Rules.AttributeRules.LowerCreateAsync(_objOwner.Character, _objAttribute, t)));

            Abbrev = objAttribute.Abbrev;
            Name = objAttribute.DisplayNameShort(GlobalSettings.Language);
            Value = objAttribute.Value;
            Total = objAttribute.TotalValue;
            Maximum = objAttribute.TotalMaximum;
            Display = objAttribute.DisplayValue;
            LongName = objAttribute.DisplayNameLong(GlobalSettings.Language);
            Minimum = objAttribute.TotalMinimum;
            AugmentedMaximum = objAttribute.TotalAugmentedMaximum;
            IsPhysical = Abbrev == "BOD" || Abbrev == "AGI" || Abbrev == "REA" || Abbrev == "STR";
            Breakdown = objAttribute.ToolTip;
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
        public string LongName { get; }
        public int Minimum { get; }
        public int AugmentedMaximum { get; }
        public bool IsPhysical { get; }
        public string Breakdown { get; } = string.Empty;
        public bool IsAugmented => Total != Value;
        public string ValueText => Value.ToString(GlobalSettings.CultureInfo);
        public string AugmentedText => "(" + Total.ToString(GlobalSettings.CultureInfo) + ")";
        public string RangeText => "range " + Minimum.ToString(GlobalSettings.CultureInfo) + "–" + Maximum.ToString(GlobalSettings.CultureInfo) + (AugmentedMaximum > Maximum ? "  //  aug. max " + AugmentedMaximum.ToString(GlobalSettings.CultureInfo) : string.Empty);
        /// <summary>Rating pips from 1 to the natural maximum; filled up to the natural value.</summary>
        public IReadOnlyList<bool> Pips => Enumerable.Range(1, Math.Max(1, Maximum)).Select(x => x <= Value).ToList();

        // ---------- editing ----------

        public bool IsCareer { get; }
        public bool IsCreate => !IsCareer;
        public bool IsEdge { get; }
        public bool CanBurnEdge => IsCareer && IsEdge;
        public bool CanImprove { get; }
        public int ImproveCost { get; }
        public bool IsAtMaximum { get; }
        public int KarmaAvailable { get; }
        public string ImproveBlockedText => IsAtMaximum ? "AT MAXIMUM" : "NEED " + ImproveCost.ToString(GlobalSettings.CultureInfo) + " KARMA";
        public string ImproveButtonText => CanImprove ? ImproveText : ImproveBlockedText;
        public string ImproveText => "+1  //  " + ImproveCost.ToString(GlobalSettings.CultureInfo) + " karma";
        public string ImproveTip { get; } = string.Empty;
        public int BasePoints { get; }
        public int KarmaPoints { get; }
        public bool BaseUnlocked { get; }
        public string PointsText => (BaseUnlocked ? "priority " + BasePoints.ToString(GlobalSettings.CultureInfo) + "  //  " : string.Empty) + "karma " + KarmaPoints.ToString(GlobalSettings.CultureInfo);
        public IAsyncRelayCommand ImproveCommand { get; }
        public IAsyncRelayCommand BurnEdgeCommand { get; }
        public IAsyncRelayCommand RaiseCommand { get; }
        public IAsyncRelayCommand LowerCommand { get; }
        public IAsyncRelayCommand QuickRaiseCommand => IsCareer ? ImproveCommand : RaiseCommand;
        public bool CanQuickRaise => !IsCareer || CanImprove;
        public string QuickRaiseTip => IsCareer
            ? CanImprove
                ? "Raise to " + (Value + 1).ToString(GlobalSettings.CultureInfo) + " for " + ImproveCost.ToString(GlobalSettings.CultureInfo) + " karma"
                : IsAtMaximum
                    ? "Already at maximum"
                    : "Raising to " + (Value + 1).ToString(GlobalSettings.CultureInfo) + " costs " + ImproveCost.ToString(GlobalSettings.CultureInfo) + " karma (you have " + KarmaAvailable.ToString(GlobalSettings.CultureInfo) + ")"
            : "Raise by 1";

        private Task RunAsync(Func<System.Threading.CancellationToken, Task<Chummer.Rules.RuleResult>> funcRule)
            => _objOwner.RunRuleAsync(funcRule);
    }

}

namespace Chummer.Desktop.ViewModels
{
    /// <summary>One box of a condition monitor; clicking marks damage up to it (clicking the last filled box heals one).</summary>
    public sealed class ConditionBoxViewModel
    {
        private readonly CharacterViewModel _objOwner;

        public ConditionBoxViewModel(CharacterViewModel objOwner, bool blnStun, int intIndex, bool blnFilled)
        {
            _objOwner = objOwner;
            IsStun = blnStun;
            Index = intIndex;
            IsFilled = blnFilled;
            ToggleCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(ToggleAsync);
        }

        public bool IsStun { get; }
        public int Index { get; }
        public bool IsFilled { get; }
        public bool IsThreshold => Index % 3 == 0;
        public string Tip => "-" + (Index / 3).ToString(GlobalSettings.CultureInfo) + " wound modifier at this box";
        public CommunityToolkit.Mvvm.Input.IRelayCommand ToggleCommand { get; }

        private Task ToggleAsync() => _objOwner.RunRuleAsync(async token =>
        {
            Character objCharacter = _objOwner.Character;
            int intCurrent = IsStun ? await objCharacter.GetStunCMFilledAsync(token).ConfigureAwait(false) : await objCharacter.GetPhysicalCMFilledAsync(token).ConfigureAwait(false);
            int intNew = intCurrent == Index ? Index - 1 : Index;
            if (IsStun)
                await objCharacter.SetStunCMFilledAsync(intNew, token).ConfigureAwait(false);
            else
                await objCharacter.SetPhysicalCMFilledAsync(intNew, token).ConfigureAwait(false);
            return Chummer.Rules.RuleResult.Changed;
        });
    }
}
