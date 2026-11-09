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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Backend.Equipment;
using Chummer.Rules;
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
        public bool IsCareerMode => Character.Created;
        public bool IsNotesDraftChanged => SelectedInventoryItem != null && (InventoryNotesDraft ?? string.Empty) != SelectedInventoryItem.Notes;

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

        /// <summary>"+ ADD" slot of a category. Buying new items is wired up separately (implement <see cref="OnAddItemRequested"/>).</summary>
        [RelayCommand]
        private void AddItem(Chummer.Desktop.ViewModels.InventoryKind eKind) => OnAddItemRequested(eKind);

        partial void OnAddItemRequested(Chummer.Desktop.ViewModels.InventoryKind eKind);

        // ---------- session actions on the selected item ----------

        private async Task RunItemRuleAsync(Func<object, CancellationToken, Task<RuleResult>> funcRule)
        {
            object objItem = SelectedInventoryItem?.Item;
            if (objItem == null)
                return;
            RuleResult eResult;
            try
            {
                eResult = await Task.Run(() => funcRule(objItem, CancellationToken.None)).ConfigureAwait(true);
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
    }
}
