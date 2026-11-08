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
using Chummer.Backend.Attributes;
using Microsoft.VisualStudio.Threading;
using NLog;
using IAsyncDisposable = System.IAsyncDisposable;

namespace Chummer.Backend.Equipment
{
    /// <summary>
    /// UI (WinForms) members of <see cref="Chummer.Backend.Equipment.Cyberware"/>, moved out of the engine for the native port.
    /// </summary>
    public static class CyberwareUiExtensions
    {
        /// <summary>
        /// Build up the Tree for the current piece of Cyberware and all of its children.
        /// </summary>
        public static async Task<TreeNode> CreateTreeNode(this Chummer.Backend.Equipment.Cyberware objThis, ContextMenuStrip cmsCyberware, ContextMenuStrip cmsGear, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            IAsyncDisposable objLocker = await objThis.LockObject.EnterReadLockAsync(token).ConfigureAwait(false);
            try
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
                    ContextMenuStrip = cmsCyberware,
                    ForeColor = await objThis.GetPreferredColorAsync(token).ConfigureAwait(false),
                    ToolTipText = (await objThis.GetNotesAsync(token).ConfigureAwait(false)).WordWrap()
                };

                TreeNodeCollection lstChildNodes = objNode.Nodes;
                await (await objThis.GetChildrenAsync(token).ConfigureAwait(false)).ForEachAsync(async (objChild, t) =>
                {
                    TreeNode objLoopNode = await objChild.CreateTreeNode(cmsCyberware, cmsGear, t).ConfigureAwait(false);
                    if (objLoopNode != null)
                        lstChildNodes.Add(objLoopNode);
                }, token).ConfigureAwait(false);

                await (await objThis.GetGearChildrenAsync(token).ConfigureAwait(false)).ForEachAsync(async (objGear, t) =>
                {
                    TreeNode objLoopNode = await objGear.CreateTreeNode(cmsGear, null, t).ConfigureAwait(false);
                    if (objLoopNode != null)
                        lstChildNodes.Add(objLoopNode);
                }, token).ConfigureAwait(false);

                await (await objThis.GetDrugChildrenAsync(token).ConfigureAwait(false)).ForEachAsync(async (objDrug, t) =>
                {
                    TreeNode objLoopNode = await objDrug.CreateTreeNode(t).ConfigureAwait(false);
                    if (objLoopNode != null)
                    {
                        objLoopNode.ContextMenuStrip = cmsCyberware;
                        lstChildNodes.Add(objLoopNode);
                    }
                }, token).ConfigureAwait(false);

                if (objNode.GetNodeCount(false) > 0)
                    objNode.Expand();

                return objNode;
            }
            finally
            {
                await objLocker.DisposeAsync().ConfigureAwait(false);
            }
        }

        public static void SetupChildrenCyberwareCollectionChanged(this Chummer.Backend.Equipment.Cyberware objThis, bool blnAdd, TreeView treCyberware,
            ContextMenuStrip cmsCyberware = null, ContextMenuStrip cmsCyberwareGear = null, AsyncNotifyCollectionChangedEventHandler funcMakeDirty = null)
        {
            if (blnAdd)
            {
                Task FuncCyberwareBeforeClearToAdd(object _, NotifyCollectionChangedEventArgs y,
                    CancellationToken innerToken = default) =>
                    objThis.RefreshChildrenCyberwareClearBindings(treCyberware, y, innerToken);

                Task FuncCyberwareToAdd(object _, NotifyCollectionChangedEventArgs y, CancellationToken innerToken = default) =>
                    objThis.RefreshChildrenCyberware(treCyberware, cmsCyberware, cmsCyberwareGear, y,
                        funcMakeDirty, token: innerToken);

                Task FuncGearBeforeClearToAdd(object _, NotifyCollectionChangedEventArgs y,
                    CancellationToken innerToken = default) =>
                    objThis.RefreshChildrenGearsClearBindings(treCyberware, y, innerToken);

                Task FuncGearToAdd(object _, NotifyCollectionChangedEventArgs y, CancellationToken innerToken = default) =>
                    objThis.RefreshChildrenGears(treCyberware, cmsCyberwareGear, null, t => objThis.Children.GetCountAsync(t), y,
                        funcMakeDirty, token: innerToken);

                async Task<int> FuncDrugOffset(CancellationToken innerToken = default)
                {
                    return await objThis.Children.GetCountAsync(innerToken).ConfigureAwait(false)
                           + await objThis.GearChildren.GetCountAsync(innerToken).ConfigureAwait(false);
                }

                Task FuncDrugToAdd(object _, NotifyCollectionChangedEventArgs y, CancellationToken innerToken = default) =>
                    objThis.RefreshChildrenDrugs(treCyberware, cmsCyberware, t => FuncDrugOffset(t), y, innerToken);

                objThis.Children.AddTaggedBeforeClearCollectionChanged(treCyberware, FuncCyberwareBeforeClearToAdd);
                objThis.Children.AddTaggedCollectionChanged(treCyberware, FuncCyberwareToAdd);
                objThis.GearChildren.AddTaggedBeforeClearCollectionChanged(treCyberware, FuncGearBeforeClearToAdd);
                objThis.GearChildren.AddTaggedCollectionChanged(treCyberware, FuncGearToAdd);
                objThis.DrugChildren.AddTaggedCollectionChanged(treCyberware, FuncDrugToAdd);
                if (funcMakeDirty != null)
                {
                    objThis.Children.AddTaggedCollectionChanged(treCyberware, funcMakeDirty);
                    objThis.GearChildren.AddTaggedCollectionChanged(treCyberware, funcMakeDirty);
                    objThis.DrugChildren.AddTaggedCollectionChanged(treCyberware, funcMakeDirty);
                }

                foreach (Cyberware objChild in objThis.Children)
                {
                    objChild.SetupChildrenCyberwareCollectionChanged(true, treCyberware, cmsCyberware,
                        cmsCyberwareGear, funcMakeDirty);
                }

                foreach (Gear objGear in objThis.GearChildren)
                    objGear.SetupChildrenGearsCollectionChanged(true, treCyberware, cmsCyberwareGear, null,
                        funcMakeDirty);
            }
            else
            {
                objThis.Children.RemoveTaggedAsyncBeforeClearCollectionChanged(treCyberware);
                objThis.Children.RemoveTaggedAsyncCollectionChanged(treCyberware);
                objThis.GearChildren.RemoveTaggedAsyncBeforeClearCollectionChanged(treCyberware);
                objThis.GearChildren.RemoveTaggedAsyncCollectionChanged(treCyberware);
                objThis.DrugChildren.RemoveTaggedAsyncCollectionChanged(treCyberware);
                foreach (Cyberware objChild in objThis.Children)
                    objChild.SetupChildrenCyberwareCollectionChanged(false, treCyberware);
                foreach (Gear objGear in objThis.GearChildren)
                    objGear.SetupChildrenGearsCollectionChanged(false, treCyberware);
            }
        }

        public static async Task SetupChildrenCyberwareCollectionChangedAsync(this Chummer.Backend.Equipment.Cyberware objThis, bool blnAdd, TreeView treCyberware,
            ContextMenuStrip cmsCyberware = null, ContextMenuStrip cmsCyberwareGear = null,
            AsyncNotifyCollectionChangedEventHandler funcMakeDirty = null, CancellationToken token = default)
        {
            TaggedObservableCollection<Cyberware> lstChildren = await objThis.GetChildrenAsync(token).ConfigureAwait(false);
            TaggedObservableCollection<Gear> lstGearChildren = await objThis.GetGearChildrenAsync(token).ConfigureAwait(false);
            TaggedObservableCollection<Drug> lstDrugChildren = await objThis.GetDrugChildrenAsync(token).ConfigureAwait(false);
            if (blnAdd)
            {
                Task FuncCyberwareBeforeClearToAdd(object _, NotifyCollectionChangedEventArgs y,
                    CancellationToken innerToken = default) =>
                    objThis.RefreshChildrenCyberwareClearBindings(treCyberware, y, innerToken);

                Task FuncCyberwareToAdd(object _, NotifyCollectionChangedEventArgs y, CancellationToken innerToken = default) =>
                    objThis.RefreshChildrenCyberware(treCyberware, cmsCyberware, cmsCyberwareGear, y,
                        funcMakeDirty, token: innerToken);

                Task FuncGearBeforeClearToAdd(object _, NotifyCollectionChangedEventArgs y,
                    CancellationToken innerToken = default) =>
                    objThis.RefreshChildrenGearsClearBindings(treCyberware, y, innerToken);

                Task FuncGearToAdd(object _, NotifyCollectionChangedEventArgs y, CancellationToken innerToken = default) =>
                    objThis.RefreshChildrenGears(treCyberware, cmsCyberwareGear, null, t => objThis.Children.GetCountAsync(t), y,
                        funcMakeDirty, token: innerToken);

                async Task<int> FuncDrugOffset(CancellationToken innerToken = default)
                {
                    return await lstChildren.GetCountAsync(innerToken).ConfigureAwait(false)
                           + await lstGearChildren.GetCountAsync(innerToken).ConfigureAwait(false);
                }

                Task FuncDrugToAdd(object _, NotifyCollectionChangedEventArgs y, CancellationToken innerToken = default) =>
                    objThis.RefreshChildrenDrugs(treCyberware, cmsCyberware, t => FuncDrugOffset(t), y, innerToken);

                lstChildren.AddTaggedBeforeClearCollectionChanged(treCyberware, FuncCyberwareBeforeClearToAdd);
                lstChildren.AddTaggedCollectionChanged(treCyberware, FuncCyberwareToAdd);
                lstGearChildren.AddTaggedBeforeClearCollectionChanged(treCyberware, FuncGearBeforeClearToAdd);
                lstGearChildren.AddTaggedCollectionChanged(treCyberware, FuncGearToAdd);
                lstDrugChildren.AddTaggedCollectionChanged(treCyberware, FuncDrugToAdd);
                if (funcMakeDirty != null)
                {
                    lstChildren.AddTaggedCollectionChanged(treCyberware, funcMakeDirty);
                    lstGearChildren.AddTaggedCollectionChanged(treCyberware, funcMakeDirty);
                    lstDrugChildren.AddTaggedCollectionChanged(treCyberware, funcMakeDirty);
                }

                await lstChildren.ForEachWithSideEffectsAsync(
                    (objChild, t) => objChild.SetupChildrenCyberwareCollectionChangedAsync(true, treCyberware, cmsCyberware,
                        cmsCyberwareGear, funcMakeDirty, t), token).ConfigureAwait(false);
                await lstGearChildren.ForEachWithSideEffectsAsync(
                    (objChild, t) => objChild.SetupChildrenGearsCollectionChangedAsync(true, treCyberware, cmsCyberwareGear,
                        null, funcMakeDirty, token: t), token).ConfigureAwait(false);
            }
            else
            {
                await lstChildren.RemoveTaggedAsyncBeforeClearCollectionChangedAsync(treCyberware, token).ConfigureAwait(false);
                await lstChildren.RemoveTaggedAsyncCollectionChangedAsync(treCyberware, token).ConfigureAwait(false);
                await lstGearChildren.RemoveTaggedAsyncBeforeClearCollectionChangedAsync(treCyberware, token).ConfigureAwait(false);
                await lstGearChildren.RemoveTaggedAsyncCollectionChangedAsync(treCyberware, token).ConfigureAwait(false);
                await lstDrugChildren.RemoveTaggedAsyncCollectionChangedAsync(treCyberware, token).ConfigureAwait(false);
                await lstChildren.ForEachWithSideEffectsAsync(
                    (objChild, t) => objChild.SetupChildrenCyberwareCollectionChangedAsync(false, treCyberware,
                        token: t), token).ConfigureAwait(false);
                await lstGearChildren.ForEachWithSideEffectsAsync(
                    (objChild, t) => objChild.SetupChildrenGearsCollectionChangedAsync(false, treCyberware, token: t),
                    token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Minimal tree refresh for DrugChildren. RefreshChildrenGears only accepts Gear.
        /// </summary>
        internal static async Task RefreshChildrenDrugs(this Chummer.Backend.Equipment.Cyberware objThis, TreeView treCyberware, ContextMenuStrip cmsCyberware,
            NotifyCollectionChangedEventArgs e, CancellationToken token = default)
        {
            if (e == null || treCyberware == null)
                return;

            TreeNode nodParent = await treCyberware.DoThreadSafeFuncAsync(x => x.FindNodeByTag(objThis), token: token).ConfigureAwait(false);
            if (nodParent == null)
                return;

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    {
                        int intNewIndex = e.NewStartingIndex;
                        foreach (Drug objDrug in e.NewItems)
                        {
                            await AddDrugToTree(objDrug, intNewIndex, innerToken: token).ConfigureAwait(false);
                            ++intNewIndex;
                        }

                        break;
                    }

                case NotifyCollectionChangedAction.Remove:
                    {
                        foreach (Drug objDrug in e.OldItems)
                        {
                            await treCyberware.DoThreadSafeAsync(() => nodParent.FindNodeByTag(objDrug)?.Remove(), token: token)
                                .ConfigureAwait(false);
                        }

                        break;
                    }

                case NotifyCollectionChangedAction.Replace:
                    {
                        string strSelectedId
                            = (await treCyberware.DoThreadSafeFuncAsync(x => x.SelectedNode?.Tag, token: token).ConfigureAwait(false) as IHasInternalId)?.InternalId
                              ?? string.Empty;
                        foreach (Drug objDrug in e.OldItems)
                        {
                            await treCyberware.DoThreadSafeAsync(() => nodParent.FindNodeByTag(objDrug)?.Remove(), token: token)
                                .ConfigureAwait(false);
                        }

                        int intNewIndex = e.NewStartingIndex;
                        foreach (Drug objDrug in e.NewItems)
                        {
                            await AddDrugToTree(objDrug, intNewIndex, innerToken: token).ConfigureAwait(false);
                            ++intNewIndex;
                        }

                        await treCyberware.DoThreadSafeAsync(x => x.SelectedNode = x.FindNode(strSelectedId), token: token)
                            .ConfigureAwait(false);
                        break;
                    }

                case NotifyCollectionChangedAction.Move:
                    {
                        string strSelectedId
                            = (await treCyberware.DoThreadSafeFuncAsync(x => x.SelectedNode?.Tag, token: token).ConfigureAwait(false) as IHasInternalId)?.InternalId
                              ?? string.Empty;
                        await treCyberware.DoThreadSafeAsync(() =>
                        {
                            foreach (Drug objDrug in e.OldItems)
                                nodParent.FindNodeByTag(objDrug)?.Remove();
                        }, token: token).ConfigureAwait(false);
                        int intNewIndex = e.NewStartingIndex;
                        foreach (Drug objDrug in e.NewItems)
                        {
                            await AddDrugToTree(objDrug, intNewIndex, innerToken: token).ConfigureAwait(false);
                            ++intNewIndex;
                        }

                        await treCyberware.DoThreadSafeAsync(x => x.SelectedNode = x.FindNode(strSelectedId), token: token)
                            .ConfigureAwait(false);
                        break;
                    }

                case NotifyCollectionChangedAction.Reset:
                    {
                        await treCyberware.DoThreadSafeAsync(() =>
                        {
                            for (int i = nodParent.GetNodeCount(false) - 1; i >= 0; --i)
                            {
                                TreeNode objNode = nodParent.Nodes[i];
                                if (objNode.Tag is Drug)
                                    objNode.Remove();
                            }
                        }, token: token).ConfigureAwait(false);
                        break;
                    }
            }

            async ValueTask AddDrugToTree(Drug objDrug, int intIndex = -1, bool blnSingleAdd = true, CancellationToken innerToken = default)
            {
                TreeNode objNode = await objDrug.CreateTreeNode(innerToken).ConfigureAwait(false);
                if (objNode == null)
                    return;
                objNode.ContextMenuStrip = cmsCyberware;
                await treCyberware.DoThreadSafeAsync(() =>
                {
                    nodParent.Nodes.AddOrInsert(objNode, intIndex);
                    nodParent.Expand();
                }, token: innerToken).ConfigureAwait(false);
                if (blnSingleAdd)
                    await treCyberware.DoThreadSafeAsync(x => x.SelectedNode = objNode, token: innerToken)
                        .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Minimal tree refresh for DrugChildren. RefreshChildrenGears only accepts Gear.
        /// </summary>
        internal static async Task RefreshChildrenDrugs(this Chummer.Backend.Equipment.Cyberware objThis, TreeView treCyberware, ContextMenuStrip cmsCyberware,
            Func<Task<int>> funcOffset, NotifyCollectionChangedEventArgs e, CancellationToken token = default)
        {
            if (e == null || treCyberware == null)
                return;

            TreeNode nodParent = await treCyberware.DoThreadSafeFuncAsync(x => x.FindNodeByTag(objThis), token: token).ConfigureAwait(false);
            if (nodParent == null)
                return;

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    {
                        int intNewIndex = e.NewStartingIndex;
                        if (funcOffset != null)
                            intNewIndex += await funcOffset.Invoke().ConfigureAwait(false);
                        foreach (Drug objDrug in e.NewItems)
                        {
                            await AddDrugToTree(objDrug, intNewIndex, innerToken: token).ConfigureAwait(false);
                            ++intNewIndex;
                        }

                        break;
                    }

                case NotifyCollectionChangedAction.Remove:
                    {
                        foreach (Drug objDrug in e.OldItems)
                        {
                            await treCyberware.DoThreadSafeAsync(() => nodParent.FindNodeByTag(objDrug)?.Remove(), token: token)
                                .ConfigureAwait(false);
                        }

                        break;
                    }

                case NotifyCollectionChangedAction.Replace:
                    {
                        string strSelectedId
                            = (await treCyberware.DoThreadSafeFuncAsync(x => x.SelectedNode?.Tag, token: token).ConfigureAwait(false) as IHasInternalId)?.InternalId
                              ?? string.Empty;
                        foreach (Drug objDrug in e.OldItems)
                        {
                            await treCyberware.DoThreadSafeAsync(() => nodParent.FindNodeByTag(objDrug)?.Remove(), token: token)
                                .ConfigureAwait(false);
                        }

                        int intNewIndex = e.NewStartingIndex;
                        if (funcOffset != null)
                            intNewIndex += await funcOffset.Invoke().ConfigureAwait(false);
                        foreach (Drug objDrug in e.NewItems)
                        {
                            await AddDrugToTree(objDrug, intNewIndex, innerToken: token).ConfigureAwait(false);
                            ++intNewIndex;
                        }

                        await treCyberware.DoThreadSafeAsync(x => x.SelectedNode = x.FindNode(strSelectedId), token: token)
                            .ConfigureAwait(false);
                        break;
                    }

                case NotifyCollectionChangedAction.Move:
                    {
                        string strSelectedId
                            = (await treCyberware.DoThreadSafeFuncAsync(x => x.SelectedNode?.Tag, token: token).ConfigureAwait(false) as IHasInternalId)?.InternalId
                              ?? string.Empty;
                        await treCyberware.DoThreadSafeAsync(() =>
                        {
                            foreach (Drug objDrug in e.OldItems)
                                nodParent.FindNodeByTag(objDrug)?.Remove();
                        }, token: token).ConfigureAwait(false);
                        int intNewIndex = e.NewStartingIndex;
                        if (funcOffset != null)
                            intNewIndex += await funcOffset.Invoke().ConfigureAwait(false);
                        foreach (Drug objDrug in e.NewItems)
                        {
                            await AddDrugToTree(objDrug, intNewIndex, innerToken: token).ConfigureAwait(false);
                            ++intNewIndex;
                        }

                        await treCyberware.DoThreadSafeAsync(x => x.SelectedNode = x.FindNode(strSelectedId), token: token)
                            .ConfigureAwait(false);
                        break;
                    }

                case NotifyCollectionChangedAction.Reset:
                    {
                        await treCyberware.DoThreadSafeAsync(() =>
                        {
                            for (int i = nodParent.GetNodeCount(false) - 1; i >= 0; --i)
                            {
                                TreeNode objNode = nodParent.Nodes[i];
                                if (objNode.Tag is Drug)
                                    objNode.Remove();
                            }
                        }, token: token).ConfigureAwait(false);
                        break;
                    }
            }

            async ValueTask AddDrugToTree(Drug objDrug, int intIndex = -1, bool blnSingleAdd = true, CancellationToken innerToken = default)
            {
                TreeNode objNode = await objDrug.CreateTreeNode(innerToken).ConfigureAwait(false);
                if (objNode == null)
                    return;
                objNode.ContextMenuStrip = cmsCyberware;
                await treCyberware.DoThreadSafeAsync(() =>
                {
                    nodParent.Nodes.AddOrInsert(objNode, intIndex);
                    nodParent.Expand();
                }, token: innerToken).ConfigureAwait(false);
                if (blnSingleAdd)
                    await treCyberware.DoThreadSafeAsync(x => x.SelectedNode = objNode, token: innerToken)
                        .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Minimal tree refresh for DrugChildren. RefreshChildrenGears only accepts Gear.
        /// </summary>
        internal static async Task RefreshChildrenDrugs(this Chummer.Backend.Equipment.Cyberware objThis, TreeView treCyberware, ContextMenuStrip cmsCyberware,
            Func<CancellationToken, Task<int>> funcOffset, NotifyCollectionChangedEventArgs e, CancellationToken token = default)
        {
            if (e == null || treCyberware == null)
                return;

            TreeNode nodParent = await treCyberware.DoThreadSafeFuncAsync(x => x.FindNodeByTag(objThis), token: token).ConfigureAwait(false);
            if (nodParent == null)
                return;

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    {
                        int intNewIndex = e.NewStartingIndex;
                        if (funcOffset != null)
                            intNewIndex += await funcOffset.Invoke(token).ConfigureAwait(false);
                        foreach (Drug objDrug in e.NewItems)
                        {
                            await AddDrugToTree(objDrug, intNewIndex, innerToken: token).ConfigureAwait(false);
                            ++intNewIndex;
                        }

                        break;
                    }

                case NotifyCollectionChangedAction.Remove:
                    {
                        foreach (Drug objDrug in e.OldItems)
                        {
                            await treCyberware.DoThreadSafeAsync(() => nodParent.FindNodeByTag(objDrug)?.Remove(), token: token)
                                .ConfigureAwait(false);
                        }

                        break;
                    }

                case NotifyCollectionChangedAction.Replace:
                    {
                        string strSelectedId
                            = (await treCyberware.DoThreadSafeFuncAsync(x => x.SelectedNode?.Tag, token: token).ConfigureAwait(false) as IHasInternalId)?.InternalId
                              ?? string.Empty;
                        foreach (Drug objDrug in e.OldItems)
                        {
                            await treCyberware.DoThreadSafeAsync(() => nodParent.FindNodeByTag(objDrug)?.Remove(), token: token)
                                .ConfigureAwait(false);
                        }

                        int intNewIndex = e.NewStartingIndex;
                        if (funcOffset != null)
                            intNewIndex += await funcOffset.Invoke(token).ConfigureAwait(false);
                        foreach (Drug objDrug in e.NewItems)
                        {
                            await AddDrugToTree(objDrug, intNewIndex, innerToken: token).ConfigureAwait(false);
                            ++intNewIndex;
                        }

                        await treCyberware.DoThreadSafeAsync(x => x.SelectedNode = x.FindNode(strSelectedId), token: token)
                            .ConfigureAwait(false);
                        break;
                    }

                case NotifyCollectionChangedAction.Move:
                    {
                        string strSelectedId
                            = (await treCyberware.DoThreadSafeFuncAsync(x => x.SelectedNode?.Tag, token: token).ConfigureAwait(false) as IHasInternalId)?.InternalId
                              ?? string.Empty;
                        await treCyberware.DoThreadSafeAsync(() =>
                        {
                            foreach (Drug objDrug in e.OldItems)
                                nodParent.FindNodeByTag(objDrug)?.Remove();
                        }, token: token).ConfigureAwait(false);
                        int intNewIndex = e.NewStartingIndex;
                        if (funcOffset != null)
                            intNewIndex += await funcOffset.Invoke(token).ConfigureAwait(false);
                        foreach (Drug objDrug in e.NewItems)
                        {
                            await AddDrugToTree(objDrug, intNewIndex, innerToken: token).ConfigureAwait(false);
                            ++intNewIndex;
                        }

                        await treCyberware.DoThreadSafeAsync(x => x.SelectedNode = x.FindNode(strSelectedId), token: token)
                            .ConfigureAwait(false);
                        break;
                    }

                case NotifyCollectionChangedAction.Reset:
                    {
                        await treCyberware.DoThreadSafeAsync(() =>
                        {
                            for (int i = nodParent.GetNodeCount(false) - 1; i >= 0; --i)
                            {
                                TreeNode objNode = nodParent.Nodes[i];
                                if (objNode.Tag is Drug)
                                    objNode.Remove();
                            }
                        }, token: token).ConfigureAwait(false);
                        break;
                    }
            }

            async ValueTask AddDrugToTree(Drug objDrug, int intIndex = -1, bool blnSingleAdd = true, CancellationToken innerToken = default)
            {
                TreeNode objNode = await objDrug.CreateTreeNode(innerToken).ConfigureAwait(false);
                if (objNode == null)
                    return;
                objNode.ContextMenuStrip = cmsCyberware;
                await treCyberware.DoThreadSafeAsync(() =>
                {
                    nodParent.Nodes.AddOrInsert(objNode, intIndex);
                    nodParent.Expand();
                }, token: innerToken).ConfigureAwait(false);
                if (blnSingleAdd)
                    await treCyberware.DoThreadSafeAsync(x => x.SelectedNode = objNode, token: innerToken)
                        .ConfigureAwait(false);
            }
        }
    }
}
