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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Chummer.Backend.Enums;
using Chummer.Backend.Equipment;
using Chummer.Selection;

namespace Chummer.Rules
{
    /// <summary>What the store's options panel chose for one purchase (the legacy picker form's public properties).</summary>
    public sealed class PurchaseRequest
    {
        public EquipmentKind Kind { get; set; }

        /// <summary>Data id (or name) of the item in its data file.</summary>
        public string ItemId { get; set; } = string.Empty;

        public int Rating { get; set; }
        public decimal Quantity { get; set; } = 1;
        public bool Free { get; set; }
        public bool BlackMarket { get; set; }

        /// <summary>Street markup in percent (career mode only).</summary>
        public decimal Markup { get; set; }

        /// <summary>Cyberware / bioware / drug grade (SourceIDString of the Grade).</summary>
        public string GradeId { get; set; } = string.Empty;

        /// <summary>Gear: "Do It Yourself" half price for Commlink Upgrades.</summary>
        public bool DoItYourself { get; set; }

        /// <summary>Gear (career): merge into an identical item the character already carries.</summary>
        public bool Stack { get; set; }

        /// <summary>Cyberware essence discount in percent (only when the settings allow it).</summary>
        public int EssenceDiscount { get; set; }

        /// <summary>Owner for mods / accessories / plugins (Weapon, Armor, Gear, Vehicle, Cyberware).</summary>
        public object Parent { get; set; }

        public Location Location { get; set; }

        /// <summary>Weapon accessory mount and extra mount (legacy SelectWeaponAccessory.SelectedMount).</summary>
        public string Mount { get; set; } = string.Empty;
        public string ExtraMount { get; set; } = string.Empty;

        /// <summary>Vehicles: bought used, with the discount in percent (20-50, legacy nudUsedVehicleDiscount).</summary>
        public bool UsedVehicle { get; set; }
        public decimal UsedVehicleDiscount { get; set; } = 20;

        /// <summary>Lifestyles.</summary>
        public string LifestyleName { get; set; } = string.Empty;
        public decimal Percentage { get; set; } = 100;
        public int Roommates { get; set; }
        public bool SplitCostWithRoommates { get; set; }
        public bool TrustFund { get; set; }
    }

    public sealed class PurchaseResult
    {
        public PurchaseResult(RuleResult eResult, object objItem = null, decimal decCost = 0, ExpenseLogEntry objExpense = null)
        {
            Result = eResult;
            Item = objItem;
            Cost = decCost;
            Expense = objExpense;
        }

        public RuleResult Result { get; }

        /// <summary>The item that was added (or the existing stack that grew).</summary>
        public object Item { get; }

        /// <summary>Nuyen deducted (career mode).</summary>
        public decimal Cost { get; }

        public ExpenseLogEntry Expense { get; }

        public bool Succeeded => Result == RuleResult.Changed;
    }

    /// <summary>
    /// Buying equipment, lifted from the legacy CharacterCareer / CharacterCreate purchase handlers (PickGear, PickWeapon,
    /// PickArmor, PickCyberware, AddVehicle, cmdAddLifestyle_Click, AddPremadeDrugAsync, tsWeaponAddAccessory_Click,
    /// tsAddArmorMod_Click, tsVehicleAddMod_Click). Career mode checks and deducts nuyen and writes the same expense entry
    /// (String_ExpensePurchase*) with the same undo data; create mode adds the item and lets the build budget pick it up.
    /// </summary>
    public static class PurchaseRules
    {
        public static Task<PurchaseResult> PurchaseAsync(Character objCharacter, PurchaseRequest objRequest, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objRequest == null)
                throw new ArgumentNullException(nameof(objRequest));
            switch (objRequest.Kind)
            {
                case EquipmentKind.Gear:
                case EquipmentKind.GearPlugin:
                    return PurchaseGearAsync(objCharacter, objRequest, token);
                case EquipmentKind.Weapon:
                    return PurchaseWeaponAsync(objCharacter, objRequest, token);
                case EquipmentKind.Armor:
                    return PurchaseArmorAsync(objCharacter, objRequest, token);
                case EquipmentKind.Cyberware:
                case EquipmentKind.Bioware:
                    return PurchaseCyberwareAsync(objCharacter, objRequest, token);
                case EquipmentKind.Vehicle:
                    return PurchaseVehicleAsync(objCharacter, objRequest, token);
                case EquipmentKind.Drug:
                    return PurchaseDrugAsync(objCharacter, objRequest, token);
                case EquipmentKind.Lifestyle:
                    return PurchaseLifestyleAsync(objCharacter, objRequest, token);
                case EquipmentKind.WeaponAccessory:
                    return PurchaseWeaponAccessoryAsync(objCharacter, objRequest, token);
                case EquipmentKind.ArmorMod:
                    return PurchaseArmorModAsync(objCharacter, objRequest, token);
                case EquipmentKind.VehicleMod:
                    return PurchaseVehicleModAsync(objCharacter, objRequest, token);
                case EquipmentKind.WeaponMountMod:
                    return WeaponMountRules.PurchaseModAsync(objCharacter, objRequest, token);
                default:
                    throw new ArgumentOutOfRangeException(nameof(objRequest));
            }
        }

        #region Shared helpers

        /// <summary>Markup, then the restricted / forbidden multipliers from the character's settings (legacy order).</summary>
        public static async Task<decimal> StreetCostAsync(Character objCharacter, decimal decCost, decimal decMarkup, char chrAvailSuffix, CancellationToken token = default)
        {
            if (decMarkup != 0)
                decCost *= 1 + decMarkup / 100.0m;
            CharacterSettings objSettings = await objCharacter.GetSettingsAsync(token).ConfigureAwait(false);
            switch (chrAvailSuffix)
            {
                case 'R' when objSettings.MultiplyRestrictedCost:
                    decCost *= objSettings.RestrictedCostMultiplier;
                    break;
                case 'F' when objSettings.MultiplyForbiddenCost:
                    decCost *= objSettings.ForbiddenCostMultiplier;
                    break;
            }
            return decCost;
        }

        internal static async Task<bool> CheckNuyenAsync(Character objCharacter, decimal decCost, CancellationToken token)
        {
            if (decCost <= await objCharacter.GetNuyenAsync(token).ConfigureAwait(false))
                return true;
            await MessageBoxes.ShowAsync(
                await LanguageManager.GetStringAsync("Message_NotEnoughNuyen", token: token).ConfigureAwait(false),
                await LanguageManager.GetStringAsync("MessageTitle_NotEnoughNuyen", token: token).ConfigureAwait(false),
                MessageButtons.OK, MessageIcon.Information, token: token).ConfigureAwait(false);
            return false;
        }

        internal static async Task ShowCapacityReachedAsync(CancellationToken token)
        {
            await MessageBoxes.ShowAsync(
                await LanguageManager.GetStringAsync("Message_CapacityReached", token: token).ConfigureAwait(false),
                await LanguageManager.GetStringAsync("MessageTitle_CapacityReached", token: token).ConfigureAwait(false),
                MessageButtons.OK, MessageIcon.Information, token: token).ConfigureAwait(false);
        }

        /// <summary>The expense entry every legacy purchase handler writes: "&lt;String_ExpensePurchase…&gt; &lt;item&gt;", amount -cost, nuyen undo.</summary>
        internal static async Task<ExpenseLogEntry> ChargeAsync(Character objCharacter, decimal decCost, string strReasonKey, string strItemName,
                                                               NuyenExpenseType eUndoType, string strObjectId, decimal decQty, CancellationToken token)
        {
            ExpenseLogEntry objExpense = new ExpenseLogEntry(objCharacter);
            objExpense.Create(decCost * -1,
                await LanguageManager.GetStringAsync(strReasonKey, token: token).ConfigureAwait(false)
                + await LanguageManager.GetStringAsync("String_Space", token: token).ConfigureAwait(false)
                + strItemName,
                ExpenseType.Nuyen, DateTime.Now);
            await objCharacter.ExpenseEntries.AddWithSortAsync(objExpense, token: token).ConfigureAwait(false);
            await objCharacter.ModifyNuyenAsync(-decCost, token).ConfigureAwait(false);
            ExpenseUndo objUndo = new ExpenseUndo();
            objUndo.CreateNuyen(eUndoType, strObjectId, decQty);
            objExpense.Undo = objUndo;
            return objExpense;
        }

        private static XmlNode DataNode(Character objCharacter, string strFile, string strXPath, string strId)
        {
            return objCharacter.LoadData(strFile).TryGetNodeByNameOrId(strXPath, strId);
        }

        private static async Task DeleteWeaponsAsync(IEnumerable<Weapon> lstWeapons)
        {
            foreach (Weapon objWeapon in lstWeapons)
                await objWeapon.DeleteWeaponAsync(token: CancellationToken.None).ConfigureAwait(false);
        }

        #endregion

        #region Gear

        /// <summary>Legacy CharacterCareer.PickGear (career) / CharacterCreate.PickGear (create), top-level or as a plugin of a piece of Gear.</summary>
        public static async Task<PurchaseResult> PurchaseGearAsync(Character objCharacter, PurchaseRequest objRequest, CancellationToken token = default)
        {
            XmlNode xmlGear = DataNode(objCharacter, "gear.xml", "/chummer/gears/gear", objRequest.ItemId);
            if (xmlGear == null)
                return new PurchaseResult(RuleResult.NotAllowed);
            Gear objParentGear = objRequest.Parent as Gear;
            bool blnCareer = await objCharacter.GetCreatedAsync(token).ConfigureAwait(false);
            CharacterSettings objSettings = await objCharacter.GetSettingsAsync(token).ConfigureAwait(false);
            if (objParentGear != null)
            {
                // Same capacity gate as the legacy form before the picker opens.
                string strParentCapacity = objParentGear.Capacity ?? string.Empty;
                if ((!string.IsNullOrEmpty(strParentCapacity) && !strParentCapacity.Contains('[') || strParentCapacity.Contains("/["))
                    && await objSettings.GetEnforceCapacityAsync(token).ConfigureAwait(false)
                    && await objParentGear.GetCapacityRemainingAsync(token).ConfigureAwait(false) < 0)
                {
                    await ShowCapacityReachedAsync(token).ConfigureAwait(false);
                    return new PurchaseResult(RuleResult.NotAllowed);
                }
            }

            List<Weapon> lstWeapons = new List<Weapon>(1);
            Gear objGear = new Gear(objCharacter);
            try
            {
                await objGear.CreateAsync(xmlGear, objRequest.Rating, lstWeapons, string.Empty,
                                          objParentGear?.Equipped != false, objParent: objParentGear, token: token).ConfigureAwait(false);
                if (objGear.InternalId.IsEmptyGuid())
                {
                    await objGear.DeleteGearAsync(token: CancellationToken.None).ConfigureAwait(false);
                    await DeleteWeaponsAsync(lstWeapons).ConfigureAwait(false);
                    return new PurchaseResult(RuleResult.Cancelled);
                }

                await objGear.SetQuantityAsync(objRequest.Quantity, token).ConfigureAwait(false);
                // Black Market Pipeline
                objGear.DiscountCost = objRequest.BlackMarket;
                // Do It Yourself components cost half.
                if (objRequest.DoItYourself)
                    objGear.Cost = "(" + objGear.Cost + ") * 0.5";

                if (!blnCareer)
                    return await FinishGearCreateAsync(objCharacter, objRequest, objGear, objParentGear, lstWeapons, token).ConfigureAwait(false);

                Gear objStackWith = null;
                if (objRequest.Stack)
                {
                    foreach (Gear objExisting in objParentGear != null ? objParentGear.Children : objCharacter.Gear)
                    {
                        if (objExisting.Location == objRequest.Location && objGear.IsIdenticalToOtherGear(objExisting))
                        {
                            objStackWith = objExisting;
                            break;
                        }
                    }
                }

                // Do not allow the user to add a new piece of Gear if its Capacity has been reached.
                if (objParentGear != null && objStackWith == null
                                          && await objSettings.GetEnforceCapacityAsync(token).ConfigureAwait(false)
                                          && await objParentGear.GetCapacityRemainingAsync(token).ConfigureAwait(false)
                                          < await objGear.GetPluginCapacityAsync(token).ConfigureAwait(false))
                {
                    await ShowCapacityReachedAsync(token).ConfigureAwait(false);
                    await objGear.DeleteGearAsync(token: CancellationToken.None).ConfigureAwait(false);
                    await DeleteWeaponsAsync(lstWeapons).ConfigureAwait(false);
                    return new PurchaseResult(RuleResult.NotAllowed);
                }

                // Add the gear now to help with cost calculations
                decimal decOldCost = 0;
                if (objStackWith != null)
                {
                    if (!objRequest.Free)
                        decOldCost = await objStackWith.GetTotalCostAsync(token).ConfigureAwait(false);
                    await objStackWith.SetQuantityAsync(objStackWith.Quantity + objGear.Quantity, token).ConfigureAwait(false);
                }
                else if (objParentGear != null)
                {
                    if (!objRequest.Free)
                        decOldCost = await objParentGear.GetTotalCostAsync(token).ConfigureAwait(false);
                    await objParentGear.Children.AddAsync(objGear, token).ConfigureAwait(false);
                }
                else
                    await objCharacter.Gear.AddAsync(objGear, token).ConfigureAwait(false);

                decimal decCost = 0;
                ExpenseLogEntry objExpense = null;
                if (!objRequest.Free)
                {
                    decCost = objStackWith != null
                        ? await objStackWith.GetTotalCostAsync(token).ConfigureAwait(false) - decOldCost
                        : objParentGear == null
                            ? await objGear.GetTotalCostAsync(token).ConfigureAwait(false)
                            : await objParentGear.GetTotalCostAsync(token).ConfigureAwait(false) - decOldCost;
                    decCost = await StreetCostAsync(objCharacter, decCost, objRequest.Markup,
                                                    (await objGear.TotalAvailTupleAsync(token: token).ConfigureAwait(false)).Suffix, token).ConfigureAwait(false);
                    if (!await CheckNuyenAsync(objCharacter, decCost, token).ConfigureAwait(false))
                    {
                        if (objStackWith != null)
                            await objStackWith.SetQuantityAsync(objStackWith.Quantity - objGear.Quantity, token).ConfigureAwait(false);
                        await DeleteWeaponsAsync(lstWeapons).ConfigureAwait(false);
                        // Remove any Improvements created by the Gear (and the gear itself).
                        await objGear.DeleteGearAsync(token: CancellationToken.None).ConfigureAwait(false);
                        return new PurchaseResult(RuleResult.NotEnoughNuyen);
                    }

                    objExpense = await ChargeAsync(objCharacter, decCost, "String_ExpensePurchaseGear",
                                                   await objGear.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false),
                                                   NuyenExpenseType.AddGear, objStackWith != null ? objStackWith.InternalId : objGear.InternalId,
                                                   objGear.Quantity, token).ConfigureAwait(false);
                }

                if (objStackWith != null)
                {
                    await objGear.DeleteGearAsync(token: CancellationToken.None).ConfigureAwait(false);
                    await DeleteWeaponsAsync(lstWeapons).ConfigureAwait(false);
                    return new PurchaseResult(RuleResult.Changed, objStackWith, decCost, objExpense);
                }

                foreach (Weapon objWeapon in lstWeapons)
                    await objCharacter.Weapons.AddAsync(objWeapon, token).ConfigureAwait(false);
                if (objRequest.Location != null)
                    await objRequest.Location.Children.AddAsync(objGear, token).ConfigureAwait(false);
                // If a Commlink has just been added, see if the character already has one. If not, make it the active Commlink.
                if (objParentGear == null && await objCharacter.GetActiveCommlinkAsync(token).ConfigureAwait(false) == null
                                          && await objGear.GetIsCommlinkAsync(token).ConfigureAwait(false))
                    await objGear.SetActiveCommlinkAsync(objCharacter, true, token).ConfigureAwait(false);
                return new PurchaseResult(RuleResult.Changed, objGear, decCost, objExpense);
            }
            catch
            {
                await DeleteWeaponsAsync(lstWeapons).ConfigureAwait(false);
                await objGear.DeleteGearAsync(token: CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        private static async Task<PurchaseResult> FinishGearCreateAsync(Character objCharacter, PurchaseRequest objRequest, Gear objGear, Gear objParentGear,
                                                                        List<Weapon> lstWeapons, CancellationToken token)
        {
            // If a Commlink has just been added, see if the character already has one. If not, make it the active Commlink.
            if (await objCharacter.GetActiveCommlinkAsync(token).ConfigureAwait(false) == null
                && await objGear.GetIsCommlinkAsync(token).ConfigureAwait(false))
                await objGear.SetActiveCommlinkAsync(objCharacter, true, token).ConfigureAwait(false);
            // If the item was marked as free, change its cost.
            if (objRequest.Free)
                objGear.Cost = "0";
            foreach (Weapon objWeapon in lstWeapons)
                await objCharacter.Weapons.AddAsync(objWeapon, token).ConfigureAwait(false);
            ThreadSafeObservableCollection<Gear> lstDestination = objParentGear != null ? objParentGear.Children : objCharacter.Gear;
            foreach (Gear objExisting in lstDestination)
            {
                if (objExisting.Location != objRequest.Location || !objGear.IsIdenticalToOtherGear(objExisting, true))
                    continue;
                if (await MessageBoxes.ShowAsync(
                        string.Format(GlobalSettings.CultureInfo,
                                      await LanguageManager.GetStringAsync("Message_MergeIdentical", token: token).ConfigureAwait(false),
                                      await objGear.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false)),
                        await LanguageManager.GetStringAsync("MessageTitle_MergeIdentical", token: token).ConfigureAwait(false),
                        MessageButtons.YesNo, MessageIcon.Question, token: token).ConfigureAwait(false) != MessageResult.Yes)
                    continue;
                // A match was found, so increase the quantity instead.
                await objExisting.SetQuantityAsync(objExisting.Quantity + objGear.Quantity, token).ConfigureAwait(false);
                await objGear.DeleteGearAsync(token: CancellationToken.None).ConfigureAwait(false);
                return new PurchaseResult(RuleResult.Changed, objExisting);
            }
            if (objRequest.Location != null)
                await objRequest.Location.Children.AddAsync(objGear, token).ConfigureAwait(false);
            await lstDestination.AddAsync(objGear, token).ConfigureAwait(false);
            return new PurchaseResult(RuleResult.Changed, objGear);
        }

        #endregion

        #region Weapons, armor, vehicles

        /// <summary>Legacy CharacterCareer.PickWeapon / CharacterCreate.PickWeapon.</summary>
        public static async Task<PurchaseResult> PurchaseWeaponAsync(Character objCharacter, PurchaseRequest objRequest, CancellationToken token = default)
        {
            if (objRequest.Parent is WeaponMount mount)
                return await WeaponMountRules.PurchaseWeaponAsync(objCharacter, mount, objRequest, token).ConfigureAwait(false);
            XmlNode xmlWeapon = DataNode(objCharacter, "weapons.xml", "/chummer/weapons/weapon", objRequest.ItemId);
            if (xmlWeapon == null)
                return new PurchaseResult(RuleResult.NotAllowed);
            bool blnCareer = await objCharacter.GetCreatedAsync(token).ConfigureAwait(false);
            List<Weapon> lstWeapons = new List<Weapon>(1);
            Weapon objWeapon = new Weapon(objCharacter);
            try
            {
                await objWeapon.CreateAsync(xmlWeapon, lstWeapons, token: token).ConfigureAwait(false);
                objWeapon.DiscountCost = objRequest.BlackMarket;
                decimal decCost = 0;
                ExpenseLogEntry objExpense = null;
                if (blnCareer)
                {
                    if (!objRequest.Free)
                    {
                        decCost = await StreetCostAsync(objCharacter, await objWeapon.GetTotalCostAsync(token).ConfigureAwait(false), objRequest.Markup,
                                                        (await objWeapon.TotalAvailTupleAsync(token: token).ConfigureAwait(false)).Suffix, token).ConfigureAwait(false);
                        if (!await CheckNuyenAsync(objCharacter, decCost, token).ConfigureAwait(false))
                        {
                            await DeleteWeaponsAsync(lstWeapons).ConfigureAwait(false);
                            await objWeapon.DeleteWeaponAsync(token: CancellationToken.None).ConfigureAwait(false);
                            return new PurchaseResult(RuleResult.NotEnoughNuyen);
                        }
                        objExpense = await ChargeAsync(objCharacter, decCost, "String_ExpensePurchaseWeapon",
                                                       await objWeapon.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false),
                                                       NuyenExpenseType.AddWeapon, objWeapon.InternalId, 0, token).ConfigureAwait(false);
                    }
                    if (objRequest.Location != null)
                    {
                        objWeapon.Location = objRequest.Location;
                        foreach (Weapon objExtraWeapon in lstWeapons)
                            objExtraWeapon.Location = objRequest.Location;
                    }
                    foreach (Weapon objExtraWeapon in lstWeapons)
                        await objCharacter.Weapons.AddAsync(objExtraWeapon, token).ConfigureAwait(false);
                    await objCharacter.Weapons.AddAsync(objWeapon, token).ConfigureAwait(false);
                }
                else
                {
                    if (objRequest.Free)
                        objWeapon.Cost = "0";
                    if (objRequest.Location != null)
                        await objRequest.Location.Children.AddAsync(objWeapon, token).ConfigureAwait(false);
                    await objCharacter.Weapons.AddAsync(objWeapon, token).ConfigureAwait(false);
                    foreach (Weapon objExtraWeapon in lstWeapons)
                        await objCharacter.Weapons.AddAsync(objExtraWeapon, token).ConfigureAwait(false);
                }
                return new PurchaseResult(RuleResult.Changed, objWeapon, decCost, objExpense);
            }
            catch
            {
                await DeleteWeaponsAsync(lstWeapons).ConfigureAwait(false);
                await objWeapon.DeleteWeaponAsync(token: CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>Legacy CharacterCareer.PickArmor / CharacterCreate.PickArmor.</summary>
        public static async Task<PurchaseResult> PurchaseArmorAsync(Character objCharacter, PurchaseRequest objRequest, CancellationToken token = default)
        {
            XmlNode xmlArmor = DataNode(objCharacter, "armor.xml", "/chummer/armors/armor", objRequest.ItemId);
            if (xmlArmor == null)
                return new PurchaseResult(RuleResult.NotAllowed);
            bool blnCareer = await objCharacter.GetCreatedAsync(token).ConfigureAwait(false);
            List<Weapon> lstWeapons = new List<Weapon>(1);
            Armor objArmor = new Armor(objCharacter);
            try
            {
                await objArmor.CreateAsync(xmlArmor, objRequest.Rating, lstWeapons, token: token).ConfigureAwait(false);
                objArmor.DiscountCost = objRequest.BlackMarket;
                if (objArmor.InternalId.IsEmptyGuid())
                {
                    await DeleteWeaponsAsync(lstWeapons).ConfigureAwait(false);
                    await objArmor.DeleteArmorAsync(CancellationToken.None).ConfigureAwait(false);
                    return new PurchaseResult(RuleResult.Cancelled);
                }
                decimal decCost = 0;
                ExpenseLogEntry objExpense = null;
                if (blnCareer)
                {
                    if (!objRequest.Free)
                    {
                        decCost = await StreetCostAsync(objCharacter, await objArmor.GetTotalCostAsync(token).ConfigureAwait(false), objRequest.Markup,
                                                        (await objArmor.TotalAvailTupleAsync(token: token).ConfigureAwait(false)).Suffix, token).ConfigureAwait(false);
                        if (!await CheckNuyenAsync(objCharacter, decCost, token).ConfigureAwait(false))
                        {
                            // Remove the Improvements created by the Armor.
                            await ImprovementManager.RemoveImprovementsAsync(objCharacter, Improvement.ImprovementSource.Armor, objArmor.InternalId, token).ConfigureAwait(false);
                            await DeleteWeaponsAsync(lstWeapons).ConfigureAwait(false);
                            await objArmor.DeleteArmorAsync(CancellationToken.None).ConfigureAwait(false);
                            return new PurchaseResult(RuleResult.NotEnoughNuyen);
                        }
                        objExpense = await ChargeAsync(objCharacter, decCost, "String_ExpensePurchaseArmor",
                                                       await objArmor.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false),
                                                       NuyenExpenseType.AddArmor, objArmor.InternalId, 0, token).ConfigureAwait(false);
                    }
                }
                else if (objRequest.Free)
                    objArmor.Cost = "0";

                if (objRequest.Location != null)
                    await objRequest.Location.Children.AddAsync(objArmor, token).ConfigureAwait(false);
                await objCharacter.Armor.AddAsync(objArmor, token).ConfigureAwait(false);
                foreach (Weapon objWeapon in lstWeapons)
                    await objCharacter.Weapons.AddAsync(objWeapon, token).ConfigureAwait(false);
                return new PurchaseResult(RuleResult.Changed, objArmor, decCost, objExpense);
            }
            catch
            {
                await DeleteWeaponsAsync(lstWeapons).ConfigureAwait(false);
                await objArmor.DeleteArmorAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>Legacy CharacterCareer.AddVehicle / CharacterCreate.AddVehicle (incl. the "used vehicle" option).</summary>
        public static async Task<PurchaseResult> PurchaseVehicleAsync(Character objCharacter, PurchaseRequest objRequest, CancellationToken token = default)
        {
            XmlNode xmlVehicle = DataNode(objCharacter, "vehicles.xml", "/chummer/vehicles/vehicle", objRequest.ItemId);
            if (xmlVehicle == null)
                return new PurchaseResult(RuleResult.NotAllowed);
            bool blnCareer = await objCharacter.GetCreatedAsync(token).ConfigureAwait(false);
            Vehicle objVehicle = new Vehicle(objCharacter);
            try
            {
                await objVehicle.CreateAsync(xmlVehicle, token: token).ConfigureAwait(false);
                if (objRequest.UsedVehicle)
                {
                    // Legacy SelectVehicle: used cost = data cost * (1 - discount); avail stays what the picker showed.
                    decimal.TryParse(xmlVehicle["cost"]?.InnerText, NumberStyles.Any, GlobalSettings.InvariantCultureInfo, out decimal decUsedCost);
                    decUsedCost *= 1 - Math.Max(20, Math.Min(50, objRequest.UsedVehicleDiscount)) / 100.0m;
                    AvailabilityValue objAvail = await objVehicle.TotalAvailTupleAsync(token: token).ConfigureAwait(false);
                    objVehicle.Avail = (await objAvail.GetValueAsync(token).ConfigureAwait(false)).ToString(GlobalSettings.InvariantCultureInfo)
                                       + (objAvail.Suffix == 'R' || objAvail.Suffix == 'F' ? objAvail.Suffix.ToString() : string.Empty);
                    objVehicle.Cost = decUsedCost.ToString(GlobalSettings.InvariantCultureInfo);
                }
                objVehicle.DiscountCost = objRequest.BlackMarket;
                decimal decCost = 0;
                ExpenseLogEntry objExpense = null;
                if (blnCareer)
                {
                    if (!objRequest.Free)
                    {
                        decCost = await StreetCostAsync(objCharacter, await objVehicle.GetTotalCostAsync(token).ConfigureAwait(false), objRequest.Markup,
                                                        (await objVehicle.TotalAvailTupleAsync(token: token).ConfigureAwait(false)).Suffix, token).ConfigureAwait(false);
                        if (!await CheckNuyenAsync(objCharacter, decCost, token).ConfigureAwait(false))
                        {
                            await objVehicle.DeleteVehicleAsync(CancellationToken.None).ConfigureAwait(false);
                            return new PurchaseResult(RuleResult.NotEnoughNuyen);
                        }
                        objExpense = await ChargeAsync(objCharacter, decCost, "String_ExpensePurchaseVehicle",
                                                       await objVehicle.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false),
                                                       NuyenExpenseType.AddVehicle, objVehicle.InternalId, 0, token).ConfigureAwait(false);
                    }
                }
                else if (objRequest.Free)
                    objVehicle.Cost = "0";

                if (objRequest.Location != null)
                    await objRequest.Location.Children.AddAsync(objVehicle, token).ConfigureAwait(false);
                await objCharacter.Vehicles.AddAsync(objVehicle, token).ConfigureAwait(false);
                return new PurchaseResult(RuleResult.Changed, objVehicle, decCost, objExpense);
            }
            catch
            {
                await objVehicle.DeleteVehicleAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        #endregion

        #region Cyberware, drugs, lifestyles

        public static Improvement.ImprovementSource WareSource(EquipmentKind eKind)
            => eKind == EquipmentKind.Bioware ? Improvement.ImprovementSource.Bioware : Improvement.ImprovementSource.Cyberware;

        /// <summary>Grade with the given id, else the first grade the store would list.</summary>
        public static async Task<Grade> FindGradeAsync(Character objCharacter, Improvement.ImprovementSource eSource, string strGradeId, CancellationToken token = default)
        {
            List<Grade> lstGrades = await objCharacter.GetGradesListAsync(eSource, token: token).ConfigureAwait(false);
            Grade objFallback = null;
            foreach (Grade objGrade in lstGrades)
            {
                if (!string.IsNullOrEmpty(strGradeId) && string.Equals(objGrade.SourceIDString, strGradeId, StringComparison.OrdinalIgnoreCase))
                    return objGrade;
                if (objFallback == null && objGrade.Name != "None")
                    objFallback = objGrade;
            }
            return objFallback;
        }

        /// <summary>
        /// Legacy CharacterCareer.PickCyberware / CharacterCreate.PickCyberware: Cyberware.Purchase does the nuyen check and
        /// writes String_ExpensePurchaseCyberware itself. A Cyberware parent makes it a plugin (subsystem).
        /// </summary>
        public static async Task<PurchaseResult> PurchaseCyberwareAsync(Character objCharacter, PurchaseRequest objRequest, CancellationToken token = default)
        {
            Improvement.ImprovementSource eSource = WareSource(objRequest.Kind);
            XmlNode xmlWare = eSource == Improvement.ImprovementSource.Bioware
                ? DataNode(objCharacter, "bioware.xml", "/chummer/biowares/bioware", objRequest.ItemId)
                : DataNode(objCharacter, "cyberware.xml", "/chummer/cyberwares/cyberware", objRequest.ItemId);
            if (xmlWare == null)
                return new PurchaseResult(RuleResult.NotAllowed);
            Cyberware objParent = objRequest.Parent as Cyberware;
            CharacterSettings objSettings = await objCharacter.GetSettingsAsync(token).ConfigureAwait(false);
            if (objParent != null && (!objParent.Capacity.Contains('[') || objParent.Capacity.Contains("/["))
                                  && await objSettings.GetEnforceCapacityAsync(token).ConfigureAwait(false)
                                  && await objParent.GetCapacityRemainingAsync(token).ConfigureAwait(false) < 0)
            {
                await ShowCapacityReachedAsync(token).ConfigureAwait(false);
                return new PurchaseResult(RuleResult.NotAllowed);
            }
            Grade objGrade = objParent != null
                ? await objParent.GetGradeAsync(token).ConfigureAwait(false)
                : await FindGradeAsync(objCharacter, eSource, objRequest.GradeId, token).ConfigureAwait(false);
            decimal decNuyenBefore = await objCharacter.GetNuyenAsync(token).ConfigureAwait(false);
            Cyberware objCyberware = new Cyberware(objCharacter);
            try
            {
                if (await objSettings.GetAllowCyberwareESSDiscountsAsync(token).ConfigureAwait(false))
                    await objCyberware.SetESSDiscountAsync(objRequest.EssenceDiscount, token).ConfigureAwait(false);
                await objCyberware.SetParentAsync(objParent, token).ConfigureAwait(false);
                if (!await objCyberware.Purchase(xmlWare, eSource, objGrade, objRequest.Rating, null,
                                                 objParent != null
                                                     ? await objParent.GetChildrenAsync(token).ConfigureAwait(false)
                                                     : await objCharacter.GetCyberwareAsync(token).ConfigureAwait(false),
                                                 await objCharacter.GetVehiclesAsync(token).ConfigureAwait(false),
                                                 await objCharacter.GetWeaponsAsync(token).ConfigureAwait(false),
                                                 objRequest.Markup, objRequest.Free, objRequest.BlackMarket,
                                                 objParent: objParent, token: token).ConfigureAwait(false))
                {
                    bool blnCreated = !objCyberware.InternalId.IsEmptyGuid();
                    await objCyberware.DeleteCyberwareAsync(token: CancellationToken.None).ConfigureAwait(false);
                    return new PurchaseResult(blnCreated && await objCharacter.GetCreatedAsync(token).ConfigureAwait(false) && !objRequest.Free
                                                  ? RuleResult.NotEnoughNuyen
                                                  : RuleResult.Cancelled);
                }
                decimal decCost = decNuyenBefore - await objCharacter.GetNuyenAsync(token).ConfigureAwait(false);
                ExpenseLogEntry objExpense = null;
                if (await objCharacter.GetCreatedAsync(token).ConfigureAwait(false))
                {
                    foreach (ExpenseLogEntry objEntry in objCharacter.ExpenseEntries)
                    {
                        if (objEntry.Undo?.ObjectId == objCyberware.InternalId)
                        {
                            objExpense = objEntry;
                            break;
                        }
                    }
                }
                return new PurchaseResult(RuleResult.Changed, objCyberware, decCost, objExpense);
            }
            catch
            {
                await objCyberware.DeleteCyberwareAsync(token: CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>Legacy CharacterShared.AddPremadeDrugAsync / AddDrugToCharacterAsync.</summary>
        public static async Task<PurchaseResult> PurchaseDrugAsync(Character objCharacter, PurchaseRequest objRequest, CancellationToken token = default)
        {
            Grade objGrade = await FindGradeAsync(objCharacter, Improvement.ImprovementSource.Drug, objRequest.GradeId, token).ConfigureAwait(false);
            Drug objDrug = await Drug.CreateFromCatalogAsync(objCharacter, objRequest.ItemId, objGrade, objRequest.Rating, token).ConfigureAwait(false);
            if (objDrug == null)
                return new PurchaseResult(RuleResult.NotAllowed);
            objDrug.DiscountCost = objRequest.BlackMarket;
            return await PurchaseDrugInstanceAsync(objCharacter, objDrug, objRequest.Markup, objRequest.Free, token).ConfigureAwait(false);
        }

        internal static async Task<PurchaseResult> PurchaseDrugInstanceAsync(Character objCharacter, Drug objDrug, decimal markup, bool free, CancellationToken token)
        {
            if (objDrug == null || !ReferenceEquals(objDrug.CharacterObject, objCharacter)
                || await objCharacter.Drugs.ContainsAsync(objDrug, token).ConfigureAwait(false)) return new PurchaseResult(RuleResult.NotAllowed);
            bool committed = false;
            bool effectsStarted = false;
            try
            {
                bool blnCareer = await objCharacter.GetCreatedAsync(token).ConfigureAwait(false);
                decimal decCost = 0;
                if (blnCareer && !free)
                {
                    // Black Market Pipeline is already included in Drug.Cost.
                    decCost = await StreetCostAsync(objCharacter, await objDrug.GetCostAsync(token).ConfigureAwait(false), markup,
                        (await objDrug.TotalAvailTupleAsync(token: token).ConfigureAwait(false)).Suffix, token).ConfigureAwait(false);
                    if (!await CheckNuyenAsync(objCharacter, decCost, token).ConfigureAwait(false))
                        return new PurchaseResult(RuleResult.NotEnoughNuyen);
                }
                await objCharacter.Drugs.AddAsync(objDrug, token).ConfigureAwait(false);
                effectsStarted = true;
                await objDrug.GenerateImprovementsAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                ExpenseLogEntry objExpense = null;
                if (blnCareer && !free)
                    objExpense = await ChargeAsync(objCharacter, decCost, "String_ExpensePurchaseDrug",
                        await objDrug.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false),
                        NuyenExpenseType.AddDrug, objDrug.InternalId, objDrug.Quantity, CancellationToken.None).ConfigureAwait(false);
                committed = true;
                return new PurchaseResult(RuleResult.Changed, objDrug, decCost, objExpense);
            }
            finally
            {
                if (!committed)
                {
                    if (effectsStarted) await objDrug.RemoveAsync(false, CancellationToken.None).ConfigureAwait(false);
                    else
                    {
                        await objCharacter.Drugs.RemoveAsync(objDrug, CancellationToken.None).ConfigureAwait(false);
                        await objDrug.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Legacy cmdAddLifestyle_Click + SelectLifestyle.AcceptForm (standard lifestyles). Adding a lifestyle costs nothing
        /// up front in either mode; rent is paid month by month (<see cref="PayLifestyleMonthAsync"/>).
        /// </summary>
        public static async Task<PurchaseResult> PurchaseLifestyleAsync(Character objCharacter, PurchaseRequest objRequest, CancellationToken token = default)
        {
            XmlNode xmlLifestyle = DataNode(objCharacter, "lifestyles.xml", "/chummer/lifestyles/lifestyle", objRequest.ItemId);
            if (xmlLifestyle == null)
                return new PurchaseResult(RuleResult.NotAllowed);
            Lifestyle objLifestyle = new Lifestyle(objCharacter);
            try
            {
                await objLifestyle.SetStyleTypeAsync(LifestyleType.Standard, token).ConfigureAwait(false);
                string strBaseLifestyle = xmlLifestyle["name"]?.InnerText ?? objRequest.ItemId;
                string strName = string.IsNullOrWhiteSpace(objRequest.LifestyleName)
                    ? xmlLifestyle["translate"]?.InnerText ?? strBaseLifestyle
                    : objRequest.LifestyleName;
                objLifestyle.Source = xmlLifestyle["source"]?.InnerText;
                objLifestyle.Page = xmlLifestyle["page"]?.InnerText;
                await objLifestyle.SetNameAsync(strName, token).ConfigureAwait(false);
                decimal.TryParse(xmlLifestyle["cost"]?.InnerText, NumberStyles.Any, GlobalSettings.InvariantCultureInfo, out decimal decCost);
                await objLifestyle.SetCostAsync(decCost, token).ConfigureAwait(false);
                await objLifestyle.SetPercentageAsync(objRequest.Percentage, token).ConfigureAwait(false);
                await objLifestyle.SetBaseLifestyleAsync(strBaseLifestyle, token).ConfigureAwait(false);
                await objLifestyle.SetTrustFundAsync(objRequest.TrustFund, token).ConfigureAwait(false);
                await objLifestyle.SetRoommatesAsync(objLifestyle.TrustFund ? 0 : Math.Max(0, objRequest.Roommates), token).ConfigureAwait(false);
                await objLifestyle.SetSplitCostWithRoommatesAsync(objRequest.SplitCostWithRoommates, token).ConfigureAwait(false);
                if (int.TryParse(xmlLifestyle["dice"]?.InnerText, NumberStyles.Integer, GlobalSettings.InvariantCultureInfo, out int intDice))
                    await objLifestyle.SetDiceAsync(intDice, token).ConfigureAwait(false);
                decimal.TryParse(xmlLifestyle["multiplier"]?.InnerText, NumberStyles.Any, GlobalSettings.InvariantCultureInfo, out decimal decMultiplier);
                await objLifestyle.SetMultiplierAsync(decMultiplier, token).ConfigureAwait(false);
                await objCharacter.Lifestyles.AddAsync(objLifestyle, token).ConfigureAwait(false);
                return new PurchaseResult(RuleResult.Changed, objLifestyle);
            }
            catch
            {
                await objLifestyle.RemoveAsync(false, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>Career: pay one more month of rent (legacy cmdIncreaseLifestyleMonths_Click → Lifestyle.BuyExtraMonth).</summary>
        public static async Task<RuleResult> PayLifestyleMonthAsync(Character objCharacter, Lifestyle objLifestyle, CancellationToken token = default)
        {
            if (objLifestyle == null)
                throw new ArgumentNullException(nameof(objLifestyle));
            if (!await objCharacter.GetCreatedAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            int intBefore = await objLifestyle.GetIncrementsAsync(token).ConfigureAwait(false);
            await objLifestyle.BuyExtraMonth(token).ConfigureAwait(false);
            return await objLifestyle.GetIncrementsAsync(token).ConfigureAwait(false) > intBefore ? RuleResult.Changed : RuleResult.NotEnoughNuyen;
        }

        /// <summary>Career: one month less (legacy cmdDecreaseLifestyleMonths_Click: a zero-amount String_ExpenseDecreaseLifestyle entry).</summary>
        public static async Task<RuleResult> DecreaseLifestyleMonthAsync(Character objCharacter, Lifestyle objLifestyle, CancellationToken token = default)
        {
            if (objLifestyle == null)
                throw new ArgumentNullException(nameof(objLifestyle));
            if (!await objCharacter.GetCreatedAsync(token).ConfigureAwait(false))
                return RuleResult.NotAllowed;
            ExpenseLogEntry objExpense = new ExpenseLogEntry(objCharacter);
            objExpense.Create(0,
                await LanguageManager.GetStringAsync("String_ExpenseDecreaseLifestyle", token: token).ConfigureAwait(false)
                + await LanguageManager.GetStringAsync("String_Space", token: token).ConfigureAwait(false)
                + await objLifestyle.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false),
                ExpenseType.Nuyen, DateTime.Now);
            await objCharacter.ExpenseEntries.AddWithSortAsync(objExpense, token: token).ConfigureAwait(false);
            await objLifestyle.ModifyIncrementsAsync(-1, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        #endregion

        #region Mods, accessories

        /// <summary>Legacy CharacterShared.AddWeaponAccessoryAsync (career: charged, create: free sets cost 0).</summary>
        public static async Task<PurchaseResult> PurchaseWeaponAccessoryAsync(Character objCharacter, PurchaseRequest objRequest, CancellationToken token = default)
        {
            if (!(objRequest.Parent is Weapon objWeapon))
                return new PurchaseResult(RuleResult.NotAllowed);
            if (objWeapon.Cyberware)
            {
                await MessageBoxes.ShowAsync(
                    await LanguageManager.GetStringAsync("Message_CyberweaponNoAccessory", token: token).ConfigureAwait(false),
                    await LanguageManager.GetStringAsync("MessageTitle_CyberweaponNoAccessory", token: token).ConfigureAwait(false),
                    MessageButtons.OK, MessageIcon.Information, token: token).ConfigureAwait(false);
                return new PurchaseResult(RuleResult.NotAllowed);
            }
            if (!objWeapon.AllowAccessory)
            {
                await MessageBoxes.ShowAsync(
                    await LanguageManager.GetStringAsync("Message_CannotModifyWeapon", token: token).ConfigureAwait(false),
                    await LanguageManager.GetStringAsync("MessageTitle_CannotModifyWeapon", token: token).ConfigureAwait(false),
                    MessageButtons.OK, MessageIcon.Information, token: token).ConfigureAwait(false);
                return new PurchaseResult(RuleResult.NotAllowed);
            }
            XmlNode xmlAccessory = DataNode(objCharacter, "weapons.xml", "/chummer/accessories/accessory", objRequest.ItemId);
            if (xmlAccessory == null)
                return new PurchaseResult(RuleResult.NotAllowed);
            bool blnCareer = await objCharacter.GetCreatedAsync(token).ConfigureAwait(false);
            WeaponAccessory objAccessory = new WeaponAccessory(objCharacter);
            try
            {
                await objAccessory.CreateAsync(xmlAccessory,
                                               new ValueTuple<string, string>(string.IsNullOrEmpty(objRequest.Mount) ? "None" : objRequest.Mount,
                                                                              string.IsNullOrEmpty(objRequest.ExtraMount) ? "None" : objRequest.ExtraMount),
                                               objRequest.Rating, token: token).ConfigureAwait(false);
                await objAccessory.SetParentAsync(objWeapon, token).ConfigureAwait(false);
                objAccessory.DiscountCost = objRequest.BlackMarket;
                decimal decOriginalCost = blnCareer ? await objWeapon.GetTotalCostAsync(token).ConfigureAwait(false) : 0;
                if (objRequest.Free && !blnCareer)
                    objAccessory.Cost = "0";
                await objWeapon.WeaponAccessories.AddAsync(objAccessory, token).ConfigureAwait(false);
                decimal decCost = 0;
                ExpenseLogEntry objExpense = null;
                if (blnCareer && !objRequest.Free)
                {
                    decCost = await StreetCostAsync(objCharacter, await objWeapon.GetTotalCostAsync(token).ConfigureAwait(false) - decOriginalCost, objRequest.Markup,
                                                    (await objAccessory.TotalAvailTupleAsync(token: token).ConfigureAwait(false)).Suffix, token).ConfigureAwait(false);
                    if (!await CheckNuyenAsync(objCharacter, decCost, token).ConfigureAwait(false))
                    {
                        await objWeapon.WeaponAccessories.RemoveAsync(objAccessory, token).ConfigureAwait(false);
                        return new PurchaseResult(RuleResult.NotEnoughNuyen);
                    }
                    objExpense = await ChargeAsync(objCharacter, decCost, "String_ExpensePurchaseWeaponAccessory",
                                                   await objAccessory.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false),
                                                   NuyenExpenseType.AddWeaponAccessory, objAccessory.InternalId, 0, token).ConfigureAwait(false);
                }
                return new PurchaseResult(RuleResult.Changed, objAccessory, decCost, objExpense);
            }
            catch
            {
                await objAccessory.DeleteWeaponAccessoryAsync(token: CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>Legacy CharacterCareer.tsAddArmorMod_Click (capacity enforced, cost = parent cost difference).</summary>
        public static async Task<PurchaseResult> PurchaseArmorModAsync(Character objCharacter, PurchaseRequest objRequest, CancellationToken token = default)
        {
            if (!(objRequest.Parent is Armor objArmor))
                return new PurchaseResult(RuleResult.NotAllowed);
            XmlNode xmlMod = DataNode(objCharacter, "armor.xml", "/chummer/mods/mod", objRequest.ItemId);
            if (xmlMod == null)
                return new PurchaseResult(RuleResult.NotAllowed);
            bool blnCareer = await objCharacter.GetCreatedAsync(token).ConfigureAwait(false);
            CharacterSettings objSettings = await objCharacter.GetSettingsAsync(token).ConfigureAwait(false);
            int.TryParse(xmlMod["maxrating"]?.InnerText, NumberStyles.Integer, GlobalSettings.InvariantCultureInfo, out int intMaxRating);
            int intRating = intMaxRating > 1 ? objRequest.Rating : 0;
            List<Weapon> lstWeapons = new List<Weapon>(1);
            ArmorMod objMod = new ArmorMod(objCharacter, objArmor);
            try
            {
                await objMod.CreateAsync(xmlMod, intRating, lstWeapons, token: token).ConfigureAwait(false);
                if (objMod.InternalId.IsEmptyGuid())
                {
                    await DeleteWeaponsAsync(lstWeapons).ConfigureAwait(false);
                    await objMod.DeleteArmorModAsync(token: CancellationToken.None).ConfigureAwait(false);
                    return new PurchaseResult(RuleResult.Cancelled);
                }
                objMod.DiscountCost = objRequest.BlackMarket;
                if (objRequest.Free && !blnCareer)
                    objMod.Cost = "0";
                decimal decOriginalCost = await objArmor.GetTotalCostAsync(token).ConfigureAwait(false);
                await objArmor.ArmorMods.AddAsync(objMod, token).ConfigureAwait(false);
                // Do not allow the user to add a new piece of Armor if its Capacity has been reached.
                if (await objSettings.GetEnforceCapacityAsync(token).ConfigureAwait(false)
                    && await objArmor.GetCapacityRemainingAsync(token).ConfigureAwait(false) < 0)
                {
                    await ShowCapacityReachedAsync(token).ConfigureAwait(false);
                    await objArmor.ArmorMods.RemoveAsync(objMod, token).ConfigureAwait(false);
                    await DeleteWeaponsAsync(lstWeapons).ConfigureAwait(false);
                    return new PurchaseResult(RuleResult.NotAllowed);
                }
                decimal decCost = 0;
                ExpenseLogEntry objExpense = null;
                if (blnCareer && !objRequest.Free)
                {
                    decCost = await StreetCostAsync(objCharacter, await objArmor.GetTotalCostAsync(token).ConfigureAwait(false) - decOriginalCost, objRequest.Markup,
                                                    (await objMod.TotalAvailTupleAsync(token: token).ConfigureAwait(false)).Suffix, token).ConfigureAwait(false);
                    if (!await CheckNuyenAsync(objCharacter, decCost, token).ConfigureAwait(false))
                    {
                        await objArmor.ArmorMods.RemoveAsync(objMod, token).ConfigureAwait(false);
                        // Remove the Improvements created by the Armor Mod.
                        await ImprovementManager.RemoveImprovementsAsync(objCharacter, Improvement.ImprovementSource.ArmorMod, objMod.InternalId, token).ConfigureAwait(false);
                        await DeleteWeaponsAsync(lstWeapons).ConfigureAwait(false);
                        return new PurchaseResult(RuleResult.NotEnoughNuyen);
                    }
                    objExpense = await ChargeAsync(objCharacter, decCost, "String_ExpensePurchaseArmorMod",
                                                   await objMod.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false),
                                                   NuyenExpenseType.AddArmorMod, objMod.InternalId, 0, token).ConfigureAwait(false);
                }
                // Add any Weapons created by the Mod.
                foreach (Weapon objWeapon in lstWeapons)
                    await objCharacter.Weapons.AddAsync(objWeapon, token).ConfigureAwait(false);
                return new PurchaseResult(RuleResult.Changed, objMod, decCost, objExpense);
            }
            catch
            {
                await DeleteWeaponsAsync(lstWeapons).ConfigureAwait(false);
                await objMod.DeleteArmorModAsync(token: CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>Legacy CharacterShared.AddVehicleModAsync (slots / R5 capacity enforced).</summary>
        public static async Task<PurchaseResult> PurchaseVehicleModAsync(Character objCharacter, PurchaseRequest objRequest, CancellationToken token = default)
        {
            if (!(objRequest.Parent is Vehicle objVehicle))
                return new PurchaseResult(RuleResult.NotAllowed);
            XmlNode xmlMod = DataNode(objCharacter, "vehicles.xml", "/chummer/mods/mod", objRequest.ItemId);
            if (xmlMod == null)
                return new PurchaseResult(RuleResult.NotAllowed);
            bool blnCareer = await objCharacter.GetCreatedAsync(token).ConfigureAwait(false);
            CharacterSettings objSettings = await objCharacter.GetSettingsAsync(token).ConfigureAwait(false);
            VehicleMod objMod = new VehicleMod(objCharacter);
            try
            {
                objMod.DiscountCost = objRequest.BlackMarket;
                await objMod.CreateAsync(xmlMod, objRequest.Rating, objVehicle, token: token).ConfigureAwait(false);
                decimal decOriginalCost = blnCareer ? await objVehicle.GetTotalCostAsync(token).ConfigureAwait(false) : 0;
                if (objRequest.Free && !blnCareer)
                    objMod.Cost = "0";
                await objVehicle.Mods.AddAsync(objMod, token).ConfigureAwait(false);
                if (await objSettings.GetEnforceCapacityAsync(token).ConfigureAwait(false))
                {
                    bool blnOverCapacity;
                    if (await objSettings.BookEnabledAsync("R5", token).ConfigureAwait(false))
                    {
                        if (objVehicle.IsDrone && await objSettings.GetDroneModsAsync(token).ConfigureAwait(false))
                            blnOverCapacity = await objVehicle.GetDroneModSlotsUsedAsync(token).ConfigureAwait(false)
                                              > await objVehicle.GetDroneModSlotsAsync(token).ConfigureAwait(false);
                        else
                            blnOverCapacity = await objVehicle.OverR5CapacityAsync(objMod.Category, token).ConfigureAwait(false);
                    }
                    else
                        blnOverCapacity = await objVehicle.GetSlotsAsync(token).ConfigureAwait(false)
                                          < await objVehicle.GetSlotsUsedAsync(token).ConfigureAwait(false);
                    if (blnOverCapacity)
                    {
                        await ShowCapacityReachedAsync(token).ConfigureAwait(false);
                        await objVehicle.Mods.RemoveAsync(objMod, token).ConfigureAwait(false);
                        return new PurchaseResult(RuleResult.NotAllowed);
                    }
                }
                decimal decCost = 0;
                ExpenseLogEntry objExpense = null;
                if (blnCareer && !objRequest.Free)
                {
                    decCost = await StreetCostAsync(objCharacter, await objVehicle.GetTotalCostAsync(token).ConfigureAwait(false) - decOriginalCost, objRequest.Markup,
                                                    (await objMod.TotalAvailTupleAsync(token: token).ConfigureAwait(false)).Suffix, token).ConfigureAwait(false);
                    if (!await CheckNuyenAsync(objCharacter, decCost, token).ConfigureAwait(false))
                    {
                        await objVehicle.Mods.RemoveAsync(objMod, token).ConfigureAwait(false);
                        return new PurchaseResult(RuleResult.NotEnoughNuyen);
                    }
                    objExpense = await ChargeAsync(objCharacter, decCost, "String_ExpensePurchaseVehicleMod",
                                                   await objMod.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false),
                                                   NuyenExpenseType.AddVehicleMod, objMod.InternalId, 0, token).ConfigureAwait(false);
                }
                return new PurchaseResult(RuleResult.Changed, objMod, decCost, objExpense);
            }
            catch
            {
                await objMod.DeleteVehicleModAsync(token: CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        #endregion
    }
}
