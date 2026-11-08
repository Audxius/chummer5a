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
using Chummer.Annotations;
using NLog;
using TreeNode = System.Windows.Forms.TreeNode;
using TreeNodeCollection = System.Windows.Forms.TreeNodeCollection;

namespace Chummer.Backend.Equipment
{
    /// <summary>
    /// UI (WinForms) members of <see cref="Chummer.Backend.Equipment.Vehicle"/>, moved out of the engine for the native port.
    /// </summary>
    public static class VehicleUiExtensions
    {
        /// <summary>
        /// Add a Vehicle to the TreeView.
        /// </summary>
        /// <param name="cmsVehicle">ContextMenuStrip for the Vehicle Node.</param>
        /// <param name="cmsVehicleLocation">ContextMenuStrip for Vehicle Location Nodes.</param>
        /// <param name="cmsVehicleWeapon">ContextMenuStrip for Vehicle Weapon Nodes.</param>
        /// <param name="cmsWeaponAccessory">ContextMenuStrip for Vehicle Weapon Accessory Nodes.</param>
        /// <param name="cmsWeaponAccessoryGear">ContextMenuStrip for Gear in Vehicle Weapon Accessory Nodes.</param>
        /// <param name="cmsVehicleGear">ContextMenuStrip for Vehicle Gear Nodes.</param>
        /// <param name="cmsVehicleWeaponMount">ContextMenuStrip for Vehicle Weapon Mounts.</param>
        /// <param name="cmsCyberware">ContextMenuStrip for Cyberware.</param>
        /// <param name="cmsCyberwareGear">ContextMenuStrip for Gear in Cyberware.</param>
        /// <param name="token">Cancellation token to listen to.</param>
        public static async Task<TreeNode> CreateTreeNode(this Chummer.Backend.Equipment.Vehicle objThis, ContextMenuStrip cmsVehicle, ContextMenuStrip cmsVehicleLocation, ContextMenuStrip cmsVehicleWeapon, ContextMenuStrip cmsWeaponAccessory, ContextMenuStrip cmsWeaponAccessoryGear, ContextMenuStrip cmsVehicleGear, ContextMenuStrip cmsVehicleWeaponMount, ContextMenuStrip cmsCyberware, ContextMenuStrip cmsCyberwareGear, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(objThis.ParentID) && !string.IsNullOrEmpty(objThis.Source) && !await (await objThis._objCharacter.GetSettingsAsync(token).ConfigureAwait(false)).BookEnabledAsync(objThis.Source, token).ConfigureAwait(false))
                return null;

            TreeNode objNode = new TreeNode
            {
                Name = objThis.InternalId,
                Text = await objThis.GetCurrentDisplayNameAsync(token).ConfigureAwait(false),
                Tag = objThis,
                ContextMenuStrip = cmsVehicle,
                ForeColor = await objThis.GetPreferredColorAsync(token).ConfigureAwait(false),
                ToolTipText = (await objThis.GetNotesAsync(token).ConfigureAwait(false)).WordWrap()
            };

            TreeNodeCollection lstChildNodes = objNode.Nodes;
            // Populate the list of Vehicle Locations.
            await objThis.Locations
                .ForEachAsync(
                    async (objLocation, t) =>
                        lstChildNodes.Add(await objLocation.CreateTreeNode(cmsVehicleLocation, t)
                            .ConfigureAwait(false)), token).ConfigureAwait(false);

            // VehicleMods.
            await VehicleModUiExtensions.AddModsToTreeNodeCollection(objThis.Mods, lstChildNodes, cmsVehicle, cmsCyberware, cmsCyberwareGear,
                cmsVehicleWeapon, cmsWeaponAccessory, cmsWeaponAccessoryGear, token).ConfigureAwait(false);
            if (await objThis.WeaponMounts.GetCountAsync(token).ConfigureAwait(false) > 0)
            {
                TreeNode nodMountsNode = new TreeNode
                {
                    Tag = "String_WeaponMounts",
                    Text = await LanguageManager.GetStringAsync("String_WeaponMounts", token: token).ConfigureAwait(false)
                };

                // Weapon Mounts
                await objThis.WeaponMounts.ForEachAsync(async (objWeaponMount, t) =>
                {
                    TreeNode objLoopNode = await objWeaponMount.CreateTreeNode(cmsVehicleWeaponMount, cmsVehicleWeapon,
                        cmsWeaponAccessory, cmsWeaponAccessoryGear, cmsCyberware, cmsCyberwareGear, cmsVehicle, t).ConfigureAwait(false);
                    if (objLoopNode != null)
                    {
                        nodMountsNode.Nodes.Add(objLoopNode);
                        nodMountsNode.Expand();
                    }
                }, token).ConfigureAwait(false);

                if (nodMountsNode.GetNodeCount(false) > 0)
                {
                    if (VehicleMod.ShouldNestWeaponMountsUnderWeaponsCategory)
                    {
                        TreeNode nodWeaponsCategory = null;
                        foreach (TreeNode objChild in lstChildNodes)
                        {
                            if (objChild.Tag is string strTag
                                && strTag == VehicleMod.GetCategoryGroupTag(VehicleMod.WeaponsCategoryKey))
                            {
                                nodWeaponsCategory = objChild;
                                break;
                            }
                        }

                        if (nodWeaponsCategory == null)
                        {
                            nodWeaponsCategory = new TreeNode
                            {
                                Tag = VehicleMod.GetCategoryGroupTag(VehicleMod.WeaponsCategoryKey),
                                Text = await VehicleMod.GetCategoryGroupDisplayNameAsync(
                                    VehicleMod.WeaponsCategoryKey, objThis._objCharacter, token).ConfigureAwait(false)
                            };
                            // Insert Weapons category in preferred order among existing category groups
                            int intChildNodeCount = objNode.GetNodeCount(false);
                            int intInsert = intChildNodeCount;
                            for (int i = 0; i < intChildNodeCount; ++i)
                            {
                                if (!VehicleMod.IsCategoryGroupTag(lstChildNodes[i].Tag))
                                {
                                    intInsert = i;
                                    break;
                                }

                                string strExistingKey = ((string)lstChildNodes[i].Tag)
                                    .Substring(VehicleMod.CategoryGroupTagPrefix.Length);
                                if (VehicleMod.GetCategoryGroupSortOrder(strExistingKey)
                                    > VehicleMod.GetCategoryGroupSortOrder(VehicleMod.WeaponsCategoryKey))
                                {
                                    intInsert = i;
                                    break;
                                }
                            }

                            lstChildNodes.Insert(intInsert, nodWeaponsCategory);
                        }

                        nodWeaponsCategory.Nodes.Add(nodMountsNode);
                        nodWeaponsCategory.Expand();
                    }
                    else
                    {
                        lstChildNodes.Add(nodMountsNode);
                    }
                }
            }
            // Vehicle Weapons (not attached to a mount).
            await objThis.Weapons.ForEachAsync(async (objWeapon, t) =>
            {
                TreeNode objLoopNode = await objWeapon.CreateTreeNode(cmsVehicleWeapon, cmsWeaponAccessory,
                    cmsWeaponAccessoryGear, t).ConfigureAwait(false);
                if (objLoopNode != null)
                {
                    TreeNode objParent = objNode;
                    if (objWeapon.Location != null)
                    {
                        foreach (TreeNode objFind in lstChildNodes)
                        {
                            if (objFind.Tag != objWeapon.Location)
                                continue;
                            objParent = objFind;
                            break;
                        }
                    }

                    objParent.Nodes.Add(objLoopNode);
                    objParent.Expand();
                }
            }, token).ConfigureAwait(false);

            // Vehicle Gear.
            await objThis.GearChildren.ForEachAsync(async (objGear, t) =>
            {
                TreeNode objLoopNode = await objGear.CreateTreeNode(cmsVehicleGear, null, t).ConfigureAwait(false);
                if (objLoopNode != null)
                {
                    TreeNode objParent = objNode;
                    if (objGear.Location != null)
                    {
                        foreach (TreeNode objFind in lstChildNodes)
                        {
                            if (objFind.Tag != objGear.Location)
                                continue;
                            objParent = objFind;
                            break;
                        }
                    }

                    objParent.Nodes.Add(objLoopNode);
                    objParent.Expand();
                }
            }, token).ConfigureAwait(false);

            if (objNode.GetNodeCount(false) > 0)
                objNode.Expand();

            return objNode;
        }
    }
}
