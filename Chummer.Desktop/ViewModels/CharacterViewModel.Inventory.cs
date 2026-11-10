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
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Backend.Equipment;
using Chummer.Rules;
using Chummer.Selection;
using Chummer.Desktop.Views;
using Chummer.Desktop.Views.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chummer.Desktop.ViewModels
{
    /// <summary>
    /// Inventory screen: selected slot + detail panel, the equipped "loadout" strip and the session actions (InventoryRules).
    /// </summary>
    public sealed partial class CharacterViewModel
    {
        [ObservableProperty] private InventoryItemViewModel _selectedInventoryItem;
        [ObservableProperty] private string _inventoryNotesDraft = string.Empty;
        [ObservableProperty] private bool _isSellPromptOpen;
        [ObservableProperty] private decimal _sellPercent = 50;

        /// <summary>Equipped weapons, worn armor and the active commlink, shown above the grid.</summary>
        public ObservableCollection<InventoryItemViewModel> LoadoutSlots { get; } = new ObservableCollection<InventoryItemViewModel>();

        public bool HasLoadout => LoadoutSlots.Count > 0;
        public bool HasSelectedInventoryItem => SelectedInventoryItem != null;
        public bool HasNoSelectedInventoryItem => SelectedInventoryItem == null;
        public bool IsNotesDraftChanged => SelectedInventoryItem != null && (InventoryNotesDraft ?? string.Empty) != SelectedInventoryItem.Notes;
        public ObservableCollection<EquipmentDamageBox> EquipmentPhysicalBoxes { get; } = new ObservableCollection<EquipmentDamageBox>();
        public ObservableCollection<EquipmentDamageBox> EquipmentMatrixBoxes { get; } = new ObservableCollection<EquipmentDamageBox>();
        public bool HasEquipmentPhysicalMonitor => EquipmentPhysicalBoxes.Count > 0;
        public bool HasEquipmentMatrixMonitor => EquipmentMatrixBoxes.Count > 0;
        public string EquipmentPhysicalLabel => "PHYSICAL DAMAGE " + EquipmentPhysicalBoxes.Count(x => x.IsFilled) + " / " + EquipmentPhysicalBoxes.Count;
        public string EquipmentMatrixLabel => "MATRIX DAMAGE " + EquipmentMatrixBoxes.Count(x => x.IsFilled) + " / " + EquipmentMatrixBoxes.Count;

        partial void OnSelectedInventoryItemChanged(InventoryItemViewModel oldValue, InventoryItemViewModel newValue)
        {
            if (oldValue != null)
                oldValue.IsSelected = false;
            if (newValue != null)
                newValue.IsSelected = true;
            InventoryNotesDraft = newValue?.Notes ?? string.Empty;
            IsSellPromptOpen = false;
            OnPropertyChanged(nameof(HasSelectedInventoryItem));
            OnPropertyChanged(nameof(HasNoSelectedInventoryItem));
            OnPropertyChanged(nameof(IsNotesDraftChanged));
            EquipmentPhysicalBoxes.Clear(); EquipmentMatrixBoxes.Clear();
            if (newValue != null)
            {
                for (int i = 1; i <= newValue.PhysicalConditionMax; ++i)
                    EquipmentPhysicalBoxes.Add(new EquipmentDamageBox(this, true, i, i <= newValue.PhysicalConditionFilled));
                for (int i = 1; i <= newValue.MatrixConditionMax; ++i)
                    EquipmentMatrixBoxes.Add(new EquipmentDamageBox(this, false, i, i <= newValue.MatrixConditionFilled));
            }
            OnPropertyChanged(nameof(HasEquipmentPhysicalMonitor)); OnPropertyChanged(nameof(HasEquipmentMatrixMonitor));
            OnPropertyChanged(nameof(EquipmentPhysicalLabel)); OnPropertyChanged(nameof(EquipmentMatrixLabel));
        }

        partial void OnInventoryNotesDraftChanged(string value) => OnPropertyChanged(nameof(IsNotesDraftChanged));

        /// <summary>Called after the grid is rebuilt: refresh the loadout strip and keep the same item selected.</summary>
        private void OnInventoryRefreshed()
        {
            LoadoutSlots.Clear();
            foreach (InventoryItemViewModel objItem in _lstAllInventory.Where(x => x.Kind == Chummer.Desktop.ViewModels.InventoryKind.Weapon && x.CanEquip && x.IsEquipped))
                LoadoutSlots.Add(objItem);
            foreach (InventoryItemViewModel objItem in _lstAllInventory.Where(x => x.Kind == Chummer.Desktop.ViewModels.InventoryKind.Armor && x.IsEquipped))
                LoadoutSlots.Add(objItem);
            InventoryItemViewModel objCommlink = _lstAllInventory.SelectMany(x => x.SelfAndDescendants()).FirstOrDefault(x => x.IsActiveCommlink);
            if (objCommlink != null && !LoadoutSlots.Contains(objCommlink))
                LoadoutSlots.Add(objCommlink);
            OnPropertyChanged(nameof(HasLoadout));

            string strId = SelectedInventoryItem?.Id;
            InventoryItemViewModel objNew = null;
            if (!string.IsNullOrEmpty(strId))
                objNew = _lstAllInventory.SelectMany(x => x.SelfAndDescendants()).FirstOrDefault(x => x.Id == strId);
            if (objNew == null)
                objNew = InventoryGroups.SelectMany(x => x.Items).FirstOrDefault();
            if (SelectedInventoryItem != objNew)
                SelectedInventoryItem = objNew;
            else if (objNew != null)
                objNew.IsSelected = true;
        }

        [RelayCommand]
        private void SelectInventoryItem(InventoryItemViewModel objItem)
        {
            if (objItem != null)
                SelectedInventoryItem = objItem;
        }

        /// <summary>Open the store for this inventory category.</summary>
        [RelayCommand]
        private Task AddItem(InventoryKind eKind) => PurchaseAsync(eKind);

        // ---------- session actions on the selected item ----------

        private Task RunItemRuleAsync(Func<object, CancellationToken, Task<RuleResult>> funcRule)
        {
            object objItem = SelectedInventoryItem?.Item;
            return objItem == null ? Task.CompletedTask : RunRuleAsync(t => funcRule(objItem, t));
        }

        internal Task MarkEquipmentDamageAsync(bool physical, int index) => RunItemRuleAsync((item, _) =>
        {
            int current = physical ? (item as Vehicle)?.PhysicalCMFilled ?? 0 : (item as IHasMatrixAttributes)?.MatrixCMFilled ?? 0;
            return Task.FromResult(EquipmentConditionRules.SetDamage(item, physical, current == index ? index - 1 : index));
        });

        [RelayCommand]
        private Task ToggleEquipped()
        {
            bool blnNew = !(SelectedInventoryItem?.IsEquipped ?? true);
            return RunItemRuleAsync((x, t) => InventoryRules.SetEquippedAsync(Character, x, blnNew, t));
        }

        [RelayCommand]
        private Task ToggleWireless()
        {
            bool blnNew = !(SelectedInventoryItem?.IsWirelessOn ?? true);
            return RunItemRuleAsync((x, t) => Task.FromResult(InventoryRules.SetWireless(x, blnNew)));
        }

        [RelayCommand]
        private Task ToggleActiveCommlink()
        {
            bool blnNew = !(SelectedInventoryItem?.IsActiveCommlink ?? true);
            return RunItemRuleAsync((x, t) => InventoryRules.SetActiveCommlinkAsync(Character, x, blnNew, t));
        }

        [RelayCommand]
        private Task FireWeapon(FireModeOption objMode)
            => RunItemRuleAsync((x, t) => x is Weapon objWeapon
                                     ? objMode == null ? InventoryRules.FireDefaultAsync(objWeapon, t) : InventoryRules.FireAsync(objWeapon, objMode.Mode, t)
                                     : Task.FromResult(RuleResult.NotAllowed));

        [RelayCommand]
        private Task ReloadWeapon()
            => RunItemRuleAsync((x, t) => x is Weapon objWeapon ? InventoryRules.ReloadAsync(Character, objWeapon, t) : Task.FromResult(RuleResult.NotAllowed));

        [RelayCommand]
        private Task UnloadWeapon()
            => RunItemRuleAsync((x, t) => x is Weapon objWeapon ? InventoryRules.UnloadAsync(Character, objWeapon, t) : Task.FromResult(RuleResult.NotAllowed));

        /// <summary>Use up one (medkit charge, dose, grenade, round...).</summary>
        [RelayCommand]
        private Task UseOne() => RunItemRuleAsync((x, t) => InventoryRules.ReduceQuantityAsync(x, 1, t));

        [RelayCommand]
        private Task ReduceQuantity() => RunItemRuleAsync((x, t) => InventoryRules.PromptReduceQuantityAsync(Character, x, t));

        [RelayCommand]
        private Task AddQuantity()
            => RunItemRuleAsync((x, t) => x is Gear objGear
                                     ? InventoryRules.AddQuantityAsync(Character, objGear, objGear.CostFor > 0 ? objGear.CostFor : 1, t)
                                     : x is Drug drug ? CustomDrugRules.BuyAdditionalDoseAsync(Character, drug, t)
                                     : Task.FromResult(RuleResult.NotAllowed));

        [RelayCommand]
        private Task DamageArmor() => RunItemRuleAsync((x, t) => Task.FromResult(InventoryRules.ChangeArmorDamage(x as Armor, 1)));

        [RelayCommand]
        private Task RepairArmor() => RunItemRuleAsync((x, t) => Task.FromResult(InventoryRules.ChangeArmorDamage(x as Armor, -1)));

        [RelayCommand]
        private Task RenameItem() => RunItemRuleAsync((x, t) => InventoryRules.PromptRenameAsync(x, t));

        [RelayCommand]
        private Task SaveItemNotes()
        {
            string strNotes = InventoryNotesDraft ?? string.Empty;
            return RunItemRuleAsync((x, t) => InventoryRules.SetNotesAsync(x, strNotes, t));
        }

        [RelayCommand]
        private void OpenSellPrompt()
        {
            SellPercent = 50;
            IsSellPromptOpen = true;
        }

        [RelayCommand]
        private void CancelSell() => IsSellPromptOpen = false;

        [RelayCommand]
        private Task ConfirmSell()
        {
            decimal decPercent = SellPercent;
            IsSellPromptOpen = false;
            return RunItemRuleAsync((x, t) => InventoryRules.SellAsync(x, decPercent, t));
        }

        [RelayCommand]
        private Task RemoveItem() => RunItemRuleAsync((x, t) => InventoryRules.RemoveAsync(x, t));

        [RelayCommand]
        private Task CreateNaturalWeapon() => DialogHost.ShowModalAsync(new NaturalWeaponWindow(this), null);

        [RelayCommand]
        private Task ManageCyberwareSuites() => DialogHost.ShowModalAsync(new CyberwareSuiteWindow(this), null);

        [RelayCommand]
        private Task CreateCustomDrug() => DialogHost.ShowModalAsync(new CustomDrugWindow(this), null);

        [RelayCommand]
        private async Task AddWeaponMount()
        {
            if (!(SelectedInventoryItem?.Item is Vehicle vehicle)) return;
            var window = new WeaponMountWindow(Character, vehicle, async configuration =>
            {
                PurchaseResult result = null;
                await RunRuleAsync(async token =>
                {
                    result = await WeaponMountRules.PurchaseAsync(Character, vehicle, configuration, token);
                    return result.Result;
                });
                return result ?? new PurchaseResult(RuleResult.Cancelled);
            });
            await DialogHost.ShowModalAsync(window, null);
        }

        [RelayCommand]
        private async Task AddMountedWeapon()
        {
            if (!(SelectedInventoryItem?.Item is WeaponMount mount)) return;
            await StoreWindow.ShowAsync(null, Character, new[] { new StoreSection(EquipmentKind.Weapon, "Mounted weapons", mount) }, null, ExecuteStorePurchaseAsync);
        }

        [RelayCommand]
        private async Task EditWeaponMount()
        {
            if (!(SelectedInventoryItem?.Item is WeaponMount mount) || mount.IncludedInVehicle) return;
            var window = new WeaponMountWindow(Character, mount.Parent, async configuration =>
            {
                PurchaseResult result = null;
                await RunRuleAsync(async token => { result = await WeaponMountRules.EditAsync(Character, mount, configuration, token); return result.Result; });
                return result ?? new PurchaseResult(RuleResult.Cancelled);
            }, mount);
            await DialogHost.ShowModalAsync(window, null);
        }

        [RelayCommand]
        private async Task SetItemLocation()
        {
            object item = SelectedInventoryItem?.Item;
            if (item == null) return;
            var locations = await Task.Run(() => EquipmentLocationRules.LocationsFor(Character, item)?.ToList());
            if (locations == null) return;
            var options = new List<ListItem> { new ListItem("", "No location"), new ListItem("new", "Create a new location…") };
            options.AddRange(locations.Select(x => new ListItem(x.InternalId, x.Name)));
            string current = (item as IHasLocation)?.Location?.InternalId ?? "";
            var choice = await PromptWindow.ChooseAsync(null, "Item location", options, current);
            if (!choice.HasValue) return;
            string selected = choice.Value.Value?.ToString() ?? "";
            if (selected == "new")
            {
                string name = await PromptWindow.AskTextAsync(null, "New location name", "");
                if (string.IsNullOrWhiteSpace(name)) return;
                await RunRuleAsync(async t =>
                {
                    var location = await EquipmentLocationRules.CreateAsync(Character, item, name, t);
                    return location == null ? RuleResult.NotAllowed : await EquipmentLocationRules.AssignAsync(Character, item, location, t);
                });
            }
            else await RunRuleAsync(t => EquipmentLocationRules.AssignAsync(Character, item, locations.FirstOrDefault(x => x.InternalId == selected), t));
        }

        [RelayCommand]
        private async Task RenameItemLocation()
        {
            object item = SelectedInventoryItem?.Item;
            var location = (item as IHasLocation)?.Location;
            if (location == null) return;
            string name = await PromptWindow.AskTextAsync(null, "Rename location", location.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            await RunRuleAsync(_ => Task.FromResult(EquipmentLocationRules.Rename(Character, item, location, name)));
        }

        [RelayCommand]
        private Task RemoveItemLocation() => RunItemRuleAsync((item, t) => EquipmentLocationRules.RemoveAsync(Character, item, (item as IHasLocation)?.Location, t));
    }

    public sealed class EquipmentDamageBox
    {
        public EquipmentDamageBox(CharacterViewModel owner, bool physical, int index, bool filled)
        {
            Index = index; IsFilled = filled;
            ToggleCommand = new AsyncRelayCommand(() => owner.MarkEquipmentDamageAsync(physical, index));
        }
        public int Index { get; }
        public bool IsFilled { get; }
        public string Tip => "Mark " + Index + " damage boxes; click the last filled box to heal one.";
        public IAsyncRelayCommand ToggleCommand { get; }
    }
}
