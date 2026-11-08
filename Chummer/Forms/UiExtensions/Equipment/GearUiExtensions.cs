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
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.XPath;
using Chummer.Annotations;
using NLog;

namespace Chummer.Backend.Equipment
{
    /// <summary>
    /// UI (WinForms) members of <see cref="Chummer.Backend.Equipment.Gear"/>, moved out of the engine for the native port.
    /// </summary>
    public static class GearUiExtensions
    {
        public static async Task ReaddImprovements(this Chummer.Backend.Equipment.Gear objThis, TreeView treGears, StringBuilder sbdOutdatedItems,
                                                 IReadOnlyCollection<string> lstInternalIdFilter,
                                                 Improvement.ImprovementSource eSource
                                                     = Improvement.ImprovementSource.Gear, bool blnStackEquipped = true,
                                                 CancellationToken token = default)
        {
            // We're only re-apply improvements a list of items, not all of them
            if (lstInternalIdFilter?.Contains(objThis.InternalId) != false)
            {
                XmlNode objNode = await objThis.GetNodeAsync(token: token).ConfigureAwait(false);
                if (objNode != null)
                {
                    if (objThis.Category == "Stacked Focus")
                    {
                        StackedFocus objStack = await objThis._objCharacter.StackedFoci
                                                                   .FindAsync(x => x.GearId == objThis.InternalId, token)
                                                                   .ConfigureAwait(false);
                        if (objStack != null)
                        {
                            await objStack.Gear.ForEachWithSideEffectsAsync((objFociGear, t) =>
                                objFociGear.ReaddImprovements(treGears, sbdOutdatedItems, lstInternalIdFilter,
                                    Improvement.ImprovementSource.StackedFocus,
                                    blnStackEquipped, t), token: token).ConfigureAwait(false);
                        }
                    }

                    objThis.Bonus = objNode["bonus"];
                    objThis.WirelessBonus = objNode["wirelessbonus"];
                    if (blnStackEquipped && objThis.Equipped)
                    {
                        if (objThis.Bonus != null)
                        {
                            ImprovementManager.SetForcedValue(objThis.Extra, objThis._objCharacter);
                            await ImprovementManager.CreateImprovementsAsync(
                                                        objThis._objCharacter, eSource, objThis.InternalId, objThis.Bonus, await objThis.GetRatingAsync(token).ConfigureAwait(false),
                                                        await objThis.GetCurrentDisplayNameShortAsync(token)
                                                            .ConfigureAwait(false), token: token)
                                                    .ConfigureAwait(false);
                            string strSelectedValue = ImprovementManager.GetSelectedValue(objThis._objCharacter);
                            if (!string.IsNullOrEmpty(strSelectedValue))
                            {
                                objThis.Extra = strSelectedValue;
                                string strText = await objThis.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false);
                                await treGears.DoThreadSafeAsync(x =>
                                {
                                    TreeNode objGearNode = x.FindNode(objThis.InternalId);
                                    if (objGearNode != null)
                                        objGearNode.Text = strText;
                                }, token: token).ConfigureAwait(false);
                            }
                        }

                        if (objThis.WirelessOn && objThis.WirelessBonus != null)
                        {
                            ImprovementManager.SetForcedValue(objThis.Extra, objThis._objCharacter);
                            if (await ImprovementManager.CreateImprovementsAsync(
                                    objThis._objCharacter, eSource, objThis.InternalId, objThis.WirelessBonus,
                                    await objThis.GetRatingAsync(token).ConfigureAwait(false),
                                    await objThis.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false),
                                    token: token).ConfigureAwait(false))
                            {
                                string strSelectedValue = ImprovementManager.GetSelectedValue(objThis._objCharacter);
                                if (!string.IsNullOrEmpty(strSelectedValue))
                                {
                                    objThis.Extra = strSelectedValue;
                                    string strText = await objThis.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false);
                                    await treGears.DoThreadSafeAsync(x =>
                                    {
                                        TreeNode objGearNode = x.FindNode(objThis.InternalId);
                                        if (objGearNode != null)
                                            objGearNode.Text = strText;
                                    }, token: token).ConfigureAwait(false);
                                }
                            }
                        }
                    }
                }
                else
                {
                    sbdOutdatedItems?.AppendLine(await objThis.GetCurrentDisplayNameShortAsync(token).ConfigureAwait(false));
                }
            }

            await objThis.Children.ForEachWithSideEffectsAsync((x, t) => x.ReaddImprovements(treGears, sbdOutdatedItems,
                lstInternalIdFilter, eSource, blnStackEquipped,
                t), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Build up the Tree for the current piece of Gear and all of its children.
        /// </summary>
        /// <param name="cmsGear">ContextMenuStrip for the Gear to use.</param>
        /// <param name="cmsCustomGear">ContextMenuStrip for the Gear to use if it can be renamed the way Custom Gear can (in Create mode).</param>
        /// <param name="token">Cancellation token to listen to.</param>
        public static async Task<TreeNode> CreateTreeNode(this Chummer.Backend.Equipment.Gear objThis, ContextMenuStrip cmsGear, ContextMenuStrip cmsCustomGear, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(objThis.ParentID) && !string.IsNullOrEmpty(objThis.Source) &&
                !await (await objThis._objCharacter.GetSettingsAsync(token).ConfigureAwait(false)).BookEnabledAsync(objThis.Source, token).ConfigureAwait(false))
                return null;

            TreeNode objNode = new TreeNode
            {
                Name = objThis.InternalId,
                Text = await objThis.GetCurrentDisplayNameAsync(token).ConfigureAwait(false),
                Tag = objThis,
                ContextMenuStrip = cmsCustomGear != null && objThis.AllowRename ? cmsCustomGear : cmsGear,
                ForeColor = await objThis.GetPreferredColorAsync(token).ConfigureAwait(false),
                ToolTipText = (await objThis.GetNotesAsync(token).ConfigureAwait(false)).WordWrap()
            };

            await objThis.BuildChildrenGearTree(objNode, cmsGear, cmsCustomGear, token).ConfigureAwait(false);

            return objNode;
        }

        /// <summary>
        /// Build up the Tree for the current piece of Gear's children.
        /// </summary>
        /// <param name="objParentNode">Parent node to which to append children gear.</param>
        /// <param name="cmsGear">ContextMenuStrip for the Gear's children to use to use.</param>
        /// <param name="cmsCustomGear">ContextMenuStrip for the Gear's children to use if they can be renamed the way Custom Gear can (in Create mode).</param>
        /// <param name="token">Cancellation token to listen to.</param>
        public static async Task BuildChildrenGearTree(this Chummer.Backend.Equipment.Gear objThis, TreeNode objParentNode, ContextMenuStrip cmsGear, ContextMenuStrip cmsCustomGear, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (objParentNode == null)
                return;
            bool blnExpandNode = false;
            await objThis.Children.ForEachAsync(async (objChild, t) =>
            {
                TreeNode objChildNode = await objChild.CreateTreeNode(cmsGear, cmsCustomGear, t).ConfigureAwait(false);
                if (objChildNode != null)
                {
                    objParentNode.Nodes.Add(objChildNode);
                    if (objChild.ParentID != objThis.InternalId ||
                        (await objThis.GetNodeXPathAsync(token: t).ConfigureAwait(false))?.SelectSingleNodeAndCacheExpression("gears/@startcollapsed", t)?.Value !=
                        bool.TrueString)
                        blnExpandNode = true;
                }
            }, token).ConfigureAwait(false);

            if (blnExpandNode)
                objParentNode.Expand();
        }

        public static void SetupChildrenGearsCollectionChanged(this Chummer.Backend.Equipment.Gear objThis, bool blnAdd, TreeView treGear, ContextMenuStrip cmsGear = null, ContextMenuStrip cmsCustomGear = null, AsyncNotifyCollectionChangedEventHandler funcMakeDirty = null)
        {
            if (blnAdd)
            {
                Task FuncDelegateBeforeClearToAdd(object _, NotifyCollectionChangedEventArgs y,
                    CancellationToken innerToken = default) =>
                    objThis.RefreshChildrenGearsClearBindings(treGear, y, innerToken);

                Task FuncDelegateToAdd(object _, NotifyCollectionChangedEventArgs y, CancellationToken innerToken = default) =>
                    objThis.RefreshChildrenGears(treGear, cmsGear, cmsCustomGear, y, funcMakeDirty, token: innerToken);

                objThis.Children.AddTaggedBeforeClearCollectionChanged(treGear, FuncDelegateBeforeClearToAdd);
                objThis.Children.AddTaggedCollectionChanged(treGear, FuncDelegateToAdd);
                if (funcMakeDirty != null)
                    objThis.Children.AddTaggedCollectionChanged(treGear, funcMakeDirty);
                foreach (Gear objChild in objThis.Children)
                {
                    objChild.SetupChildrenGearsCollectionChanged(true, treGear, cmsGear, cmsCustomGear, funcMakeDirty);
                }
            }
            else
            {
                objThis.Children.RemoveTaggedAsyncBeforeClearCollectionChanged(treGear);
                objThis.Children.RemoveTaggedAsyncCollectionChanged(treGear);
                foreach (Gear objChild in objThis.Children)
                {
                    objChild.SetupChildrenGearsCollectionChanged(false, treGear);
                }
            }
        }

        public static async Task SetupChildrenGearsCollectionChangedAsync(this Chummer.Backend.Equipment.Gear objThis, bool blnAdd, TreeView treGear, ContextMenuStrip cmsGear = null, ContextMenuStrip cmsCustomGear = null, AsyncNotifyCollectionChangedEventHandler funcMakeDirty = null, CancellationToken token = default)
        {
            if (blnAdd)
            {
                Task FuncDelegateBeforeClearToAdd(object _, NotifyCollectionChangedEventArgs y,
                    CancellationToken innerToken = default) =>
                    objThis.RefreshChildrenGearsClearBindings(treGear, y, innerToken);

                Task FuncDelegateToAdd(object _, NotifyCollectionChangedEventArgs y, CancellationToken innerToken = default) =>
                    objThis.RefreshChildrenGears(treGear, cmsGear, cmsCustomGear, y, funcMakeDirty, token: innerToken);

                objThis.Children.AddTaggedBeforeClearCollectionChanged(treGear, FuncDelegateBeforeClearToAdd);
                objThis.Children.AddTaggedCollectionChanged(treGear, FuncDelegateToAdd);
                if (funcMakeDirty != null)
                    objThis.Children.AddTaggedCollectionChanged(treGear, funcMakeDirty);
                await objThis.Children.ForEachWithSideEffectsAsync(
                    (objChild, t) => objChild.SetupChildrenGearsCollectionChangedAsync(true, treGear, cmsGear, cmsCustomGear,
                        funcMakeDirty, t), token).ConfigureAwait(false);
            }
            else
            {
                await objThis.Children.RemoveTaggedAsyncBeforeClearCollectionChangedAsync(treGear, token).ConfigureAwait(false);
                await objThis.Children.RemoveTaggedAsyncCollectionChangedAsync(treGear, token).ConfigureAwait(false);
                await objThis.Children.ForEachWithSideEffectsAsync(
                    (objChild, t) => objChild.SetupChildrenGearsCollectionChangedAsync(false, treGear, token: t), token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Refreshes a single focus' rating (for changing ratings in create mode)
        /// </summary>
        /// <param name="treFoci">TreeView of foci.</param>
        /// <param name="intNewRating">New rating that the focus is supposed to have.</param>
        /// <param name="token">Cancellation token to listen to.</param>
        /// <returns>True if the new rating complies by focus limits or the gear is not bonded, false otherwise</returns>
        public static async Task<bool> RefreshSingleFocusRating(this Chummer.Backend.Equipment.Gear objThis, TreeView treFoci, int intNewRating, CancellationToken token = default)
        {
            if (objThis.Bonded)
            {
                int intMaxFocusTotal = await (await objThis._objCharacter.GetAttributeAsync("MAG", token: token).ConfigureAwait(false)).GetTotalValueAsync(token).ConfigureAwait(false) * 5;
                if (await (await objThis._objCharacter.GetSettingsAsync(token).ConfigureAwait(false)).GetMysAdeptSecondMAGAttributeAsync(token).ConfigureAwait(false) && await objThis._objCharacter.GetIsMysticAdeptAsync(token).ConfigureAwait(false))
                    intMaxFocusTotal = Math.Min(intMaxFocusTotal, await (await objThis._objCharacter.GetAttributeAsync("MAGAdept", token: token).ConfigureAwait(false)).GetTotalValueAsync(token).ConfigureAwait(false) * 5);

                int intFociTotal = await (await objThis._objCharacter.GetFociAsync(token).ConfigureAwait(false)).SumAsync(x => !object.ReferenceEquals(x.GearObject, objThis), (x, t) => x.GetRatingAsync(t), token).ConfigureAwait(false);

                if (intFociTotal + intNewRating > intMaxFocusTotal && !await objThis._objCharacter.GetIgnoreRulesAsync(token).ConfigureAwait(false))
                {
                    await MessageBoxes.ShowAsync(await LanguageManager.GetStringAsync("Message_FocusMaximumForce", token: token).ConfigureAwait(false),
                        await LanguageManager.GetStringAsync("MessageTitle_FocusMaximum", token: token).ConfigureAwait(false), MessageButtons.OK,
                        MessageIcon.Information, token: token).ConfigureAwait(false);
                    return false;
                }
            }

            await objThis.SetRatingAsync(intNewRating, token).ConfigureAwait(false);

            switch (objThis.Category)
            {
                case "Foci":
                case "Metamagic Foci":
                    {
                        TreeNode nodFocus = await treFoci.DoThreadSafeFuncAsync(x => x.FindNodeByTag(objThis), token: token).ConfigureAwait(false);
                        if (nodFocus != null)
                        {
                            string strText = (await objThis.GetCurrentDisplayNameAsync(token).ConfigureAwait(false)).Replace(
                                await LanguageManager.GetStringAsync(objThis.RatingLabel, token: token).ConfigureAwait(false),
                                await LanguageManager.GetStringAsync("String_Force", token: token).ConfigureAwait(false));
                            await treFoci.DoThreadSafeFuncAsync(() => nodFocus.Text = strText, token: token)
                                         .ConfigureAwait(false);
                        }
                    }
                    break;

                case "Stacked Focus":
                    {
                        ThreadSafeList<StackedFocus> lstStackedFoci
                            = await objThis._objCharacter.GetStackedFociAsync(token).ConfigureAwait(false);
                        for (int i = await lstStackedFoci.GetCountAsync(token).ConfigureAwait(false) - 1; i >= 0; --i)
                        {
                            if (i >= await lstStackedFoci.GetCountAsync(token).ConfigureAwait(false))
                                continue;
                            StackedFocus objStack = await lstStackedFoci.GetValueAtAsync(i, token)
                                                                        .ConfigureAwait(false);
                            if (objStack.GearId != objThis.InternalId)
                                continue;
                            TreeNode nodFocus = await treFoci
                                                      .DoThreadSafeFuncAsync(
                                                          x => x.FindNode(objStack.InternalId), token: token)
                                                      .ConfigureAwait(false);
                            if (nodFocus != null)
                            {
                                string strText = (await objThis.GetCurrentDisplayNameAsync(token).ConfigureAwait(false)).Replace(
                                    await LanguageManager.GetStringAsync(objThis.RatingLabel, token: token).ConfigureAwait(false),
                                    await LanguageManager.GetStringAsync("String_Force", token: token)
                                                         .ConfigureAwait(false));
                                await treFoci.DoThreadSafeFuncAsync(() => nodFocus.Text = strText, token: token)
                                             .ConfigureAwait(false);
                            }

                            break;
                        }
                    }
                    break;
            }

            return true;
        }
    }
}
