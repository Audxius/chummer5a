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
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.XPath;
using Chummer.Backend.Attributes;
using Chummer.Backend.Enums;
using Chummer.Backend.Skills;
using Microsoft.VisualStudio.Threading;
using NLog;
using IAsyncDisposable = System.IAsyncDisposable;

namespace Chummer.Backend.Equipment
{
    /// <summary>
    /// UI (WinForms) members of <see cref="Chummer.Backend.Equipment.Weapon"/>, moved out of the engine for the native port.
    /// </summary>
    public static class WeaponUiExtensions
    {
        public static async Task Reload(this Chummer.Backend.Equipment.Weapon objThis, IAsyncCollection<Gear> lstGears, TreeView treGearView,
                                      CancellationToken token = default)
        {
            List<string> lstCount = new List<string>(1);
            string ammoString
                = await objThis.CalculatedAmmoAsync(GlobalSettings.InvariantCultureInfo, GlobalSettings.DefaultLanguage, token)
                    .ConfigureAwait(false);
            if (!objThis.RequireAmmo)
            {
                // For weapons that have ammo capacities but no requirement for ammo, these are charges
                // We treat this function differently for them, letting the character reload as many charges as they see fit

                Clip objInternalClip = objThis.GetClip(objThis.ActiveAmmoSlot);
                if (objInternalClip == null)
                {
                    await objThis.RecreateInternalClipAsync(token).ConfigureAwait(false);
                    objInternalClip = objThis.GetClip(objThis.ActiveAmmoSlot) ?? throw new InvalidOperationException(nameof(objInternalClip));
                }

                int intCurrentAmmoCount = objInternalClip.Ammo;

                // Determine which loading methods are available to the Weapon.
                if (ammoString.IndexOfAny(Weapon.s_achrMultiplyOrAddSymbols) != -1 ||
                    ammoString.Contains(" or ", StringComparison.OrdinalIgnoreCase) ||
                    ammoString.Contains("Special", StringComparison.OrdinalIgnoreCase) ||
                    ammoString.Contains("External Source", StringComparison.OrdinalIgnoreCase))
                {
                    string strWeaponAmmo = ammoString.FastEscape("External Source", StringComparison.OrdinalIgnoreCase);
                    strWeaponAmmo = strWeaponAmmo.ToLowerInvariant();
                    // Get rid of or belt, and + energy.
                    strWeaponAmmo = strWeaponAmmo.FastEscapeOnceFromEnd(" + energy", eComparison: StringComparison.OrdinalIgnoreCase)
                                                 .Replace(" or belt", " or 250(belt)");

                    foreach (string strAmmo in
                             strWeaponAmmo.SplitNoAlloc(" or ", StringSplitOptions.RemoveEmptyEntries, StringComparison.OrdinalIgnoreCase))
                    {
                        lstCount.Add(Weapon.AmmoCapacity(strAmmo));
                    }
                }
                else
                {
                    // Nothing weird in the ammo string, so just use the number given.
                    string strAmmo = ammoString;
                    int intPos = strAmmo.IndexOf('(');
                    if (intPos != -1)
                        strAmmo = strAmmo.Substring(0, intPos);
                    lstCount.Add(strAmmo);
                }

                int intMaxAmmoCount = 0;

                foreach (string strAmmo in lstCount)
                {
                    if (int.TryParse(strAmmo, NumberStyles.Any, GlobalSettings.InvariantCultureInfo, out intMaxAmmoCount))
                    {
                        break;
                    }
                }

                if (intMaxAmmoCount <= intCurrentAmmoCount)
                    return;

                string strDescription = StringExtensions.FastFormat(
                    await LanguageManager.GetStringAsync("Message_SelectNumberOfCharges", token: token)
                                         .ConfigureAwait(false),
                    await objThis.GetCurrentDisplayNameAsync(token).ConfigureAwait(false));
                using (ThreadSafeForm<SelectNumber> frmNewAmmoCount = await ThreadSafeForm<SelectNumber>.GetAsync(
                           () => new SelectNumber(0)
                           {
                               AllowCancel = true,
                               Maximum = intMaxAmmoCount,
                               Minimum = intCurrentAmmoCount,
                               Description = strDescription
                           }, token).ConfigureAwait(false))
                {
                    if (await frmNewAmmoCount.ShowDialogSafeAsync(objThis._objCharacter, token).ConfigureAwait(false)
                        != DialogResult.OK)
                        return;

                    objInternalClip.Ammo = frmNewAmmoCount.MyForm.SelectedValue.ToInt32();
                }

                return;
            }

            bool blnExternalSource = false;
            List<Gear> lstAmmo = new List<Gear>(1);
            // Determine which loading methods are available to the Weapon.
            if (ammoString.IndexOfAny(Weapon.s_achrMultiplyOrAddSymbols) != -1 ||
                ammoString.Contains(" or ", StringComparison.OrdinalIgnoreCase) ||
                ammoString.Contains("Special", StringComparison.OrdinalIgnoreCase) ||
                ammoString.Contains("External Source", StringComparison.OrdinalIgnoreCase))
            {
                string strWeaponAmmo = ammoString;
                if (strWeaponAmmo.Contains("External Source", StringComparison.OrdinalIgnoreCase))
                {
                    blnExternalSource = true;
                    strWeaponAmmo = strWeaponAmmo.FastEscape("External Source", StringComparison.OrdinalIgnoreCase);
                }

                strWeaponAmmo = strWeaponAmmo.ToLowerInvariant();
                // Get rid of or belt, and + energy.
                strWeaponAmmo = strWeaponAmmo.FastEscapeOnceFromEnd(" + energy", eComparison: StringComparison.OrdinalIgnoreCase)
                                             .Replace(" or belt", " or 250(belt)");

                foreach (string strAmmo in
                         strWeaponAmmo.SplitNoAlloc(" or ", StringSplitOptions.RemoveEmptyEntries, StringComparison.OrdinalIgnoreCase))
                {
                    lstCount.Add(Weapon.AmmoCapacity(strAmmo));
                }
            }
            else
            {
                // Nothing weird in the ammo string, so just use the number given.
                string strAmmo = ammoString;
                int intPos = strAmmo.IndexOf('(');
                if (intPos != -1)
                    strAmmo = strAmmo.Substring(0, intPos);
                lstCount.Add(strAmmo);
            }

            Gear objExternalSource = null;
            if (blnExternalSource)
            {
                lstCount.Add(await LanguageManager.GetStringAsync("String_ExternalSource", token: token)
                                                  .ConfigureAwait(false));
                objExternalSource = new Gear(objThis._objCharacter);
                try
                {
                    objExternalSource.Name = await LanguageManager.GetStringAsync("String_ExternalSource", token: token)
                                                .ConfigureAwait(false);
                    objExternalSource.SourceID = Guid.Empty;
                }
                catch
                {
                    await objExternalSource.DeleteGearAsync(token: CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
            }

            if (objThis.RequireAmmo)
            {
                lstAmmo.AddRange(objThis.GetAmmoReloadable(lstGears));
                // Make sure the character has some form of Ammunition for this Weapon.
                if (lstAmmo.Count == 0)
                {
                    await MessageBoxes.ShowAsync(StringExtensions.FastFormat(
                            await LanguageManager
                                .GetStringAsync("Message_OutOfAmmoType", token: token)
                                .ConfigureAwait(false),
                            await objThis.GetCurrentDisplayNameAsync(token).ConfigureAwait(false)),
                        await LanguageManager.GetStringAsync("Message_OutOfAmmo", token: token)
                            .ConfigureAwait(false),
                        icon: MessageIcon.Warning, token: token).ConfigureAwait(false);
                    return;
                }
            }

            if (objExternalSource != null)
                lstAmmo.Add(objExternalSource);

            // Show the Ammunition Selection window.
            using (ThreadSafeForm<ReloadWeapon> frmReloadWeapon = await ThreadSafeForm<ReloadWeapon>.GetAsync(
                       () => new ReloadWeapon(objThis)
                       {
                           Ammo = lstAmmo,
                           Count = lstCount
                       }, token).ConfigureAwait(false))
            {
                if (await frmReloadWeapon.ShowDialogSafeAsync(objThis._objCharacter, token).ConfigureAwait(false)
                    != DialogResult.OK)
                    return;

                Gear objCurrentlyLoadedAmmo = objThis.AmmoLoaded;
                Gear objSelectedAmmo;
                decimal decQty = await frmReloadWeapon.MyForm.GetSelectedCountAsync(token).ConfigureAwait(false);
                string strSelectedAmmo = await frmReloadWeapon.MyForm.GetSelectedAmmoAsync(token).ConfigureAwait(false);
                // If an External Source is not being used, consume ammo.
                if (strSelectedAmmo != objExternalSource?.InternalId)
                {
                    objSelectedAmmo = await lstGears.DeepFindByIdAsync(strSelectedAmmo, token: token).ConfigureAwait(false);

                    // If the Ammo is coming from a Spare Clip, reduce the container quantity instead of the plugin quantity.
                    if (objSelectedAmmo.Parent is Gear objParent &&
                        (objParent.Name.StartsWith("Spare Clip", StringComparison.Ordinal)
                         || objParent.Name.StartsWith("Speed Loader", StringComparison.Ordinal)))
                    {
                        if (objParent.Quantity > 1)
                        {
                            // Duplicate the clip into a new entry where we can directly deduct from the quantity as we fire
                            Gear objDuplicatedParent = new Gear(objThis._objCharacter);
                            try
                            {
                                await objDuplicatedParent.CopyAsync(objParent, token).ConfigureAwait(false);
                                await objDuplicatedParent.SetQuantityAsync(1, token).ConfigureAwait(false);
                                await lstGears.AddAsync(objDuplicatedParent, token).ConfigureAwait(false);
                                await objParent.SetQuantityAsync(objParent.Quantity - 1, token).ConfigureAwait(false);
                                Gear objNewSelectedAmmo
                                    = await objDuplicatedParent.Children.DeepFindByIdAsync(strSelectedAmmo, token: token).ConfigureAwait(false);
                                if (objNewSelectedAmmo == null)
                                {
                                    objNewSelectedAmmo = new Gear(objThis._objCharacter);
                                    try
                                    {
                                        await objNewSelectedAmmo.CopyAsync(objSelectedAmmo, token).ConfigureAwait(false);
                                        await objDuplicatedParent.Children.AddAsync(objNewSelectedAmmo, token)
                                                                 .ConfigureAwait(false);
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

                        string strParentText = await objParent.GetCurrentDisplayNameAsync(token).ConfigureAwait(false);
                        await treGearView.DoThreadSafeAsync(x =>
                        {
                            TreeNode objNode = x.FindNode(objParent.InternalId);
                            if (objNode != null)
                                objNode.Text = strParentText;
                        }, token: token).ConfigureAwait(false);
                    }

                    if (objSelectedAmmo.IsIdenticalToOtherGear(objCurrentlyLoadedAmmo))
                    {
                        // Just top up the currently loaded ammo
                        decimal decTopUp = decQty - objCurrentlyLoadedAmmo.Quantity;
                        if (decTopUp > objSelectedAmmo.Quantity)
                        {
                            // We need more ammo for a full top-up than the quantity of gear, so just merge the gears and delete the old gear.
                            await objCurrentlyLoadedAmmo.SetQuantityAsync(objCurrentlyLoadedAmmo.Quantity - objSelectedAmmo.Quantity, token).ConfigureAwait(false);
                            await objSelectedAmmo.DeleteGearAsync(token: token).ConfigureAwait(false);
                            objThis.GetClip(objThis._intActiveAmmoSlot).Ammo
                                = objCurrentlyLoadedAmmo.Quantity
                                                        .ToInt32(); // Bypass AmmoRemaining so as not to alter the gear quantity
                        }
                        else
                        {
                            await objCurrentlyLoadedAmmo.SetQuantityAsync(decQty, token).ConfigureAwait(false);
                            await objSelectedAmmo.SetQuantityAsync(objSelectedAmmo.Quantity - decTopUp, token).ConfigureAwait(false);
                            string strCurrentlyLoadedText = await objCurrentlyLoadedAmmo
                                                                  .GetCurrentDisplayNameAsync(token)
                                                                  .ConfigureAwait(false);
                            string strSelectedText = await objSelectedAmmo
                                                           .GetCurrentDisplayNameAsync(token)
                                                           .ConfigureAwait(false);
                            await treGearView.DoThreadSafeAsync(x =>
                            {
                                // Refresh the Gear tree.
                                TreeNode objSelectedNode = x.FindNode(objCurrentlyLoadedAmmo.InternalId);
                                if (objSelectedNode != null)
                                    objSelectedNode.Text = strCurrentlyLoadedText;
                                objSelectedNode = x.FindNode(objSelectedAmmo.InternalId);
                                if (objSelectedNode != null)
                                    objSelectedNode.Text = strSelectedText;
                            }, token: token).ConfigureAwait(false);
                            objThis.GetClip(objThis._intActiveAmmoSlot).Ammo
                                = decQty.ToInt32(); // Bypass AmmoRemaining so as not to alter the gear quantity
                        }

                        return;
                    }

                    if (objSelectedAmmo.Quantity > decQty)
                    {
                        // Duplicate the ammo into a new entry where we can directly deduct from the quantity as we fire
                        Gear objNewSelectedAmmo = new Gear(objThis._objCharacter);
                        try
                        {
                            await objNewSelectedAmmo.CopyAsync(objSelectedAmmo, token).ConfigureAwait(false);
                            await objNewSelectedAmmo.SetQuantityAsync(decQty, token).ConfigureAwait(false);
                            await lstGears.AddAsync(objNewSelectedAmmo, token).ConfigureAwait(false);
                            await objSelectedAmmo.SetQuantityAsync(objSelectedAmmo.Quantity - decQty, token).ConfigureAwait(false);
                            string strId2 = objSelectedAmmo.InternalId;
                            string strText2 = await objSelectedAmmo.GetCurrentDisplayNameAsync(token).ConfigureAwait(false);
                            await treGearView.DoThreadSafeAsync(x =>
                            {
                                // Refresh the Gear tree.
                                TreeNode objSelectedNode = x.FindNode(strId2);
                                if (objSelectedNode != null)
                                    objSelectedNode.Text = strText2;
                            }, token: token).ConfigureAwait(false);
                            objSelectedAmmo = objNewSelectedAmmo;
                        }
                        catch
                        {
                            await objNewSelectedAmmo.DeleteGearAsync(token: CancellationToken.None).ConfigureAwait(false);
                            throw;
                        }
                    }
                    else if (decQty > objSelectedAmmo.Quantity)
                    {
                        decQty = objSelectedAmmo.Quantity;
                    }

                    string strId = objSelectedAmmo.InternalId;
                    string strText = await objSelectedAmmo.GetCurrentDisplayNameAsync(token).ConfigureAwait(false);
                    await treGearView.DoThreadSafeAsync(x =>
                    {
                        // Refresh the Gear tree.
                        TreeNode objSelectedNode = x.FindNode(strId);
                        if (objSelectedNode != null)
                            objSelectedNode.Text = strText;
                    }, token: token).ConfigureAwait(false);
                }
                else
                {
                    objSelectedAmmo = objExternalSource;
                }

                await objThis.SetAmmoLoadedAsync(objSelectedAmmo, token).ConfigureAwait(false);
                if (objCurrentlyLoadedAmmo != objSelectedAmmo)
                {
                    if (objCurrentlyLoadedAmmo != null)
                    {
                        string strId = objCurrentlyLoadedAmmo.InternalId;
                        string strText = await objCurrentlyLoadedAmmo.GetCurrentDisplayNameAsync(token)
                            .ConfigureAwait(false);
                        if (objSelectedAmmo != null)
                        {
                            string strId2 = objSelectedAmmo.InternalId;
                            string strText2 = await objSelectedAmmo.GetCurrentDisplayNameAsync(token)
                                .ConfigureAwait(false);
                            await treGearView.DoThreadSafeAsync(x =>
                            {
                                // Refresh the Gear tree.
                                TreeNode objSelectedNode = x.FindNode(strId);
                                if (objSelectedNode != null)
                                    objSelectedNode.Text = strText;
                                objSelectedNode = x.FindNode(strId2);
                                if (objSelectedNode != null)
                                    objSelectedNode.Text = strText2;
                            }, token: token).ConfigureAwait(false);
                        }
                        else
                        {
                            await treGearView.DoThreadSafeAsync(x =>
                            {
                                // Refresh the Gear tree.
                                TreeNode objSelectedNode = x.FindNode(strId);
                                if (objSelectedNode != null)
                                    objSelectedNode.Text = strText;
                            }, token: token).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        string strId = objSelectedAmmo.InternalId;
                        string strText = await objSelectedAmmo.GetCurrentDisplayNameAsync(token)
                            .ConfigureAwait(false);
                        await treGearView.DoThreadSafeAsync(x =>
                        {
                            // Refresh the Gear tree.
                            TreeNode objSelectedNode = x.FindNode(strId);
                            if (objSelectedNode != null)
                                objSelectedNode.Text = strText;
                        }, token: token).ConfigureAwait(false);
                    }
                }

                objThis.GetClip(objThis._intActiveAmmoSlot).Ammo
                    = decQty.ToInt32(); // Bypass AmmoRemaining so as not to alter the gear quantity
            }
        }

        public static async Task Unload(this Chummer.Backend.Equipment.Weapon objThis, IAsyncCollection<Gear> lstGears, TreeView treGearView, CancellationToken token = default)
        {
            Clip objClip = objThis.GetClip(objThis.ActiveAmmoSlot);
            Gear objAmmo = await Weapon.UnloadGearAsync(lstGears, objClip, token).ConfigureAwait(false);
            if (objAmmo == null)
                return;
            string strText = await objAmmo.GetCurrentDisplayNameAsync(token).ConfigureAwait(false);
            await treGearView.DoThreadSafeAsync(x =>
            {
                // Refresh the Gear tree.
                TreeNode objSelectedNode = x.FindNode(objAmmo.InternalId);
                if (objSelectedNode != null)
                    objSelectedNode.Text = strText;
            }, token: token).ConfigureAwait(false);
        }

        /// <summary>
        /// Add a Weapon to the TreeView.
        /// </summary>
        /// <param name="cmsWeapon">ContextMenuStrip for the Weapon Node.</param>
        /// <param name="cmsWeaponAccessory">ContextMenuStrip for Vehicle Accessory Nodes.</param>
        /// <param name="cmsWeaponAccessoryGear">ContextMenuStrip for Vehicle Weapon Accessory Gear Nodes.</param>
        /// <param name="token">Cancellation token to listen to.</param>
        public static async Task<TreeNode> CreateTreeNode(this Chummer.Backend.Equipment.Weapon objThis, ContextMenuStrip cmsWeapon, ContextMenuStrip cmsWeaponAccessory, ContextMenuStrip cmsWeaponAccessoryGear, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if ((objThis.Cyberware || objThis.Category == "Gear" || objThis.Category.StartsWith("Quality", StringComparison.Ordinal) || !string.IsNullOrEmpty(objThis.ParentID)) && !string.IsNullOrEmpty(objThis.Source)
                && !await (await objThis._objCharacter.GetSettingsAsync(token).ConfigureAwait(false)).BookEnabledAsync(objThis.Source, token).ConfigureAwait(false))
                return null;

            TreeNode objNode = new TreeNode
            {
                Name = objThis.InternalId,
                Text = await objThis.GetCurrentDisplayNameAsync(token).ConfigureAwait(false),
                Tag = objThis,
                ContextMenuStrip = cmsWeapon,
                ForeColor = await objThis.GetPreferredColorAsync(token).ConfigureAwait(false),
                ToolTipText = (await objThis.GetNotesAsync(token).ConfigureAwait(false)).WordWrap()
            };

            TreeNodeCollection lstChildNodes = objNode.Nodes;
            // Add Underbarrel Weapons.
            await objThis.UnderbarrelWeapons.ForEachAsync(async (objUnderbarrelWeapon, t) =>
            {
                TreeNode objLoopNode =
                    await objUnderbarrelWeapon.CreateTreeNode(cmsWeapon, cmsWeaponAccessory, cmsWeaponAccessoryGear,
                        t).ConfigureAwait(false);
                if (objLoopNode != null)
                    lstChildNodes.Add(objLoopNode);
            }, token).ConfigureAwait(false);
            // Add attached Weapon Accessories.
            await objThis.WeaponAccessories.ForEachAsync(async (objAccessory, t) =>
            {
                TreeNode objLoopNode = await objAccessory.CreateTreeNode(cmsWeaponAccessory, cmsWeaponAccessoryGear, t).ConfigureAwait(false);
                if (objLoopNode != null)
                    lstChildNodes.Add(objLoopNode);
            }, token).ConfigureAwait(false);

            if (objNode.GetNodeCount(false) > 0)
                objNode.Expand();

            return objNode;
        }

        public static void SetupChildrenWeaponsCollectionChanged(this Chummer.Backend.Equipment.Weapon objThis, bool blnAdd, TreeView treWeapons, ContextMenuStrip cmsWeapon = null, ContextMenuStrip cmsWeaponAccessory = null, ContextMenuStrip cmsWeaponAccessoryGear = null, AsyncNotifyCollectionChangedEventHandler funcMakeDirty = null)
        {
            if (blnAdd)
            {
                Task FuncUnderbarrelWeaponsBeforeClearToAdd(object _, NotifyCollectionChangedEventArgs y,
                    CancellationToken innerToken = default) =>
                    objThis.RefreshChildrenWeaponsClearBindings(treWeapons, y, innerToken);

                Task FuncUnderbarrelWeaponsToAdd(object _, NotifyCollectionChangedEventArgs y, CancellationToken innerToken = default) =>
                    objThis.RefreshChildrenWeapons(treWeapons, cmsWeapon, cmsWeaponAccessory, cmsWeaponAccessoryGear,
                        y, funcMakeDirty, token: innerToken);

                Task FuncWeaponAccessoriesBeforeClearToAdd(object _, NotifyCollectionChangedEventArgs y,
                    CancellationToken innerToken = default) =>
                    objThis.RefreshWeaponAccessoriesClearBindings(treWeapons, y, innerToken);

                Task FuncWeaponAccessoriesToAdd(object _, NotifyCollectionChangedEventArgs y, CancellationToken innerToken = default) =>
                    objThis.RefreshWeaponAccessories(treWeapons, cmsWeaponAccessory, cmsWeaponAccessoryGear,
                        t => objThis.UnderbarrelWeapons.GetCountAsync(t), y, funcMakeDirty, token: innerToken);

                objThis.UnderbarrelWeapons.AddTaggedBeforeClearCollectionChanged(treWeapons,
                    FuncUnderbarrelWeaponsBeforeClearToAdd);
                objThis.UnderbarrelWeapons.AddTaggedCollectionChanged(treWeapons, FuncUnderbarrelWeaponsToAdd);
                objThis.WeaponAccessories.AddTaggedBeforeClearCollectionChanged(treWeapons,
                    FuncWeaponAccessoriesBeforeClearToAdd);
                objThis.WeaponAccessories.AddTaggedCollectionChanged(treWeapons, FuncWeaponAccessoriesToAdd);
                if (funcMakeDirty != null)
                {
                    objThis.UnderbarrelWeapons.AddTaggedCollectionChanged(treWeapons, funcMakeDirty);
                    objThis.WeaponAccessories.AddTaggedCollectionChanged(treWeapons, funcMakeDirty);
                }
                foreach (Weapon objChild in objThis.UnderbarrelWeapons)
                {
                    objChild.SetupChildrenWeaponsCollectionChanged(true, treWeapons, cmsWeapon, cmsWeaponAccessory, cmsWeaponAccessoryGear, funcMakeDirty);
                }

                foreach (WeaponAccessory objChild in objThis.WeaponAccessories)
                {
                    Task FuncWeaponAccessoryGearBeforeClearToAdd(object _, NotifyCollectionChangedEventArgs y,
                        CancellationToken innerToken = default) =>
                        objThis.RefreshChildrenGearsClearBindings(treWeapons, y, innerToken);

                    Task FuncWeaponAccessoryGearToAdd(object _, NotifyCollectionChangedEventArgs y,
                        CancellationToken innerToken = default) =>
                        objChild.RefreshChildrenGears(treWeapons, cmsWeaponAccessoryGear, null, y, funcMakeDirty,
                            token: innerToken);

                    TaggedObservableCollection<Gear> lstGearChildren = objChild.GearChildren;
                    lstGearChildren.AddTaggedBeforeClearCollectionChanged(treWeapons,
                        FuncWeaponAccessoryGearBeforeClearToAdd);
                    lstGearChildren.AddTaggedCollectionChanged(treWeapons, FuncWeaponAccessoryGearToAdd);
                    if (funcMakeDirty != null)
                        lstGearChildren.AddTaggedCollectionChanged(treWeapons, funcMakeDirty);
                    foreach (Gear objGear in lstGearChildren)
                        objGear.SetupChildrenGearsCollectionChanged(true, treWeapons, cmsWeaponAccessoryGear, null, funcMakeDirty);
                }
            }
            else
            {
                objThis.UnderbarrelWeapons.RemoveTaggedAsyncBeforeClearCollectionChanged(treWeapons);
                objThis.UnderbarrelWeapons.RemoveTaggedAsyncCollectionChanged(treWeapons);
                objThis.WeaponAccessories.RemoveTaggedAsyncBeforeClearCollectionChanged(treWeapons);
                objThis.WeaponAccessories.RemoveTaggedAsyncCollectionChanged(treWeapons);
                foreach (Weapon objChild in objThis.UnderbarrelWeapons)
                {
                    objChild.SetupChildrenWeaponsCollectionChanged(false, treWeapons);
                }
                foreach (WeaponAccessory objChild in objThis.WeaponAccessories)
                {
                    TaggedObservableCollection<Gear> lstGearChildren = objChild.GearChildren;
                    lstGearChildren.RemoveTaggedAsyncBeforeClearCollectionChanged(treWeapons);
                    lstGearChildren.RemoveTaggedAsyncCollectionChanged(treWeapons);
                    foreach (Gear objGear in lstGearChildren)
                        objGear.SetupChildrenGearsCollectionChanged(false, treWeapons);
                }
            }
        }

        public static async Task SetupChildrenWeaponsCollectionChangedAsync(this Chummer.Backend.Equipment.Weapon objThis, bool blnAdd, TreeView treWeapons, ContextMenuStrip cmsWeapon = null, ContextMenuStrip cmsWeaponAccessory = null, ContextMenuStrip cmsWeaponAccessoryGear = null, AsyncNotifyCollectionChangedEventHandler funcMakeDirty = null, CancellationToken token = default)
        {
            if (blnAdd)
            {
                Task FuncUnderbarrelWeaponsBeforeClearToAdd(object _, NotifyCollectionChangedEventArgs y,
                    CancellationToken innerToken = default) =>
                    objThis.RefreshChildrenWeaponsClearBindings(treWeapons, y, innerToken);

                Task FuncUnderbarrelWeaponsToAdd(object _, NotifyCollectionChangedEventArgs y, CancellationToken innerToken = default) =>
                    objThis.RefreshChildrenWeapons(treWeapons, cmsWeapon, cmsWeaponAccessory, cmsWeaponAccessoryGear,
                        y, funcMakeDirty, token: innerToken);

                Task FuncWeaponAccessoriesBeforeClearToAdd(object _, NotifyCollectionChangedEventArgs y,
                    CancellationToken innerToken = default) =>
                    objThis.RefreshWeaponAccessoriesClearBindings(treWeapons, y, innerToken);

                Task FuncWeaponAccessoriesToAdd(object _, NotifyCollectionChangedEventArgs y, CancellationToken innerToken = default) =>
                    objThis.RefreshWeaponAccessories(treWeapons, cmsWeaponAccessory, cmsWeaponAccessoryGear,
                        t => objThis.UnderbarrelWeapons.GetCountAsync(t), y, funcMakeDirty, token: innerToken);

                objThis.UnderbarrelWeapons.AddTaggedBeforeClearCollectionChanged(treWeapons,
                    FuncUnderbarrelWeaponsBeforeClearToAdd);
                objThis.UnderbarrelWeapons.AddTaggedCollectionChanged(treWeapons, FuncUnderbarrelWeaponsToAdd);
                objThis.WeaponAccessories.AddTaggedBeforeClearCollectionChanged(treWeapons,
                    FuncWeaponAccessoriesBeforeClearToAdd);
                objThis.WeaponAccessories.AddTaggedCollectionChanged(treWeapons, FuncWeaponAccessoriesToAdd);
                if (funcMakeDirty != null)
                {
                    objThis.UnderbarrelWeapons.AddTaggedCollectionChanged(treWeapons, funcMakeDirty);
                    objThis.WeaponAccessories.AddTaggedCollectionChanged(treWeapons, funcMakeDirty);
                }
                await objThis.UnderbarrelWeapons.ForEachWithSideEffectsAsync(
                    (objChild, t) => objChild.SetupChildrenWeaponsCollectionChangedAsync(true, treWeapons, cmsWeapon, cmsWeaponAccessory, cmsWeaponAccessoryGear, funcMakeDirty, t),
                    token).ConfigureAwait(false);

                await objThis.WeaponAccessories.ForEachWithSideEffectsAsync(async (objChild, t1) =>
                {
                    Task FuncWeaponAccessoryGearBeforeClearToAdd(object _, NotifyCollectionChangedEventArgs y,
                        CancellationToken innerToken = default) =>
                        objThis.RefreshChildrenGearsClearBindings(treWeapons, y, innerToken);

                    Task FuncWeaponAccessoryGearToAdd(object _, NotifyCollectionChangedEventArgs y,
                        CancellationToken innerToken = default) =>
                        objChild.RefreshChildrenGears(treWeapons, cmsWeaponAccessoryGear, null, y, funcMakeDirty,
                            token: innerToken);

                    TaggedObservableCollection<Gear> lstGearChildren = objChild.GearChildren;
                    lstGearChildren.AddTaggedBeforeClearCollectionChanged(treWeapons,
                        FuncWeaponAccessoryGearBeforeClearToAdd);
                    lstGearChildren.AddTaggedCollectionChanged(treWeapons, FuncWeaponAccessoryGearToAdd);
                    if (funcMakeDirty != null)
                        lstGearChildren.AddTaggedCollectionChanged(treWeapons, funcMakeDirty);
                    await lstGearChildren.ForEachWithSideEffectsAsync(
                        (objGear, t2) => objGear.SetupChildrenGearsCollectionChangedAsync(true, treWeapons,
                            cmsWeaponAccessoryGear, null, funcMakeDirty, t2),
                        t1).ConfigureAwait(false);
                }, token).ConfigureAwait(false);
            }
            else
            {
                await objThis.UnderbarrelWeapons.RemoveTaggedAsyncBeforeClearCollectionChangedAsync(treWeapons, token).ConfigureAwait(false);
                await objThis.UnderbarrelWeapons.RemoveTaggedAsyncCollectionChangedAsync(treWeapons, token).ConfigureAwait(false);
                await objThis.WeaponAccessories.RemoveTaggedAsyncBeforeClearCollectionChangedAsync(treWeapons, token).ConfigureAwait(false);
                await objThis.WeaponAccessories.RemoveTaggedAsyncCollectionChangedAsync(treWeapons, token).ConfigureAwait(false);
                await objThis.UnderbarrelWeapons.ForEachWithSideEffectsAsync(
                    (objChild, t) => objChild.SetupChildrenWeaponsCollectionChangedAsync(false, treWeapons, token: t),
                    token).ConfigureAwait(false);
                await objThis.WeaponAccessories.ForEachWithSideEffectsAsync(async (objChild, t1) =>
                {
                    TaggedObservableCollection<Gear> lstGearChildren = objChild.GearChildren;
                    await lstGearChildren.RemoveTaggedAsyncBeforeClearCollectionChangedAsync(treWeapons, t1).ConfigureAwait(false);
                    await lstGearChildren.RemoveTaggedAsyncCollectionChangedAsync(treWeapons, t1).ConfigureAwait(false);
                    await lstGearChildren.ForEachWithSideEffectsAsync(
                        (objGear, t2) => objGear.SetupChildrenGearsCollectionChangedAsync(false, treWeapons, token: t2),
                        t1).ConfigureAwait(false);
                }, token).ConfigureAwait(false);
            }
        }
    }
}
