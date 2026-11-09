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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Backend.Equipment;

namespace Chummer.Rules
{
    /// <summary>Weapon firing modes, in the order of the legacy career ammo menu (cmsAmmoExpense).</summary>
    public enum FiringMode
    {
        SingleShot,
        ShortBurst,
        LongBurst,
        FullBurst,
        Suppressive
    }

    /// <summary>One entry of the "fire" menu: how many rounds the mode uses and whether the weapon supports it.</summary>
    public readonly struct FiringModeInfo
    {
        public FiringModeInfo(FiringMode eMode, bool blnAllowed, int intRounds, string strLabel)
        {
            Mode = eMode;
            Allowed = blnAllowed;
            Rounds = intRounds;
            Label = strLabel;
        }

        public FiringMode Mode { get; }
        public bool Allowed { get; }
        public int Rounds { get; }
        public string Label { get; }
    }

    /// <summary>
    /// Session actions on owned items (equip, wireless, active commlink, firing and reloading weapons, quantities, armor damage,
    /// renaming, notes, selling and removing), lifted from the legacy career form (CharacterCareer.cs / CharacterShared.cs) so
    /// the native inventory applies the same rules, prompts and expense entries.
    /// </summary>
    public static class InventoryRules
    {
        // ---------- equip / wireless / commlink ----------

        /// <summary>Whether the item has an "Equipped" checkbox that can be changed (legacy chkWeaponEquipped/chkGearEquipped/chkArmorEquipped).</summary>
        public static bool CanChangeEquipped(object objItem)
        {
            switch (objItem)
            {
                case Weapon objWeapon:
                    return !objWeapon.IncludedInWeapon;
                case WeaponAccessory objAccessory:
                    return !objAccessory.IncludedInWeapon;
                case Gear objGear:
                    return objGear.CanChangeEquip;
                case Armor _:
                case ArmorMod _:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>chkWeaponEquipped_CheckedChanged / chkArmorEquipped_CheckedChanged / chkGearEquipped_CheckedChanged.</summary>
        public static async Task<RuleResult> SetEquippedAsync(Character objCharacter, object objItem, bool blnEquipped, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (!CanChangeEquipped(objItem))
                return RuleResult.NotAllowed;
            switch (objItem)
            {
                case Weapon objWeapon:
                    await objWeapon.SetEquippedAsync(blnEquipped, token).ConfigureAwait(false);
                    break;
                case WeaponAccessory objAccessory:
                    await objAccessory.SetEquippedAsync(blnEquipped, token).ConfigureAwait(false);
                    break;
                case Armor objArmor:
                    await objArmor.SetEquippedAsync(blnEquipped, token).ConfigureAwait(false);
                    break;
                case ArmorMod objMod:
                    await objMod.SetEquippedAsync(blnEquipped, token).ConfigureAwait(false);
                    break;
                case Gear objGear:
                {
                    await objGear.SetEquippedAsync(blnEquipped, token).ConfigureAwait(false);
                    (Gear objArmorGear, Armor objParentArmor, ArmorMod objParentMod) = await objCharacter.Armor.FindArmorGearAsync(objGear.InternalId, token).ConfigureAwait(false);
                    if (objArmorGear != null && blnEquipped)
                    {
                        // Gear inside armor only applies its bonuses while the armor (and mod) holding it is worn.
                        if (objParentArmor.Equipped && objParentMod?.Equipped != false)
                            await objGear.ChangeEquippedStatusAsync(true, token: token).ConfigureAwait(false);
                    }
                    else
                        await objGear.ChangeEquippedStatusAsync(blnEquipped, token: token).ConfigureAwait(false);
                    break;
                }
                default:
                    return RuleResult.NotAllowed;
            }
            return RuleResult.Changed;
        }

        /// <summary>chkGearWireless / chkWeaponWireless / chkArmorWireless / chkCyberwareWireless.</summary>
        public static RuleResult SetWireless(object objItem, bool blnOn)
        {
            if (!(objItem is IHasWirelessBonus objWireless))
                return RuleResult.NotAllowed;
            if (objWireless.WirelessOn == blnOn)
                return RuleResult.NotAllowed;
            objWireless.WirelessOn = blnOn;
            return RuleResult.Changed;
        }

        /// <summary>chkGearActiveCommlink / chkArmorActiveCommlink / chkCyberwareActiveCommlink ...</summary>
        public static async Task<RuleResult> SetActiveCommlinkAsync(Character objCharacter, object objItem, bool blnActive, CancellationToken token = default)
        {
            if (!(objItem is IHasMatrixAttributes objCommlink) || !objCommlink.IsCommlink)
                return RuleResult.NotAllowed;
            await objCommlink.SetActiveCommlinkAsync(objCharacter, blnActive, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        // ---------- weapons: fire / reload / unload ----------

        /// <summary>Career "Fire" button is enabled for ranged weapons and melee weapons that use ammo (legacy RefreshSelectedWeapon).</summary>
        public static bool UsesAmmo(Weapon objWeapon)
            => objWeapon != null && (objWeapon.RangeType == "Ranged" || (objWeapon.RangeType == "Melee" && objWeapon.Ammo != "0"))
               && objWeapon.Clips.Count > 0 && objWeapon.GetClip(objWeapon.ActiveAmmoSlot) != null;

        public static async Task<IReadOnlyList<FiringModeInfo>> GetFiringModesAsync(Weapon objWeapon, CancellationToken token = default)
        {
            if (objWeapon == null)
                throw new ArgumentNullException(nameof(objWeapon));
            string strBullet = await LanguageManager.GetStringAsync("String_Bullet", token: token).ConfigureAwait(false);
            string strBullets = await LanguageManager.GetStringAsync("String_Bullets", token: token).ConfigureAwait(false);
            async Task<FiringModeInfo> Make(FiringMode eMode, bool blnAllowed, int intRounds, string strKey)
            {
                string strLabel = blnAllowed
                    ? string.Format(GlobalSettings.CultureInfo, await LanguageManager.GetStringAsync(strKey, token: token).ConfigureAwait(false),
                                    intRounds, intRounds == 1 ? strBullet : strBullets)
                    : await LanguageManager.GetStringAsync(strKey + "NA", token: token).ConfigureAwait(false);
                return new FiringModeInfo(eMode, blnAllowed, intRounds, strLabel);
            }
            return new[]
            {
                await Make(FiringMode.SingleShot, await objWeapon.GetAllowSingleShotAsync(token).ConfigureAwait(false), objWeapon.SingleShot, "String_SingleShot").ConfigureAwait(false),
                await Make(FiringMode.ShortBurst, await objWeapon.GetAllowShortBurstAsync(token).ConfigureAwait(false), objWeapon.ShortBurst, "String_ShortBurst").ConfigureAwait(false),
                await Make(FiringMode.LongBurst, await objWeapon.GetAllowLongBurstAsync(token).ConfigureAwait(false), objWeapon.LongBurst, "String_LongBurst").ConfigureAwait(false),
                await Make(FiringMode.FullBurst, await objWeapon.GetAllowFullBurstAsync(token).ConfigureAwait(false), objWeapon.FullBurst, "String_FullBurst").ConfigureAwait(false),
                await Make(FiringMode.Suppressive, await objWeapon.GetAllowSuppressiveAsync(token).ConfigureAwait(false), objWeapon.Suppressive, "String_SuppressiveFire").ConfigureAwait(false),
            };
        }

        /// <summary>cmdFireWeapon_Click: fires the first enabled of single shot, short burst, otherwise long burst.</summary>
        public static async Task<RuleResult> FireDefaultAsync(Weapon objWeapon, CancellationToken token = default)
        {
            if (objWeapon == null)
                throw new ArgumentNullException(nameof(objWeapon));
            FiringMode eMode = await objWeapon.GetAllowSingleShotAsync(token).ConfigureAwait(false)
                ? FiringMode.SingleShot
                : await objWeapon.GetAllowShortBurstAsync(token).ConfigureAwait(false)
                    ? FiringMode.ShortBurst
                    : FiringMode.LongBurst;
            return await FireAsync(objWeapon, eMode, token).ConfigureAwait(false);
        }

        /// <summary>DoSingleShot / DoShortBurst / DoLongBurst / cmsAmmoFullBurst_Click / cmsAmmoSuppressiveFire_Click.</summary>
        public static async Task<RuleResult> FireAsync(Weapon objWeapon, FiringMode eMode, CancellationToken token = default)
        {
            if (objWeapon == null)
                throw new ArgumentNullException(nameof(objWeapon));
            if (!UsesAmmo(objWeapon))
                return RuleResult.NotAllowed;
            int intRemaining = objWeapon.AmmoRemaining;
            switch (eMode)
            {
                case FiringMode.SingleShot:
                    if (intRemaining < objWeapon.SingleShot)
                    {
                        await OutOfAmmoAsync(token).ConfigureAwait(false);
                        return RuleResult.NotAllowed;
                    }
                    await objWeapon.SetAmmoRemaining(intRemaining - objWeapon.SingleShot, token).ConfigureAwait(false);
                    return RuleResult.Changed;
                case FiringMode.ShortBurst:
                    if (intRemaining == 0)
                    {
                        await OutOfAmmoAsync(token).ConfigureAwait(false);
                        return RuleResult.NotAllowed;
                    }
                    if (intRemaining >= objWeapon.ShortBurst)
                    {
                        await objWeapon.SetAmmoRemaining(intRemaining - objWeapon.ShortBurst, token).ConfigureAwait(false);
                        return RuleResult.Changed;
                    }
                    return await AskEmptyClipAsync(objWeapon, intRemaining == objWeapon.SingleShot ? "Message_NotEnoughAmmoSingleShot" : "Message_NotEnoughAmmoShortBurstShort", token).ConfigureAwait(false);
                case FiringMode.LongBurst:
                    if (intRemaining == 0)
                    {
                        await OutOfAmmoAsync(token).ConfigureAwait(false);
                        return RuleResult.NotAllowed;
                    }
                    if (intRemaining >= objWeapon.LongBurst)
                    {
                        await objWeapon.SetAmmoRemaining(intRemaining - objWeapon.LongBurst, token).ConfigureAwait(false);
                        return RuleResult.Changed;
                    }
                    if (intRemaining == objWeapon.SingleShot)
                        return await AskEmptyClipAsync(objWeapon, "Message_NotEnoughAmmoSingleShot", token).ConfigureAwait(false);
                    if (intRemaining > objWeapon.ShortBurst)
                        return await AskEmptyClipAsync(objWeapon, "Message_NotEnoughAmmoLongBurstShort", token).ConfigureAwait(false);
                    if (intRemaining == objWeapon.ShortBurst)
                        return await AskEmptyClipAsync(objWeapon, "Message_NotEnoughAmmoShortBurst", token).ConfigureAwait(false);
                    return await AskEmptyClipAsync(objWeapon, "Message_NotEnoughAmmoShortBurstShort", token).ConfigureAwait(false);
                case FiringMode.FullBurst:
                case FiringMode.Suppressive:
                {
                    if (intRemaining == 0)
                    {
                        await OutOfAmmoAsync(token).ConfigureAwait(false);
                        return RuleResult.NotAllowed;
                    }
                    int intRounds = eMode == FiringMode.FullBurst ? objWeapon.FullBurst : objWeapon.Suppressive;
                    if (intRemaining >= intRounds)
                    {
                        await objWeapon.SetAmmoRemaining(intRemaining - intRounds, token).ConfigureAwait(false);
                        return RuleResult.Changed;
                    }
                    await MessageBoxes.ShowAsync(
                        await LanguageManager.GetStringAsync(eMode == FiringMode.FullBurst ? "Message_NotEnoughAmmoFullBurst" : "Message_NotEnoughAmmoSuppressiveFire", token: token).ConfigureAwait(false),
                        await LanguageManager.GetStringAsync("MessageTitle_NotEnoughAmmo", token: token).ConfigureAwait(false),
                        MessageButtons.OK, MessageIcon.Exclamation, token: token).ConfigureAwait(false);
                    return RuleResult.NotAllowed;
                }
                default:
                    return RuleResult.NotAllowed;
            }
        }

        private static async Task OutOfAmmoAsync(CancellationToken token)
        {
            await MessageBoxes.ShowAsync(
                await LanguageManager.GetStringAsync("Message_OutOfAmmo", token: token).ConfigureAwait(false),
                await LanguageManager.GetStringAsync("MessageTitle_OutOfAmmo", token: token).ConfigureAwait(false),
                MessageButtons.OK, MessageIcon.Exclamation, token: token).ConfigureAwait(false);
        }

        /// <summary>Not enough rounds for the burst: legacy asks whether to fire what is left (emptying the clip).</summary>
        private static async Task<RuleResult> AskEmptyClipAsync(Weapon objWeapon, string strMessageKey, CancellationToken token)
        {
            if (await MessageBoxes.ShowAsync(
                    await LanguageManager.GetStringAsync(strMessageKey, token: token).ConfigureAwait(false),
                    await LanguageManager.GetStringAsync("MessageTitle_NotEnoughAmmo", token: token).ConfigureAwait(false),
                    MessageButtons.YesNo, MessageIcon.Exclamation, token: token).ConfigureAwait(false) != MessageResult.Yes)
                return RuleResult.Cancelled;
            await objWeapon.SetAmmoRemaining(0, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        /// <summary>Where a weapon reloads from: the vehicle's gear for vehicle weapons (cmdReloadVehicleWeapon), the runner's gear otherwise.</summary>
        public static IAsyncCollection<Gear> AmmoSource(Character objCharacter, Weapon objWeapon)
            => objWeapon?.ParentVehicle != null ? (IAsyncCollection<Gear>)objWeapon.ParentVehicle.GearChildren : objCharacter.Gear;

        /// <summary>The magazine sizes the weapon can load (legacy Reload: "Count" list of the ReloadWeapon form).</summary>
        public static async Task<List<string>> GetReloadCountsAsync(Weapon objWeapon, CancellationToken token = default)
        {
            List<string> lstCount = new List<string>(1);
            string strAmmo = await objWeapon.CalculatedAmmoAsync(GlobalSettings.InvariantCultureInfo, GlobalSettings.DefaultLanguage, token).ConfigureAwait(false);
            if (strAmmo.IndexOfAny(Weapon.s_achrMultiplyOrAddSymbols) != -1
                || strAmmo.Contains(" or ", StringComparison.OrdinalIgnoreCase)
                || strAmmo.Contains("Special", StringComparison.OrdinalIgnoreCase)
                || strAmmo.Contains("External Source", StringComparison.OrdinalIgnoreCase))
            {
                string strWeaponAmmo = strAmmo.FastEscape("External Source", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
                strWeaponAmmo = strWeaponAmmo.FastEscapeOnceFromEnd(" + energy", eComparison: StringComparison.OrdinalIgnoreCase).Replace(" or belt", " or 250(belt)");
                foreach (string strLoop in strWeaponAmmo.SplitNoAlloc(" or ", StringSplitOptions.RemoveEmptyEntries, StringComparison.OrdinalIgnoreCase))
                    lstCount.Add(Weapon.AmmoCapacity(strLoop));
            }
            else
            {
                int intPos = strAmmo.IndexOf('(');
                lstCount.Add(intPos != -1 ? strAmmo.Substring(0, intPos) : strAmmo);
            }
            return lstCount;
        }

        /// <summary>Legacy ReloadWeapon form's entry text: "Name ×Qty (Rating N) (Parent @ Location) [plugins]".</summary>
        public static async Task<string> GetAmmoDisplayNameAsync(Gear objGear, CancellationToken token = default)
        {
            string strSpace = await LanguageManager.GetStringAsync("String_Space", token: token).ConfigureAwait(false);
            string strLeftPar = await LanguageManager.GetStringAsync("String_LeftPar", token: token).ConfigureAwait(false);
            string strRightPar = await LanguageManager.GetStringAsync("String_RightPar", token: token).ConfigureAwait(false);
            StringBuilder sbdName = new StringBuilder(await objGear.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false));
            sbdName.Append(strSpace).Append('×').Append(objGear.Quantity.ToString(GlobalSettings.InvariantCultureInfo));
            int intRating = await objGear.GetRatingAsync(token).ConfigureAwait(false);
            if (intRating > 0)
                sbdName.Append(strSpace).Append(strLeftPar)
                       .Append(string.Format(GlobalSettings.CultureInfo, await LanguageManager.GetStringAsync("Label_RatingFormat", token: token).ConfigureAwait(false),
                                             await LanguageManager.GetStringAsync(objGear.RatingLabel, token: token).ConfigureAwait(false)))
                       .Append(strSpace).Append(intRating.ToString(GlobalSettings.CultureInfo)).Append(strRightPar);
            if (objGear.Parent is Gear objParent)
            {
                string strParent = await objParent.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(strParent))
                {
                    sbdName.Append(strSpace).Append(strLeftPar).Append(strParent);
                    if (objParent.Location != null)
                        sbdName.Append(strSpace).Append('@').Append(strSpace).Append(await objParent.Location.GetCurrentDisplayNameAsync(token).ConfigureAwait(false));
                    sbdName.Append(strRightPar);
                }
            }
            else if (objGear.Location != null)
                sbdName.Append(strSpace).Append(strLeftPar).Append(await objGear.Location.GetCurrentDisplayNameAsync(token).ConfigureAwait(false)).Append(strRightPar);
            List<Gear> lstChildren = objGear.Children.ToList();
            if (lstChildren.Count > 0)
            {
                string strComma = await LanguageManager.GetStringAsync("String_Comma", token: token).ConfigureAwait(false) + strSpace;
                List<string> lstNames = new List<string>(lstChildren.Count);
                foreach (Gear objChild in lstChildren)
                    lstNames.Add(await objChild.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false));
                sbdName.Append(strSpace).Append('[').Append(string.Join(strComma, lstNames)).Append(']');
            }
            return sbdName.ToString().Trim();
        }

        /// <summary>
        /// cmdReloadWeapon_Click (Weapon.Reload): pick the ammunition and magazine size (the legacy ReloadWeapon form, asked here as
        /// SelectItem prompts) and load it. Weapons that use charges instead of ammo ask for the new number of charges.
        /// </summary>
        public static async Task<RuleResult> ReloadAsync(Character objCharacter, Weapon objWeapon, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objWeapon == null)
                throw new ArgumentNullException(nameof(objWeapon));
            List<string> lstCount = await GetReloadCountsAsync(objWeapon, token).ConfigureAwait(false);
            if (!objWeapon.RequireAmmo)
            {
                Clip objInternalClip = objWeapon.GetClip(objWeapon.ActiveAmmoSlot);
                if (objInternalClip == null)
                {
                    await objWeapon.RecreateInternalClipAsync(token).ConfigureAwait(false);
                    objInternalClip = objWeapon.GetClip(objWeapon.ActiveAmmoSlot);
                    if (objInternalClip == null)
                        return RuleResult.NotAllowed;
                }
                int intMaxAmmoCount = 0;
                foreach (string strAmmo in lstCount)
                {
                    if (int.TryParse(strAmmo, NumberStyles.Any, GlobalSettings.InvariantCultureInfo, out intMaxAmmoCount))
                        break;
                }
                int intCurrent = objInternalClip.Ammo;
                if (intMaxAmmoCount <= intCurrent)
                    return RuleResult.NotAllowed;
                SelectNumberRequest objNumber = new SelectNumberRequest(0)
                {
                    AllowCancel = true,
                    Maximum = intMaxAmmoCount,
                    Minimum = intCurrent,
                    Description = string.Format(GlobalSettings.CultureInfo,
                                                await LanguageManager.GetStringAsync("Message_SelectNumberOfCharges", token: token).ConfigureAwait(false),
                                                await objWeapon.GetCurrentDisplayNameAsync(token).ConfigureAwait(false))
                };
                if (await HostServices.Dialogs.ShowAsync(objNumber, HostServices.MainWindow, token).ConfigureAwait(false) != DialogOutcome.OK)
                    return RuleResult.Cancelled;
                objInternalClip.Ammo = objNumber.SelectedValue.ToInt32();
                return RuleResult.Changed;
            }

            IAsyncCollection<Gear> lstGears = AmmoSource(objCharacter, objWeapon);
            List<Gear> lstAmmo = objWeapon.GetAmmoReloadable(lstGears).ToList();
            string strCalculatedAmmo = await objWeapon.CalculatedAmmoAsync(GlobalSettings.InvariantCultureInfo, GlobalSettings.DefaultLanguage, token).ConfigureAwait(false);
            bool blnExternalSource = strCalculatedAmmo.Contains("External Source", StringComparison.OrdinalIgnoreCase);
            string strExternal = await LanguageManager.GetStringAsync("String_ExternalSource", token: token).ConfigureAwait(false);
            // Like legacy: a weapon that requires ammo needs some carried, even when it could also use an external source.
            if (lstAmmo.Count == 0)
            {
                await MessageBoxes.ShowAsync(
                    string.Format(GlobalSettings.CultureInfo, await LanguageManager.GetStringAsync("Message_OutOfAmmoType", token: token).ConfigureAwait(false),
                                  await objWeapon.GetCurrentDisplayNameAsync(token).ConfigureAwait(false)),
                    await LanguageManager.GetStringAsync("Message_OutOfAmmo", token: token).ConfigureAwait(false),
                    MessageButtons.OK, MessageIcon.Warning, token: token).ConfigureAwait(false);
                return RuleResult.NotAllowed;
            }
            List<ListItem> lstAmmoItems = new List<ListItem>(lstAmmo.Count + 1);
            foreach (Gear objGear in lstAmmo)
                lstAmmoItems.Add(new ListItem(objGear.InternalId, await GetAmmoDisplayNameAsync(objGear, token).ConfigureAwait(false)));
            if (blnExternalSource)
                lstAmmoItems.Add(new ListItem(string.Empty, strExternal));
            SelectItemRequest objAmmoPick = new SelectItemRequest
            {
                Description = await LanguageManager.GetStringAsync("Title_Reload", token: token).ConfigureAwait(false),
                SelectedItem = objWeapon.AmmoLoaded?.InternalId
            };
            objAmmoPick.SetGeneralItemsMode(lstAmmoItems);
            if (await HostServices.Dialogs.ShowAsync(objAmmoPick, HostServices.MainWindow, token).ConfigureAwait(false) != DialogOutcome.OK)
                return RuleResult.Cancelled;
            string strSelectedAmmo = objAmmoPick.SelectedItem ?? string.Empty;

            if (lstCount.Count == 0)
                return RuleResult.NotAllowed;
            string strCount = lstCount[0];
            if (lstCount.Count > 1)
            {
                SelectItemRequest objCountPick = new SelectItemRequest
                {
                    Description = await LanguageManager.GetStringAsync("Label_Qty", token: token).ConfigureAwait(false)
                };
                objCountPick.SetGeneralItemsMode(lstCount.Select(x => new ListItem(x, x)));
                if (await HostServices.Dialogs.ShowAsync(objCountPick, HostServices.MainWindow, token).ConfigureAwait(false) != DialogOutcome.OK)
                    return RuleResult.Cancelled;
                strCount = objCountPick.SelectedItem ?? strCount;
            }
            decimal decQty = ParseCount(strCount);
            Gear objSelected = string.IsNullOrEmpty(strSelectedAmmo) ? null : lstAmmo.Find(x => x.InternalId == strSelectedAmmo);
            if (objSelected == null && !string.IsNullOrEmpty(strSelectedAmmo))
                return RuleResult.NotAllowed;
            return await LoadAsync(objCharacter, objWeapon, objSelected, decQty, token).ConfigureAwait(false);
        }

        /// <summary>The number in a magazine size string ("30", "250(belt)", "10(c)").</summary>
        public static decimal ParseCount(string strCount)
        {
            if (string.IsNullOrEmpty(strCount))
                return 0;
            int i = 0;
            while (i < strCount.Length && (char.IsDigit(strCount[i]) || strCount[i] == '.'))
                ++i;
            return decimal.TryParse(strCount.Substring(0, i), NumberStyles.Any, GlobalSettings.InvariantCultureInfo, out decimal decReturn) ? decReturn : 0;
        }

        /// <summary>
        /// The loading part of Weapon.Reload once ammo and count are chosen: <paramref name="objSelectedAmmo"/> null loads from an
        /// external source. Spare clips/speed loaders are split off, a partly used stack is split so the loaded rounds become their own
        /// gear (deducted as the weapon fires), and identical ammo just tops up the loaded stack.
        /// </summary>
        public static async Task<RuleResult> LoadAsync(Character objCharacter, Weapon objWeapon, Gear objSelectedAmmo, decimal decQty, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objWeapon == null)
                throw new ArgumentNullException(nameof(objWeapon));
            if (decQty <= 0)
                return RuleResult.NotAllowed;
            IAsyncCollection<Gear> lstGears = AmmoSource(objCharacter, objWeapon);
            Gear objCurrentlyLoadedAmmo = objWeapon.AmmoLoaded;
            if (objSelectedAmmo == null)
            {
                Gear objExternalSource = new Gear(objCharacter);
                objExternalSource.Name = await LanguageManager.GetStringAsync("String_ExternalSource", token: token).ConfigureAwait(false);
                objExternalSource.SourceID = Guid.Empty;
                objSelectedAmmo = objExternalSource;
            }
            else
            {
                string strSelectedAmmo = objSelectedAmmo.InternalId;
                // If the Ammo is coming from a Spare Clip, reduce the container quantity instead of the plugin quantity.
                if (objSelectedAmmo.Parent is Gear objParent
                    && (objParent.Name.StartsWith("Spare Clip", StringComparison.Ordinal) || objParent.Name.StartsWith("Speed Loader", StringComparison.Ordinal))
                    && objParent.Quantity > 1)
                {
                    // Duplicate the clip into a new entry where we can directly deduct from the quantity as we fire
                    Gear objDuplicatedParent = new Gear(objCharacter);
                    try
                    {
                        await objDuplicatedParent.CopyAsync(objParent, token).ConfigureAwait(false);
                        await objDuplicatedParent.SetQuantityAsync(1, token).ConfigureAwait(false);
                        await lstGears.AddAsync(objDuplicatedParent, token).ConfigureAwait(false);
                        await objParent.SetQuantityAsync(objParent.Quantity - 1, token).ConfigureAwait(false);
                        Gear objNewSelectedAmmo = await objDuplicatedParent.Children.DeepFindByIdAsync(strSelectedAmmo, token: token).ConfigureAwait(false);
                        if (objNewSelectedAmmo == null)
                        {
                            objNewSelectedAmmo = new Gear(objCharacter);
                            try
                            {
                                await objNewSelectedAmmo.CopyAsync(objSelectedAmmo, token).ConfigureAwait(false);
                                await objDuplicatedParent.Children.AddAsync(objNewSelectedAmmo, token).ConfigureAwait(false);
                            }
                            catch
                            {
                                await objNewSelectedAmmo.DeleteGearAsync(token: CancellationToken.None).ConfigureAwait(false);
                                throw;
                            }
                        }
                        objSelectedAmmo = objNewSelectedAmmo;
                    }
                    catch
                    {
                        await objDuplicatedParent.DeleteGearAsync(token: CancellationToken.None).ConfigureAwait(false);
                        throw;
                    }
                }

                if (objCurrentlyLoadedAmmo != null && objSelectedAmmo.IsIdenticalToOtherGear(objCurrentlyLoadedAmmo))
                {
                    // Just top up the currently loaded ammo
                    decimal decTopUp = decQty - objCurrentlyLoadedAmmo.Quantity;
                    if (decTopUp > objSelectedAmmo.Quantity)
                    {
                        // We need more ammo for a full top-up than the quantity of gear, so just merge the gears and delete the old gear.
                        await objCurrentlyLoadedAmmo.SetQuantityAsync(objCurrentlyLoadedAmmo.Quantity - objSelectedAmmo.Quantity, token).ConfigureAwait(false);
                        await objSelectedAmmo.DeleteGearAsync(token: token).ConfigureAwait(false);
                        objWeapon.GetClip(objWeapon._intActiveAmmoSlot).Ammo = objCurrentlyLoadedAmmo.Quantity.ToInt32();
                    }
                    else
                    {
                        await objCurrentlyLoadedAmmo.SetQuantityAsync(decQty, token).ConfigureAwait(false);
                        await objSelectedAmmo.SetQuantityAsync(objSelectedAmmo.Quantity - decTopUp, token).ConfigureAwait(false);
                        objWeapon.GetClip(objWeapon._intActiveAmmoSlot).Ammo = decQty.ToInt32();
                    }
                    return RuleResult.Changed;
                }

                if (objSelectedAmmo.Quantity > decQty)
                {
                    // Duplicate the ammo into a new entry where we can directly deduct from the quantity as we fire
                    Gear objNewSelectedAmmo = new Gear(objCharacter);
                    try
                    {
                        await objNewSelectedAmmo.CopyAsync(objSelectedAmmo, token).ConfigureAwait(false);
                        await objNewSelectedAmmo.SetQuantityAsync(decQty, token).ConfigureAwait(false);
                        await lstGears.AddAsync(objNewSelectedAmmo, token).ConfigureAwait(false);
                        await objSelectedAmmo.SetQuantityAsync(objSelectedAmmo.Quantity - decQty, token).ConfigureAwait(false);
                        objSelectedAmmo = objNewSelectedAmmo;
                    }
                    catch
                    {
                        await objNewSelectedAmmo.DeleteGearAsync(token: CancellationToken.None).ConfigureAwait(false);
                        throw;
                    }
                }
                else if (decQty > objSelectedAmmo.Quantity)
                    decQty = objSelectedAmmo.Quantity;
            }

            await objWeapon.SetAmmoLoadedAsync(objSelectedAmmo, token).ConfigureAwait(false);
            objWeapon.GetClip(objWeapon._intActiveAmmoSlot).Ammo = decQty.ToInt32(); // Bypass AmmoRemaining so as not to alter the gear quantity
            return RuleResult.Changed;
        }

        /// <summary>cmdUnloadWeapon_Click: return the loaded rounds to the gear list (enabled when ammo is loaded and the clip is not empty).</summary>
        public static async Task<RuleResult> UnloadAsync(Character objCharacter, Weapon objWeapon, CancellationToken token = default)
        {
            if (!CanUnload(objWeapon))
                return RuleResult.NotAllowed;
            Gear objAmmo = await Weapon.UnloadGearAsync(AmmoSource(objCharacter, objWeapon), objWeapon.GetClip(objWeapon.ActiveAmmoSlot), token).ConfigureAwait(false);
            return objAmmo == null ? RuleResult.NotAllowed : RuleResult.Changed;
        }

        public static bool CanUnload(Weapon objWeapon)
            => UsesAmmo(objWeapon) && objWeapon.RequireAmmo && objWeapon.AmmoLoaded != null && objWeapon.AmmoRemaining != 0;

        // ---------- quantities ----------

        /// <summary>Decimal places the quantity prompt allows (cmdGearReduceQty_Click: nuyen uses the setting, currency 2, else whole items).</summary>
        public static async Task<int> GetQuantityDecimalsAsync(Character objCharacter, Gear objGear, CancellationToken token = default)
        {
            if (objGear.Name.StartsWith("Nuyen", StringComparison.Ordinal))
                return await (await objCharacter.GetSettingsAsync(token).ConfigureAwait(false)).GetMaxNuyenDecimalsAsync(token).ConfigureAwait(false);
            return objGear.Category == "Currency" ? 2 : 0;
        }

        /// <summary>cmdGearReduceQty_Click / btnDecreaseDrugQty_Click: ask how many to remove, then reduce.</summary>
        public static async Task<RuleResult> PromptReduceQuantityAsync(Character objCharacter, object objItem, CancellationToken token = default)
        {
            decimal decMaximum;
            int intDecimals = 0;
            switch (objItem)
            {
                case Gear objGear:
                    decMaximum = objGear.Quantity;
                    intDecimals = await GetQuantityDecimalsAsync(objCharacter, objGear, token).ConfigureAwait(false);
                    break;
                case Drug objDrug:
                    decMaximum = objDrug.Quantity;
                    break;
                default:
                    return RuleResult.NotAllowed;
            }
            SelectNumberRequest objNumber = new SelectNumberRequest(intDecimals)
            {
                Minimum = 0,
                Maximum = decMaximum,
                Description = await LanguageManager.GetStringAsync("String_ReduceGear", token: token).ConfigureAwait(false)
            };
            if (await HostServices.Dialogs.ShowAsync(objNumber, HostServices.MainWindow, token).ConfigureAwait(false) != DialogOutcome.OK)
                return RuleResult.Cancelled;
            return await ReduceQuantityAsync(objItem, objNumber.SelectedValue, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Remove <paramref name="decAmount"/> items after the legacy "reduce this quantity by {0}?" confirmation; gear reduced to nothing
        /// is removed (with the delete confirmation) like cmdGearReduceQty_Click.
        /// </summary>
        public static async Task<RuleResult> ReduceQuantityAsync(object objItem, decimal decAmount, CancellationToken token = default)
        {
            if (decAmount <= 0)
                return RuleResult.NotAllowed;
            switch (objItem)
            {
                case Gear objGear:
                    if (objGear.Quantity > decAmount)
                    {
                        if (!await ConfirmReduceAsync(decAmount, token).ConfigureAwait(false))
                            return RuleResult.Cancelled;
                        await objGear.SetQuantityAsync(objGear.Quantity - decAmount, token).ConfigureAwait(false);
                        return RuleResult.Changed;
                    }
                    return await objGear.RemoveAsync(token: token).ConfigureAwait(false) ? RuleResult.Changed : RuleResult.Cancelled;
                case Drug objDrug:
                    if (decAmount > objDrug.Quantity)
                        decAmount = objDrug.Quantity;
                    if (!await ConfirmReduceAsync(decAmount, token).ConfigureAwait(false))
                        return RuleResult.Cancelled;
                    objDrug.Quantity -= decAmount;
                    return RuleResult.Changed;
                default:
                    return RuleResult.NotAllowed;
            }
        }

        private static async Task<bool> ConfirmReduceAsync(decimal decAmount, CancellationToken token)
            => await CommonFunctions.ConfirmDeleteAsync(
                string.Format(GlobalSettings.CultureInfo, await LanguageManager.GetStringAsync("Message_ReduceQty", token: token).ConfigureAwait(false), decAmount),
                token).ConfigureAwait(false);

        /// <summary>
        /// cmdGearIncreaseQty_Click for the common case of stacking more of the same item: create mode just raises the quantity (the
        /// create sheet's quantity box); career mode buys them like legacy PickGear with "stack" ticked: the added cost (with the
        /// restricted/forbidden multipliers) is checked against nuyen and logged as "Purchased Gear" with an undoable expense.
        /// </summary>
        public static async Task<RuleResult> AddQuantityAsync(Character objCharacter, Gear objGear, decimal decAmount, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objGear == null)
                throw new ArgumentNullException(nameof(objGear));
            if (decAmount <= 0)
                return RuleResult.NotAllowed;
            if (!await objCharacter.GetCreatedAsync(token).ConfigureAwait(false))
            {
                await objGear.SetQuantityAsync(objGear.Quantity + decAmount, token).ConfigureAwait(false);
                return RuleResult.Changed;
            }
            decimal decOldCost = await objGear.GetTotalCostAsync(token).ConfigureAwait(false);
            await objGear.SetQuantityAsync(objGear.Quantity + decAmount, token).ConfigureAwait(false);
            decimal decCost = await objGear.GetTotalCostAsync(token).ConfigureAwait(false) - decOldCost;
            CharacterSettings objSettings = await objCharacter.GetSettingsAsync(token).ConfigureAwait(false);
            switch ((await objGear.TotalAvailTupleAsync(token: token).ConfigureAwait(false)).Suffix)
            {
                case 'R' when objSettings.MultiplyRestrictedCost:
                    decCost *= objSettings.RestrictedCostMultiplier;
                    break;
                case 'F' when objSettings.MultiplyForbiddenCost:
                    decCost *= objSettings.ForbiddenCostMultiplier;
                    break;
            }
            if (decCost > await objCharacter.GetNuyenAsync(token).ConfigureAwait(false))
            {
                await objGear.SetQuantityAsync(objGear.Quantity - decAmount, token).ConfigureAwait(false);
                await MessageBoxes.ShowAsync(
                    await LanguageManager.GetStringAsync("Message_NotEnoughNuyen", token: token).ConfigureAwait(false),
                    await LanguageManager.GetStringAsync("MessageTitle_NotEnoughNuyen", token: token).ConfigureAwait(false),
                    MessageButtons.OK, MessageIcon.Information, token: token).ConfigureAwait(false);
                return RuleResult.NotAllowed;
            }
            ExpenseLogEntry objExpense = new ExpenseLogEntry(objCharacter);
            objExpense.Create(decCost * -1,
                              await LanguageManager.GetStringAsync("String_ExpensePurchaseGear", token: token).ConfigureAwait(false)
                              + await LanguageManager.GetStringAsync("String_Space", token: token).ConfigureAwait(false)
                              + await objGear.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false),
                              ExpenseType.Nuyen, DateTime.Now);
            await objCharacter.ExpenseEntries.AddWithSortAsync(objExpense, token: token).ConfigureAwait(false);
            await objCharacter.ModifyNuyenAsync(-decCost, token).ConfigureAwait(false);
            ExpenseUndo objUndo = new ExpenseUndo();
            objUndo.CreateNuyen(NuyenExpenseType.AddGear, objGear.InternalId, decAmount);
            objExpense.Undo = objUndo;
            return RuleResult.Changed;
        }

        // ---------- armor ----------

        /// <summary>cmdArmorDecrease_Click (+1 damage) / cmdArmorIncrease_Click (-1 damage, never below 0).</summary>
        public static RuleResult ChangeArmorDamage(Armor objArmor, int intDelta)
        {
            if (objArmor == null || intDelta == 0)
                return RuleResult.NotAllowed;
            int intOld = objArmor.ArmorDamage;
            objArmor.ArmorDamage = intOld + intDelta;
            return objArmor.ArmorDamage == intOld ? RuleResult.NotAllowed : RuleResult.Changed;
        }

        // ---------- names / notes ----------

        /// <summary>The legacy rename prompt for the item, or null when legacy has no rename for it.</summary>
        public static string RenameDescriptionKey(object objItem)
        {
            switch (objItem)
            {
                case Weapon _:
                    return "String_WeaponName";
                case Gear _:
                    return "String_GearName";
                case Armor _:
                    return "String_ArmorName";
                case Vehicle _:
                    return "String_VehicleName";
                default:
                    return null;
            }
        }

        public static string GetCustomName(object objItem)
        {
            switch (objItem)
            {
                case Gear objGear:
                    return objGear.GearName;
                case IHasCustomName objNamed:
                    return objNamed.CustomName;
                default:
                    return string.Empty;
            }
        }

        /// <summary>tsWeaponName_Click / tsGearName_Click / tsArmorName_Click / tsVehicleName_Click: blank removes the custom name.</summary>
        public static RuleResult Rename(object objItem, string strName)
        {
            if (RenameDescriptionKey(objItem) == null)
                return RuleResult.NotAllowed;
            strName = strName ?? string.Empty;
            if (GetCustomName(objItem) == strName)
                return RuleResult.NotAllowed;
            if (objItem is Gear objGear)
                objGear.GearName = strName;
            else
                ((IHasCustomName)objItem).CustomName = strName;
            return RuleResult.Changed;
        }

        /// <summary>The same rename, asking for the name with the legacy SelectText prompt.</summary>
        public static async Task<RuleResult> PromptRenameAsync(object objItem, CancellationToken token = default)
        {
            string strKey = RenameDescriptionKey(objItem);
            if (strKey == null)
                return RuleResult.NotAllowed;
            SelectTextRequest objText = new SelectTextRequest
            {
                Description = await LanguageManager.GetStringAsync(strKey, token: token).ConfigureAwait(false),
                SelectedValue = GetCustomName(objItem)
            };
            if (await HostServices.Dialogs.ShowAsync(objText, HostServices.MainWindow, token).ConfigureAwait(false) != DialogOutcome.OK)
                return RuleResult.Cancelled;
            return Rename(objItem, objText.SelectedValue);
        }

        /// <summary>WriteNotes (CharacterShared.cs).</summary>
        public static async Task<RuleResult> SetNotesAsync(object objItem, string strNotes, CancellationToken token = default)
        {
            if (!(objItem is IHasNotes objNotes))
                return RuleResult.NotAllowed;
            strNotes = strNotes ?? string.Empty;
            if (await objNotes.GetNotesAsync(token).ConfigureAwait(false) == strNotes)
                return RuleResult.NotAllowed;
            await objNotes.SetNotesAsync(strNotes, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        // ---------- sell / remove ----------

        public static bool CanSell(object objItem) => objItem is ICanSell && !(objItem is VehicleMod objMod && objMod.Name == "Obsolete");

        public static bool CanRemove(object objItem) => objItem is ICanRemove && !(objItem is VehicleMod objMod && objMod.Name == "Obsolete");

        /// <summary>
        /// tsWeaponSell_Click / tsArmorSell_Click / tsCyberwareSell_Click / sellItemToolStripMenuItem_Click / tsVehicleSell_Click: the
        /// item's own SellAsync refunds <paramref name="decPercent"/>% of its cost into an expense entry after the delete confirmation.
        /// Plugin cyberware ("[*]" capacity inside a parent) cannot be removed on its own.
        /// </summary>
        public static async Task<RuleResult> SellAsync(object objItem, decimal decPercent, CancellationToken token = default)
        {
            if (!CanSell(objItem))
                return RuleResult.NotAllowed;
            if (decPercent < 0)
                return RuleResult.NotAllowed;
            if (objItem is Cyberware objCyberware && objCyberware.Capacity == "[*]" && await objCyberware.GetParentAsync(token).ConfigureAwait(false) != null)
            {
                await CannotRemoveCyberwareAsync(token).ConfigureAwait(false);
                return RuleResult.NotAllowed;
            }
            return await ((ICanSell)objItem).SellAsync(decPercent / 100.0m, GlobalSettings.ConfirmDelete, token).ConfigureAwait(false)
                ? RuleResult.Changed
                : RuleResult.Cancelled;
        }

        /// <summary>cmdDeleteWeapon_Click / cmdDeleteArmor_Click / cmdDeleteGear_Click ... (RemoveSelectedObject): remove without refund.</summary>
        public static async Task<RuleResult> RemoveAsync(object objItem, CancellationToken token = default)
        {
            if (!CanRemove(objItem))
                return RuleResult.NotAllowed;
            if (objItem is Cyberware objCyberware && objCyberware.Capacity == "[*]" && await objCyberware.GetParentAsync(token).ConfigureAwait(false) != null)
            {
                await CannotRemoveCyberwareAsync(token).ConfigureAwait(false);
                return RuleResult.NotAllowed;
            }
            return await ((ICanRemove)objItem).RemoveAsync(token: token).ConfigureAwait(false) ? RuleResult.Changed : RuleResult.Cancelled;
        }

        private static async Task CannotRemoveCyberwareAsync(CancellationToken token)
        {
            await MessageBoxes.ShowAsync(
                await LanguageManager.GetStringAsync("Message_CannotRemoveCyberware", token: token).ConfigureAwait(false),
                await LanguageManager.GetStringAsync("MessageTitle_CannotRemoveCyberware", token: token).ConfigureAwait(false),
                MessageButtons.OK, MessageIcon.Information, token: token).ConfigureAwait(false);
        }
    }
}
