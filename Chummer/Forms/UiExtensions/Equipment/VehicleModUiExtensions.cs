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
using NLog;

namespace Chummer.Backend.Equipment
{
    /// <summary>
    /// UI (WinForms) members of <see cref="Chummer.Backend.Equipment.VehicleMod"/>, moved out of the engine for the native port.
    /// </summary>
    public static class VehicleModUiExtensions
    {
        /// <summary>
        /// Creates a synthetic tree node that groups vehicle mods of this mod's category.
        /// </summary>
        /// <param name="token">Cancellation token to listen to.</param>
        /// <returns>A tree node whose Tag is the category group key.</returns>
        public static async Task<TreeNode> CreateCategoryGroupTreeNode(this Chummer.Backend.Equipment.VehicleMod objThis, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return new TreeNode
            {
                Tag = VehicleMod.GetCategoryGroupTag(objThis.Category),
                Text = await VehicleMod.GetCategoryGroupDisplayNameAsync(objThis.Category, objThis._objCharacter, token).ConfigureAwait(false)
            };
        }

        /// <summary>
        /// Adds vehicle mod tree nodes to a parent collection, optionally grouping by category.
        /// </summary>
        /// <param name="lstMods">Mods to add.</param>
        /// <param name="lstChildNodes">Parent node collection to populate.</param>
        /// <param name="cmsVehicleMod">ContextMenuStrip for Vehicle Mods.</param>
        /// <param name="cmsCyberware">ContextMenuStrip for Cyberware.</param>
        /// <param name="cmsCyberwareGear">ContextMenuStrip for Gear in Cyberware.</param>
        /// <param name="cmsVehicleWeapon">ContextMenuStrip for Vehicle Weapons.</param>
        /// <param name="cmsVehicleWeaponAccessory">ContextMenuStrip for Vehicle Weapon Accessories.</param>
        /// <param name="cmsVehicleWeaponAccessoryGear">ContextMenuStrip for Gear in Vehicle Weapon Accessories.</param>
        /// <param name="token">Cancellation token to listen to.</param>
        public static async Task AddModsToTreeNodeCollection(
            TaggedObservableCollection<VehicleMod> lstMods,
            TreeNodeCollection lstChildNodes,
            ContextMenuStrip cmsVehicleMod,
            ContextMenuStrip cmsCyberware,
            ContextMenuStrip cmsCyberwareGear,
            ContextMenuStrip cmsVehicleWeapon,
            ContextMenuStrip cmsVehicleWeaponAccessory,
            ContextMenuStrip cmsVehicleWeaponAccessoryGear,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (lstMods == null || lstChildNodes == null)
                return;

            if (!GlobalSettings.GroupVehicleModsByCategory)
            {
                await lstMods.ForEachAsync(async (objMod, t) =>
                {
                    TreeNode objLoopNode = await objMod.CreateTreeNode(cmsVehicleMod, cmsCyberware, cmsCyberwareGear,
                        cmsVehicleWeapon, cmsVehicleWeaponAccessory, cmsVehicleWeaponAccessoryGear, t).ConfigureAwait(false);
                    if (objLoopNode != null)
                        lstChildNodes.Add(objLoopNode);
                }, token).ConfigureAwait(false);
                return;
            }

            Dictionary<string, TreeNode> dicCategories = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
            await lstMods.ForEachAsync(async (objMod, t) =>
            {
                TreeNode objLoopNode = await objMod.CreateTreeNode(cmsVehicleMod, cmsCyberware, cmsCyberwareGear,
                    cmsVehicleWeapon, cmsVehicleWeaponAccessory, cmsVehicleWeaponAccessoryGear, t).ConfigureAwait(false);
                if (objLoopNode == null)
                    return;

                string strCategoryKey = VehicleMod.GetCategoryGroupKey(objMod.Category);
                if (!dicCategories.TryGetValue(strCategoryKey, out TreeNode nodCategory))
                {
                    nodCategory = await objMod.CreateCategoryGroupTreeNode(t).ConfigureAwait(false);
                    dicCategories.Add(strCategoryKey, nodCategory);
                }

                nodCategory.Nodes.Add(objLoopNode);
                nodCategory.Expand();
            }, token).ConfigureAwait(false);

            foreach (string strCategoryKey in dicCategories.Keys
                         .OrderBy(VehicleMod.GetCategoryGroupSortOrder)
                         .ThenBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                lstChildNodes.Add(dicCategories[strCategoryKey]);
            }
        }

        /// <summary>
        /// Add a piece of Armor to the Armor TreeView.
        /// </summary>
        public static async Task<TreeNode> CreateTreeNode(this Chummer.Backend.Equipment.VehicleMod objThis, ContextMenuStrip cmsVehicleMod, ContextMenuStrip cmsCyberware, ContextMenuStrip cmsCyberwareGear, ContextMenuStrip cmsVehicleWeapon, ContextMenuStrip cmsVehicleWeaponAccessory, ContextMenuStrip cmsVehicleWeaponAccessoryGear, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (objThis.IncludedInVehicle && !string.IsNullOrEmpty(objThis.Source) && !await (await objThis._objCharacter.GetSettingsAsync(token).ConfigureAwait(false)).BookEnabledAsync(objThis.Source, token).ConfigureAwait(false))
                return null;

            TreeNode objNode = new TreeNode
            {
                Name = objThis.InternalId,
                Text = await objThis.GetCurrentDisplayNameAsync(token).ConfigureAwait(false),
                Tag = objThis,
                ContextMenuStrip = cmsVehicleMod,
                ForeColor = await objThis.GetPreferredColorAsync(token).ConfigureAwait(false),
                ToolTipText = (await objThis.GetNotesAsync(token).ConfigureAwait(false)).WordWrap()
            };

            TreeNodeCollection lstChildNodes = objNode.Nodes;
            // Cyberware.
            await objThis.Cyberware.ForEachAsync(async (objCyberware, t) =>
            {
                TreeNode objLoopNode = await objCyberware.CreateTreeNode(cmsCyberware, cmsCyberwareGear, t).ConfigureAwait(false);
                if (objLoopNode != null)
                    lstChildNodes.Add(objLoopNode);
            }, token).ConfigureAwait(false);

            // VehicleWeapons.
            await objThis.Weapons.ForEachAsync(async (objWeapon, t) =>
            {
                TreeNode objLoopNode = await objWeapon.CreateTreeNode(cmsVehicleWeapon, cmsVehicleWeaponAccessory,
                    cmsVehicleWeaponAccessoryGear, t).ConfigureAwait(false);
                if (objLoopNode != null)
                    lstChildNodes.Add(objLoopNode);
            }, token).ConfigureAwait(false);

            if (objNode.GetNodeCount(false) > 0)
                objNode.Expand();

            return objNode;
        }
    }
}
