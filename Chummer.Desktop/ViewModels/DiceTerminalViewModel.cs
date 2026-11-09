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
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Chummer.Backend.Equipment;
using Chummer.Rolls;

namespace Chummer.Desktop.ViewModels
{
    /// <summary>
    /// "Roll for X": search any core action or house rule, click it, and the Roll20 formula for the selected
    /// runner (stats, skills, gear, modifiers) is on the clipboard.
    /// </summary>
    public sealed partial class DiceTerminalViewModel : ViewModelBase
    {
        private const string SettingsKeyName = "DiceTerminal";
        private readonly MainWindowViewModel _objShell;
        private readonly Dictionary<Character, RollCatalog> _dicCatalogs = new Dictionary<Character, RollCatalog>();
        private readonly HashSet<string> _setFavorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _lstRecent = new List<string>();
        private RollCatalog _objCatalog;
        private CancellationTokenSource _objSearchCancel;
        private bool _blnSuppressEvaluate;

        public DiceTerminalViewModel(MainWindowViewModel objShell)
        {
            _objShell = objShell;
            Formatter = new Roll20Formatter();
            LoadPreferences();
            objShell.OpenCharacters.CollectionChanged += OpenCharactersOnCollectionChanged;
        }

        public Roll20Formatter Formatter { get; }

        public RollCatalog Catalog => _objCatalog;

        /// <summary>Opens the terminal on one roll (from global search).</summary>
        public void ShowRoll(RollAction objAction)
        {
            _objShell.CurrentPage = this;
            Activate();
            Query = objAction.DisplayName;
            SelectedRoll = Results.FirstOrDefault(x => ReferenceEquals(x.Action, objAction)) ?? new RollItemViewModel(objAction, _setFavorites.Contains(objAction.Name));
        }

        public ObservableCollection<CharacterViewModel> Characters => _objShell.OpenCharacters;

        public ObservableCollection<RollItemViewModel> Results { get; } = new ObservableCollection<RollItemViewModel>();
        public ObservableCollection<RollItemViewModel> Recent { get; } = new ObservableCollection<RollItemViewModel>();

        public ObservableCollection<ContextOption> Weapons { get; } = new ObservableCollection<ContextOption>();
        public ObservableCollection<ContextOption> Vehicles { get; } = new ObservableCollection<ContextOption>();
        public ObservableCollection<ContextOption> SpellOptions { get; } = new ObservableCollection<ContextOption>();
        public ObservableCollection<ContextOption> Devices { get; } = new ObservableCollection<ContextOption>();
        public ObservableCollection<ContextOption> SpiritOptions { get; } = new ObservableCollection<ContextOption>();
        public ObservableCollection<OverrideViewModel> Overrides { get; } = new ObservableCollection<OverrideViewModel>();

        [ObservableProperty] private CharacterViewModel _selectedCharacter;
        [ObservableProperty] private string _query = string.Empty;
        [ObservableProperty] private RollItemViewModel _selectedRoll;
        [ObservableProperty] private bool _onlyHouseRules;
        [ObservableProperty] private bool _onlyFavorites;

        [ObservableProperty] private ContextOption _selectedWeapon;
        [ObservableProperty] private ContextOption _selectedVehicle;
        [ObservableProperty] private ContextOption _selectedSpell;
        [ObservableProperty] private decimal _spellForce = 1;
        [ObservableProperty] private ContextOption _selectedDevice;
        [ObservableProperty] private ContextOption _selectedSpirit;
        [ObservableProperty] private decimal _modifier;
        [ObservableProperty] private bool _useEdge;

        [ObservableProperty] private int _pool;
        [ObservableProperty] private string _breakdown = string.Empty;
        [ObservableProperty] private string _limitText = string.Empty;
        [ObservableProperty] private string _thresholdText = string.Empty;
        [ObservableProperty] private string _opposedText = string.Empty;
        [ObservableProperty] private string _formula = string.Empty;
        [ObservableProperty] private string _toast = string.Empty;
        [ObservableProperty] private string _resultCount = string.Empty;

        public bool HasCharacter => SelectedCharacter != null;
        public bool HasNoCharacter => SelectedCharacter == null;
        public bool HasSelection => SelectedRoll != null;
        public bool NeedsWeapon => SelectedRoll?.Action.Needs.HasFlag(RollNeeds.Weapon) == true;
        public bool NeedsVehicle => SelectedRoll?.Action.Needs.HasFlag(RollNeeds.Vehicle) == true;
        public bool NeedsSpell => SelectedRoll?.Action.Needs.HasFlag(RollNeeds.Spell) == true;
        public bool NeedsDevice => SelectedRoll?.Action.Needs.HasFlag(RollNeeds.MatrixDevice) == true;
        public bool NeedsSpirit => SelectedRoll?.Action.Needs.HasFlag(RollNeeds.Spirit) == true;
        public bool HasOverrides => Overrides.Count > 0;
        public bool HasLimit => LimitText.Length > 0;
        public bool HasThreshold => ThresholdText.Length > 0;
        public bool HasOpposed => OpposedText.Length > 0;
        public bool HasToast => Toast.Length > 0;
        public bool HasRecent => Recent.Count > 0;

        // ---------- lifecycle ----------

        public void Activate()
        {
            if (SelectedCharacter == null || !Characters.Contains(SelectedCharacter))
                SelectedCharacter = _objShell.CurrentPage as CharacterViewModel ?? Characters.FirstOrDefault();
        }

        private void OpenCharactersOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (SelectedCharacter != null && !Characters.Contains(SelectedCharacter))
            {
                _dicCatalogs.Remove(SelectedCharacter.Character);
                SelectedCharacter = Characters.FirstOrDefault();
            }
            else if (SelectedCharacter == null)
                SelectedCharacter = Characters.FirstOrDefault();
        }

        partial void OnSelectedCharacterChanged(CharacterViewModel value)
        {
            OnPropertyChanged(nameof(HasCharacter));
            OnPropertyChanged(nameof(HasNoCharacter));
            _ = LoadCharacterAsync(value);
        }

        private async Task LoadCharacterAsync(CharacterViewModel objViewModel)
        {
            Results.Clear();
            Recent.Clear();
            _objCatalog = null;
            if (objViewModel == null)
                return;
            Character objCharacter = objViewModel.Character;
            RollCatalog objCatalog;
            if (!_dicCatalogs.TryGetValue(objCharacter, out objCatalog))
            {
                objCatalog = await Task.Run(() => RollCatalog.Load(objCharacter)).ConfigureAwait(true);
                _dicCatalogs[objCharacter] = objCatalog;
            }
            if (!ReferenceEquals(SelectedCharacter, objViewModel))
                return;
            _objCatalog = objCatalog;
            List<ContextOption>[] alstOptions = await Task.Run(() => BuildContextOptions(objCharacter)).ConfigureAwait(true);
            _blnSuppressEvaluate = true;
            try
            {
                Reset(Weapons, alstOptions[0]);
                Reset(Vehicles, alstOptions[1]);
                Reset(SpellOptions, alstOptions[2]);
                Reset(Devices, alstOptions[3]);
                Reset(SpiritOptions, alstOptions[4]);
                SelectedWeapon = Weapons.FirstOrDefault(x => x.IsDefault) ?? Weapons.FirstOrDefault();
                SelectedVehicle = Vehicles.FirstOrDefault();
                SelectedSpell = SpellOptions.FirstOrDefault();
                SelectedDevice = Devices.FirstOrDefault(x => x.IsDefault) ?? Devices.FirstOrDefault();
                SelectedSpirit = SpiritOptions.FirstOrDefault();
                SpellForce = Math.Max(1, objCharacter.GetAttribute("MAG")?.TotalValue ?? 1);
            }
            finally
            {
                _blnSuppressEvaluate = false;
            }
            foreach (string strName in _lstRecent)
            {
                RollAction objAction = objCatalog.Find(strName);
                if (objAction != null)
                    Recent.Add(new RollItemViewModel(objAction, _setFavorites.Contains(objAction.Name)));
            }
            OnPropertyChanged(nameof(HasRecent));
            await SearchAsync().ConfigureAwait(true);
        }

        private static List<ContextOption>[] BuildContextOptions(Character objCharacter)
        {
            List<ContextOption> lstWeapons = objCharacter.Weapons.ToList()
                                                         .Select(x => new ContextOption(x.CurrentDisplayName, x, x.Equipped))
                                                         .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name, StringComparer.CurrentCulture).ToList();
            List<ContextOption> lstVehicles = objCharacter.Vehicles.ToList().Select(x => new ContextOption(x.CurrentDisplayName, x)).ToList();
            List<ContextOption> lstSpells = objCharacter.Spells.ToList().Select(x => new ContextOption(x.CurrentDisplayName, x))
                                                        .OrderBy(x => x.Name, StringComparer.CurrentCulture).ToList();
            IHasMatrixAttributes objActive = objCharacter.ActiveCommlink;
            List<ContextOption> lstDevices = objCharacter.Gear.ToList()
                                                         .DeepWhere(x => x.Children, x => x.IsCommlink)
                                                         .Select(x => new ContextOption(x.CurrentDisplayName, x, ReferenceEquals(x, objActive)))
                                                         .ToList();
            if (objActive != null && lstDevices.All(x => !ReferenceEquals(x.Value, objActive)))
                lstDevices.Insert(0, new ContextOption(objActive.ToString(), objActive, true));
            List<ContextOption> lstSpirits = objCharacter.Spirits.ToList().Select(x => new ContextOption(x.CurrentDisplayName, x)).ToList();
            return new[] { lstWeapons, lstVehicles, lstSpells, lstDevices, lstSpirits };
        }

        private static void Reset<T>(ObservableCollection<T> lstTarget, IEnumerable<T> lstItems)
        {
            lstTarget.Clear();
            foreach (T objItem in lstItems)
                lstTarget.Add(objItem);
        }

        // ---------- search ----------

        partial void OnQueryChanged(string value) => _ = SearchAsync();
        partial void OnOnlyHouseRulesChanged(bool value) => _ = SearchAsync();
        partial void OnOnlyFavoritesChanged(bool value) => _ = SearchAsync();

        private async Task SearchAsync()
        {
            _objSearchCancel?.Cancel();
            CancellationTokenSource objCancel = new CancellationTokenSource();
            _objSearchCancel = objCancel;
            RollCatalog objCatalog = _objCatalog;
            if (objCatalog == null)
            {
                Results.Clear();
                return;
            }
            string strQuery = Query ?? string.Empty;
            bool blnHouse = OnlyHouseRules;
            bool blnFavorites = OnlyFavorites;
            HashSet<string> setFavorites = new HashSet<string>(_setFavorites, StringComparer.OrdinalIgnoreCase);
            RollContext objContext = BuildContext(false);
            List<RollItemViewModel> lstResults;
            try
            {
                lstResults = await Task.Run(() =>
                {
                    List<RollItemViewModel> lstReturn = new List<RollItemViewModel>();
                    foreach (RollAction objAction in objCatalog.Search(strQuery))
                    {
                        objCancel.Token.ThrowIfCancellationRequested();
                        if (blnHouse && !objAction.IsHouseRule)
                            continue;
                        if (blnFavorites && !setFavorites.Contains(objAction.Name))
                            continue;
                        RollItemViewModel objItem = new RollItemViewModel(objAction, setFavorites.Contains(objAction.Name));
                        objItem.Preview(objContext);
                        lstReturn.Add(objItem);
                        if (lstReturn.Count >= 300)
                            break;
                    }
                    return lstReturn;
                }, objCancel.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (objCancel.IsCancellationRequested)
                return;
            Reset(Results, lstResults);
            ResultCount = lstResults.Count.ToString(CultureInfo.CurrentCulture) + " ROLLS";
        }

        // ---------- selection / evaluation ----------

        partial void OnSelectedRollChanged(RollItemViewModel value)
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(NeedsWeapon));
            OnPropertyChanged(nameof(NeedsVehicle));
            OnPropertyChanged(nameof(NeedsSpell));
            OnPropertyChanged(nameof(NeedsDevice));
            OnPropertyChanged(nameof(NeedsSpirit));
            Overrides.Clear();
            OnPropertyChanged(nameof(HasOverrides));
            Modifier = 0;
            UseEdge = false;
            Evaluate();
        }

        partial void OnSelectedWeaponChanged(ContextOption value) => Evaluate();
        partial void OnSelectedVehicleChanged(ContextOption value) => Evaluate();
        partial void OnSelectedSpellChanged(ContextOption value) => Evaluate();
        partial void OnSpellForceChanged(decimal value) => Evaluate();
        partial void OnSelectedDeviceChanged(ContextOption value) => Evaluate();
        partial void OnSelectedSpiritChanged(ContextOption value) => Evaluate();
        partial void OnModifierChanged(decimal value) => Evaluate();
        partial void OnUseEdgeChanged(bool value) => Evaluate();
        partial void OnLimitTextChanged(string value) => OnPropertyChanged(nameof(HasLimit));
        partial void OnThresholdTextChanged(string value) => OnPropertyChanged(nameof(HasThreshold));
        partial void OnOpposedTextChanged(string value) => OnPropertyChanged(nameof(HasOpposed));
        partial void OnToastChanged(string value) => OnPropertyChanged(nameof(HasToast));

        private RollContext BuildContext(bool blnWithOverrides)
        {
            Character objCharacter = SelectedCharacter?.Character;
            if (objCharacter == null)
                return null;
            RollCatalog objCatalog = _objCatalog;
            RollContext objContext = new RollContext(objCharacter)
            {
                Weapon = SelectedWeapon?.Value as Weapon,
                Vehicle = SelectedVehicle?.Value as Vehicle,
                Spell = SelectedSpell?.Value as Spell,
                SpellForce = (int)SpellForce,
                MatrixDevice = SelectedDevice?.Value as IHasMatrixAttributes,
                Spirit = SelectedSpirit?.Value as Spirit,
                ActionLookup = objCatalog == null ? (Func<string, RollAction>)null : objCatalog.Find,
            };
            if (blnWithOverrides)
            {
                foreach (OverrideViewModel objOverride in Overrides.Where(x => x.Value.HasValue))
                    objContext.Overrides[objOverride.Token] = (int)objOverride.Value.Value;
            }
            return objContext;
        }

        internal void Evaluate()
        {
            if (_blnSuppressEvaluate)
                return;
            RollItemViewModel objItem = SelectedRoll;
            RollContext objContext = BuildContext(true);
            if (objItem == null || objContext == null)
            {
                Formula = string.Empty;
                return;
            }
            RollEvaluation objEvaluation;
            try
            {
                objEvaluation = DiceExpressionEvaluator.EvaluateTest(objItem.Action, objContext);
            }
            catch (Exception ex)
            {
                Breakdown = "Could not evaluate: " + ex.Message;
                Formula = string.Empty;
                return;
            }
            // Offer an input for every token the engine could not resolve.
            foreach (string strUnknown in objEvaluation.Pool.Unknowns)
            {
                if (Overrides.All(x => x.Token != strUnknown))
                    Overrides.Add(new OverrideViewModel(this, strUnknown));
            }
            OnPropertyChanged(nameof(HasOverrides));
            int intEdge = objContext.Character.GetAttribute("EDG")?.TotalValue ?? 0;
            Pool = Math.Max(0, objEvaluation.Pool.Value + (int)Modifier + (UseEdge ? intEdge : 0));
            string strBreakdown = objEvaluation.Pool.Terms.Count > 0
                ? string.Join("  +  ", objEvaluation.Pool.Terms.Select(x => x.ToString()))
                : objEvaluation.Pool.Value.ToString(CultureInfo.CurrentCulture);
            if (Modifier != 0)
                strBreakdown += "  +  modifier " + ((int)Modifier).ToString("+0;-0", CultureInfo.CurrentCulture);
            if (UseEdge)
                strBreakdown += "  +  Edge " + intEdge.ToString(CultureInfo.CurrentCulture);
            Breakdown = strBreakdown;
            LimitText = UseEdge || objEvaluation.Limit == null ? string.Empty : objEvaluation.Limit.Value.ToString(CultureInfo.CurrentCulture);
            ThresholdText = objEvaluation.Threshold?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            OpposedText = objEvaluation.OpposedBy;
            Formula = Formatter.Format(objItem.Action, objEvaluation, SelectedCharacter.Name, UseEdge, intEdge, (int)Modifier);
        }

        // ---------- commands ----------

        /// <summary>Clicking a result selects it and immediately copies its formula.</summary>
        [RelayCommand]
        private async Task Roll(RollItemViewModel objItem)
        {
            if (objItem == null)
                return;
            if (!ReferenceEquals(SelectedRoll, objItem))
                SelectedRoll = objItem;
            await CopyAsync().ConfigureAwait(true);
        }

        [RelayCommand]
        private Task Copy() => CopyAsync();

        private async Task CopyAsync()
        {
            if (string.IsNullOrEmpty(Formula) || SelectedRoll == null)
                return;
            IClipboard objClipboard = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Clipboard;
            if (objClipboard != null)
                await objClipboard.SetTextAsync(Formula).ConfigureAwait(true);
            AddRecent(SelectedRoll.Action);
            string strToast = "COPIED  //  " + SelectedRoll.Name.ToUpper(CultureInfo.CurrentCulture) + "  //  " + Pool.ToString(CultureInfo.CurrentCulture) + "d6";
            Toast = strToast;
            await Task.Delay(2500).ConfigureAwait(true);
            if (Toast == strToast)
                Toast = string.Empty;
        }

        [RelayCommand]
        private void ToggleFavorite(RollItemViewModel objItem)
        {
            if (objItem == null)
                return;
            objItem.IsFavorite = !objItem.IsFavorite;
            if (objItem.IsFavorite)
                _setFavorites.Add(objItem.Action.Name);
            else
                _setFavorites.Remove(objItem.Action.Name);
            foreach (RollItemViewModel objOther in Results.Concat(Recent).Where(x => x.Action.Name == objItem.Action.Name))
                objOther.IsFavorite = objItem.IsFavorite;
            SavePreferences();
        }

        [RelayCommand]
        private void ClearQuery() => Query = string.Empty;

        private void AddRecent(RollAction objAction)
        {
            _lstRecent.RemoveAll(x => string.Equals(x, objAction.Name, StringComparison.OrdinalIgnoreCase));
            _lstRecent.Insert(0, objAction.Name);
            if (_lstRecent.Count > 12)
                _lstRecent.RemoveRange(12, _lstRecent.Count - 12);
            RollItemViewModel objExisting = Recent.FirstOrDefault(x => x.Action.Name == objAction.Name);
            if (objExisting != null)
                Recent.Remove(objExisting);
            Recent.Insert(0, objExisting ?? new RollItemViewModel(objAction, _setFavorites.Contains(objAction.Name)));
            while (Recent.Count > 12)
                Recent.RemoveAt(Recent.Count - 1);
            OnPropertyChanged(nameof(HasRecent));
            SavePreferences();
        }

        // ---------- preferences (favorites, recent, Roll20 templates) ----------

        private void LoadPreferences()
        {
            try
            {
                using (ISettingsKey objRoot = HostServices.Settings.OpenRoot())
                using (ISettingsKey objKey = objRoot?.OpenSubKey(SettingsKeyName))
                {
                    if (objKey == null)
                        return;
                    foreach (string strName in (objKey.GetValue("Favorites")?.ToString() ?? string.Empty).Split('\n').Where(x => x.Length > 0))
                        _setFavorites.Add(strName);
                    _lstRecent.AddRange((objKey.GetValue("Recent")?.ToString() ?? string.Empty).Split('\n').Where(x => x.Length > 0));
                    string strTemplate = objKey.GetValue("Template")?.ToString();
                    if (!string.IsNullOrWhiteSpace(strTemplate))
                        Formatter.Template = strTemplate;
                    string strEdgeTemplate = objKey.GetValue("EdgeTemplate")?.ToString();
                    if (!string.IsNullOrWhiteSpace(strEdgeTemplate))
                        Formatter.EdgeTemplate = strEdgeTemplate;
                }
            }
            catch (Exception ex)
            {
                Utils.BreakIfDebug();
                System.Diagnostics.Trace.TraceWarning("Dice Terminal preferences: " + ex.Message);
            }
        }

        private void SavePreferences()
        {
            try
            {
                using (ISettingsKey objRoot = HostServices.Settings.CreateRoot())
                using (ISettingsKey objKey = objRoot.CreateSubKey(SettingsKeyName))
                {
                    objKey.SetValue("Favorites", string.Join("\n", _setFavorites.OrderBy(x => x, StringComparer.Ordinal)));
                    objKey.SetValue("Recent", string.Join("\n", _lstRecent));
                    if (Formatter.Template != Roll20Formatter.DefaultTemplate)
                        objKey.SetValue("Template", Formatter.Template);
                    if (Formatter.EdgeTemplate != Roll20Formatter.DefaultEdgeTemplate)
                        objKey.SetValue("EdgeTemplate", Formatter.EdgeTemplate);
                }
            }
            catch (Exception ex)
            {
                Utils.BreakIfDebug();
                System.Diagnostics.Trace.TraceWarning("Dice Terminal preferences: " + ex.Message);
            }
        }
    }

    /// <summary>One search result.</summary>
    public sealed partial class RollItemViewModel : ObservableObject
    {
        public RollItemViewModel(RollAction objAction, bool blnFavorite)
        {
            Action = objAction;
            _isFavorite = blnFavorite;
        }

        public RollAction Action { get; }
        public string Name => Action.DisplayName;
        public string Tag => Action.IsHouseRule ? Action.Book.ToUpperInvariant() : (Action.Source + " " + Action.Page).Trim();
        public bool IsHouseRule => Action.IsHouseRule;
        public string Category => string.IsNullOrEmpty(Action.Category) ? string.Empty : Action.Category.ToUpperInvariant();
        public string Description => Action.Description;
        public string Notes => Action.Notes;
        public bool HasNotes => Action.Notes.Length > 0;
        public string Dice => DiceExpressionEvaluator.Describe(Action.Dice);
        public string IconKey => Action.Needs.HasFlag(RollNeeds.Weapon) ? "Icon.Weapon"
            : Action.Needs.HasFlag(RollNeeds.Vehicle) ? "Icon.Vehicle"
            : Action.Needs.HasFlag(RollNeeds.Spell) ? "Icon.Spell"
            : Action.Needs.HasFlag(RollNeeds.Spirit) ? "Icon.Spirit"
            : Action.Needs.HasFlag(RollNeeds.MatrixDevice) || Action.Category.Equals("Matrix", StringComparison.OrdinalIgnoreCase) ? "Icon.Cyberdeck"
            : "Icon.Dice";

        [ObservableProperty] private bool _isFavorite;
        [ObservableProperty] private string _poolPreview = string.Empty;
        [ObservableProperty] private bool _isIncomplete;

        internal void Preview(RollContext objContext)
        {
            if (objContext == null)
                return;
            try
            {
                RollEvaluation objEvaluation = DiceExpressionEvaluator.EvaluateTest(Action, objContext);
                PoolPreview = Math.Max(0, objEvaluation.Pool.Value).ToString(CultureInfo.InvariantCulture) + "d6";
                IsIncomplete = objEvaluation.Pool.Unknowns.Count > 0;
            }
            catch (Exception)
            {
                PoolPreview = "?";
                IsIncomplete = true;
            }
        }
    }

    /// <summary>An item the roll can be made with (weapon, vehicle, spell, device, spirit).</summary>
    public sealed class ContextOption
    {
        public ContextOption(string strName, object objValue, bool blnDefault = false)
        {
            Name = strName;
            Value = objValue;
            IsDefault = blnDefault;
        }

        public string Name { get; }
        public object Value { get; }
        public bool IsDefault { get; }

        public override string ToString() => Name;
    }

    /// <summary>A value the engine cannot know (target's stats, situational numbers), typed in by the player.</summary>
    public sealed partial class OverrideViewModel : ObservableObject
    {
        private readonly DiceTerminalViewModel _objOwner;

        public OverrideViewModel(DiceTerminalViewModel objOwner, string strToken)
        {
            _objOwner = objOwner;
            Token = strToken;
        }

        public string Token { get; }
        public string Label => DiceExpressionEvaluator.Describe(Token);

        [ObservableProperty] private decimal? _value;

        partial void OnValueChanged(decimal? value) => _objOwner.Evaluate();
    }
}
