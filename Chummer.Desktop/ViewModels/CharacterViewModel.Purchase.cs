/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using System.Collections.Generic;
using System.Threading.Tasks;
using Chummer.Backend.Equipment;
using Chummer.Desktop.Views.Dialogs;
using Chummer.Rules;
using Chummer.Selection;
using InvKind = Chummer.Desktop.ViewModels.InventoryKind;
using CommunityToolkit.Mvvm.Input;

namespace Chummer.Desktop.ViewModels
{
    /// <summary>
    /// Buying equipment between sessions: the store window for every legacy "Add ..." button (cmdAddWeapon, cmdAddArmor,
    /// cmdAddGear, cmdAddCyberware/Bioware, cmdAddVehicle, cmdAddLifestyle, premade drugs) and for mods / accessories /
    /// plugins on an owned item, plus paying lifestyle rent.
    /// </summary>
    public sealed partial class CharacterViewModel
    {
        [RelayCommand]
        private Task AddSelectedMod() => AddModAsync(SelectedInventoryItem?.Item);

        [RelayCommand]
        private Task PaySelectedRent() => PayRentAsync(SelectedInventoryItem?.Item);

        [RelayCommand]
        private Task DecreaseSelectedRent() => DecreaseRentAsync(SelectedInventoryItem?.Item);

        /// <summary>Store sections opened for an inventory tab.</summary>
        public static IReadOnlyList<StoreSection> SectionsFor(InvKind eKind)
        {
            switch (eKind)
            {
                case InvKind.Weapon:
                    return new[] { new StoreSection(EquipmentKind.Weapon, "Weapons") };
                case InvKind.Armor:
                    return new[] { new StoreSection(EquipmentKind.Armor, "Armor") };
                case InvKind.Cyberware:
                    return new[] { new StoreSection(EquipmentKind.Cyberware, "Cyberware"), new StoreSection(EquipmentKind.Bioware, "Bioware") };
                case InvKind.Vehicle:
                    return new[] { new StoreSection(EquipmentKind.Vehicle, "Vehicles & drones") };
                case InvKind.Drug:
                    return new[] { new StoreSection(EquipmentKind.Drug, "Drugs") };
                case InvKind.Lifestyle:
                    return new[] { new StoreSection(EquipmentKind.Lifestyle, "Lifestyles") };
                default:
                    return new[] { new StoreSection(EquipmentKind.Gear, "Gear") };
            }
        }

        /// <summary>Store section for adding something to an owned item, or null when the item takes no add-ons.</summary>
        public static StoreSection ModSectionFor(object objParentItem)
        {
            switch (objParentItem)
            {
                case Weapon objWeapon:
                    return new StoreSection(EquipmentKind.WeaponAccessory, "Weapon accessories", objWeapon);
                case Armor objArmor:
                    return new StoreSection(EquipmentKind.ArmorMod, "Armor mods", objArmor);
                case Gear objGear:
                    return new StoreSection(EquipmentKind.GearPlugin, "Plugins", objGear);
                case Vehicle objVehicle:
                    return new StoreSection(EquipmentKind.VehicleMod, "Vehicle mods", objVehicle);
                case WeaponMount mount:
                    return new StoreSection(EquipmentKind.WeaponMountMod, "Weapon mount modifications", mount);
                case Cyberware objCyberware:
                    return new StoreSection(objCyberware.SourceType == Improvement.ImprovementSource.Bioware ? EquipmentKind.Bioware : EquipmentKind.Cyberware,
                                            "Subsystems", objCyberware);
                default:
                    return null;
            }
        }

        /// <summary>Open the store for one inventory tab; the sheet refreshes after every purchase.</summary>
        public async Task PurchaseAsync(InvKind eKind)
        {
            await StoreWindow.ShowAsync(null, Character, SectionsFor(eKind), null, ExecuteStorePurchaseAsync).ConfigureAwait(true);
        }

        /// <summary>Open the store for mods / accessories / plugins of an owned item (weapon, armor, gear, vehicle, cyberware).</summary>
        public async Task AddModAsync(object objParentItem)
        {
            StoreSection objSection = ModSectionFor(objParentItem);
            if (objSection == null)
                return;
            await StoreWindow.ShowAsync(null, Character, new[] { objSection }, null, ExecuteStorePurchaseAsync).ConfigureAwait(true);
        }

        /// <summary>Career: pay one more month of rent for a lifestyle (legacy cmdIncreaseLifestyleMonths_Click).</summary>
        public async Task PayRentAsync(object objLifestyle)
        {
            if (!(objLifestyle is Lifestyle objCast))
                return;
            if (await Task.Run(() => PurchaseRules.PayLifestyleMonthAsync(Character, objCast)).ConfigureAwait(true) != RuleResult.Changed)
                return;
            MarkDirty();
            Reload();
        }

        /// <summary>Career: one month less on a lifestyle (legacy cmdDecreaseLifestyleMonths_Click).</summary>
        public async Task DecreaseRentAsync(object objLifestyle)
        {
            if (!(objLifestyle is Lifestyle objCast) || objCast.Increments <= 0)
                return;
            if (await Task.Run(() => PurchaseRules.DecreaseLifestyleMonthAsync(Character, objCast)).ConfigureAwait(true) != RuleResult.Changed)
                return;
            MarkDirty();
            Reload();
        }

        private async Task<PurchaseResult> ExecuteStorePurchaseAsync(PurchaseRequest request)
        {
            PurchaseResult result = null;
            await RunRuleAsync(async token =>
            {
                result = await PurchaseRules.PurchaseAsync(Character, request, token);
                return result.Result;
            });
            return result ?? new PurchaseResult(RuleResult.Cancelled);
        }
    }
}
