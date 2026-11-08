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
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.XPath;
using Chummer.Annotations;
using Chummer.Backend.Enums;
using NLog;

namespace Chummer
{
    /// <summary>
    /// UI (WinForms) members of <see cref="Chummer.Contact"/>, moved out of the engine for the native port.
    /// </summary>
    public static class ContactUiExtensions
    {
        public static bool StartTrackingTreeNodeForUpdates(this Chummer.Contact objThis, TreeNode objNode)
        {
            return WinFormsTrackedNodes.Get(objThis).TryAdd(objNode);
        }

        public static bool StopTrackingTreeNodeForUpdates(this Chummer.Contact objThis, TreeNode objNode)
        {
            return WinFormsTrackedNodes.Get(objThis).Remove(objNode);
        }

        /// <summary>
        /// Build up the Tree for the current contact, enemy, or pet.
        /// </summary>
        /// <param name="cmsContact">ContextMenuStrip for the contact, enemy, or pet to use.</param>
        /// <param name="token">Cancellation token to listen to.</param>
        public static async Task<TreeNode> CreateTreeNode(this Chummer.Contact objThis, ContextMenuStrip cmsContact, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();

            string strNodeText = await objThis.GetCurrentDisplayNameAsync(token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(strNodeText))
                strNodeText = await LanguageManager.GetStringAsync("String_Unknown", token: token).ConfigureAwait(false);
            TreeNode objNode = new TreeNode
            {
                Name = objThis.InternalId,
                Text = strNodeText,
                Tag = objThis,
                ContextMenuStrip = cmsContact,
                ForeColor = await objThis.GetPreferredColorAsync(token).ConfigureAwait(false),
                ToolTipText = (await objThis.GetNotesAsync(token).ConfigureAwait(false)).WordWrap()
            };

            if (await objThis.GetEntityTypeAsync(token).ConfigureAwait(false) == ContactType.Pet)
            {
                string strMetatype = await objThis.GetDisplayMetatypeAsync(token).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(strMetatype))
                    objNode.Nodes.Add("TypeData", strMetatype);
            }
            else
            {
                string strType = await objThis.GetDisplayTypeAsync(token).ConfigureAwait(false);
                if (string.IsNullOrEmpty(strType))
                    objNode.Nodes.Add("TypeData", await objThis.GetQuickTextAsync(token).ConfigureAwait(false));
                else
                    objNode.Nodes.Add("TypeData", strType
                        + await LanguageManager.GetStringAsync("String_Space", token: token).ConfigureAwait(false)
                        + await objThis.GetQuickTextAsync(token).ConfigureAwait(false));
                string strLocation = await objThis.GetLocationAsync(token).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(strLocation))
                    objNode.Nodes.Add("Location", strLocation);
            }
            objNode.Expand();
            return objNode;
        }

        public static void RefreshTrackedTreeNodes_Text(this Chummer.Contact objThis,CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (WinFormsTrackedNodes.Get(objThis).IsEmpty)
                return;
            string strText = objThis.CurrentDisplayName;
            if (string.IsNullOrEmpty(strText))
                strText = LanguageManager.GetString("String_Unknown", token: token);
            Utils.RunOnMainThread(t =>
            {
                foreach (TreeNode objNode in WinFormsTrackedNodes.Get(objThis))
                {
                    t.ThrowIfCancellationRequested();
                    if (objNode.Tag != objThis) // Sanity check
                        continue;
                    objNode.Text = strText;
                }
            }, token: token);
        }

        public static void RefreshTrackedTreeNodes_Color(this Chummer.Contact objThis,CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (WinFormsTrackedNodes.Get(objThis).IsEmpty)
                return;
            Color objColor = objThis.PreferredColor;
            Utils.RunOnMainThread(t =>
            {
                foreach (TreeNode objNode in WinFormsTrackedNodes.Get(objThis))
                {
                    t.ThrowIfCancellationRequested();
                    if (objNode.Tag != objThis) // Sanity check
                        continue;
                    objNode.ForeColor = objColor;
                }
            }, token: token);
        }

        public static void RefreshTrackedTreeNodes_ToolTipText(this Chummer.Contact objThis,CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (WinFormsTrackedNodes.Get(objThis).IsEmpty)
                return;
            string strToolTipText = objThis.Notes.WordWrap();
            Utils.RunOnMainThread(t =>
            {
                foreach (TreeNode objNode in WinFormsTrackedNodes.Get(objThis))
                {
                    t.ThrowIfCancellationRequested();
                    if (objNode.Tag != objThis) // Sanity check
                        continue;
                    objNode.ToolTipText = strToolTipText;
                }
            }, token: token);
        }

        public static void RefreshTrackedTreeNodes_TypeData(this Chummer.Contact objThis,CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (WinFormsTrackedNodes.Get(objThis).IsEmpty)
                return;
            string strText;
            if (objThis.EntityType == ContactType.Pet)
                strText = objThis.DisplayMetatype;
            else
            {
                strText = objThis.DisplayType;
                if (string.IsNullOrEmpty(strText))
                    strText = objThis.QuickText;
                else
                    strText += LanguageManager.GetString("String_Space", token: token) + objThis.QuickText;
            }

            Utils.RunOnMainThread(t =>
            {
                foreach (TreeNode objNode in WinFormsTrackedNodes.Get(objThis))
                {
                    t.ThrowIfCancellationRequested();
                    if (objNode.Tag != objThis) // Sanity check
                        continue;
                    if (string.IsNullOrEmpty(strText))
                        objNode.Nodes.RemoveByKey("TypeData");
                    else if (objNode.Nodes.ContainsKey("TypeData"))
                    {
                        foreach (TreeNode objInnerNode in objNode.Nodes.Find("TypeData", true))
                            objInnerNode.Text = strText;
                    }
                    else
                        objNode.Nodes.Add("TypeData", strText);
                }
            }, token: token);
        }

        public static void RefreshTrackedTreeNodes_Location(this Chummer.Contact objThis,CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (WinFormsTrackedNodes.Get(objThis).IsEmpty)
                return;
            if (objThis.EntityType == ContactType.Pet)
            {
                Utils.RunOnMainThread(t =>
                {
                    foreach (TreeNode objNode in WinFormsTrackedNodes.Get(objThis))
                    {
                        t.ThrowIfCancellationRequested();
                        if (objNode.Tag != objThis) // Sanity check
                            continue;
                        objNode.Nodes.RemoveByKey("Location");
                    }
                }, token: token);
                return;
            }
            string strText = objThis.Location;
            Utils.RunOnMainThread(t =>
            {
                foreach (TreeNode objNode in WinFormsTrackedNodes.Get(objThis))
                {
                    t.ThrowIfCancellationRequested();
                    if (objNode.Tag != objThis) // Sanity check
                        continue;
                    if (string.IsNullOrEmpty(strText))
                        objNode.Nodes.RemoveByKey("Location");
                    else if (objNode.Nodes.ContainsKey("Location"))
                    {
                        foreach (TreeNode objInnerNode in objNode.Nodes.Find("Location", true))
                            objInnerNode.Text = strText;
                    }
                    else
                        objNode.Nodes.Add("Location", strText);
                }
            }, token: token);
        }

        public static async Task RefreshTrackedTreeNodes_TextAsync(this Chummer.Contact objThis,CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (WinFormsTrackedNodes.Get(objThis).IsEmpty)
                return;
            string strText = await objThis.GetCurrentDisplayNameAsync(token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(strText))
                strText = await LanguageManager.GetStringAsync("String_Unknown", token: token).ConfigureAwait(false);
            await Utils.RunOnMainThreadAsync(t =>
            {
                foreach (TreeNode objNode in WinFormsTrackedNodes.Get(objThis))
                {
                    t.ThrowIfCancellationRequested();
                    if (objNode.Tag != objThis) // Sanity check
                        continue;
                    objNode.Text = strText;
                }
            }, token: token).ConfigureAwait(false);
        }

        public static async Task RefreshTrackedTreeNodes_ColorAsync(this Chummer.Contact objThis,CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (WinFormsTrackedNodes.Get(objThis).IsEmpty)
                return;
            Color objColor = await objThis.GetPreferredColorAsync(token).ConfigureAwait(false);
            await Utils.RunOnMainThreadAsync(t =>
            {
                foreach (TreeNode objNode in WinFormsTrackedNodes.Get(objThis))
                {
                    t.ThrowIfCancellationRequested();
                    if (objNode.Tag != objThis) // Sanity check
                        continue;
                    objNode.ForeColor = objColor;
                }
            }, token: token).ConfigureAwait(false);
        }

        public static async Task RefreshTrackedTreeNodes_ToolTipTextAsync(this Chummer.Contact objThis,CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (WinFormsTrackedNodes.Get(objThis).IsEmpty)
                return;
            string strToolTipText = (await objThis.GetNotesAsync(token).ConfigureAwait(false)).WordWrap();
            await Utils.RunOnMainThreadAsync(t =>
            {
                foreach (TreeNode objNode in WinFormsTrackedNodes.Get(objThis))
                {
                    t.ThrowIfCancellationRequested();
                    if (objNode.Tag != objThis) // Sanity check
                        continue;
                    objNode.ToolTipText = strToolTipText;
                }
            }, token: token).ConfigureAwait(false);
        }

        public static async Task RefreshTrackedTreeNodes_TypeDataAsync(this Chummer.Contact objThis,CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (WinFormsTrackedNodes.Get(objThis).IsEmpty)
                return;
            string strText;
            if (await objThis.GetEntityTypeAsync(token).ConfigureAwait(false) == ContactType.Pet)
                strText = await objThis.GetDisplayMetatypeAsync(token).ConfigureAwait(false);
            else
            {
                strText = await objThis.GetDisplayTypeAsync(token).ConfigureAwait(false);
                if (string.IsNullOrEmpty(strText))
                    strText = await objThis.GetQuickTextAsync(token).ConfigureAwait(false);
                else
                    strText += await LanguageManager.GetStringAsync("String_Space", token: token).ConfigureAwait(false)
                        + await objThis.GetQuickTextAsync(token).ConfigureAwait(false);
            }

            await Utils.RunOnMainThreadAsync(t =>
            {
                foreach (TreeNode objNode in WinFormsTrackedNodes.Get(objThis))
                {
                    t.ThrowIfCancellationRequested();
                    if (objNode.Tag != objThis) // Sanity check
                        continue;
                    if (string.IsNullOrEmpty(strText))
                        objNode.Nodes.RemoveByKey("TypeData");
                    else if (objNode.Nodes.ContainsKey("TypeData"))
                    {
                        foreach (TreeNode objInnerNode in objNode.Nodes.Find("TypeData", true))
                            objInnerNode.Text = strText;
                    }
                    else
                        objNode.Nodes.Add("TypeData", strText);
                }
            }, token: token).ConfigureAwait(false);
        }

        public static async Task RefreshTrackedTreeNodes_LocationAsync(this Chummer.Contact objThis,CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (WinFormsTrackedNodes.Get(objThis).IsEmpty)
                return;
            if (await objThis.GetEntityTypeAsync(token).ConfigureAwait(false) == ContactType.Pet)
            {
                await Utils.RunOnMainThreadAsync(t =>
                {
                    foreach (TreeNode objNode in WinFormsTrackedNodes.Get(objThis))
                    {
                        t.ThrowIfCancellationRequested();
                        if (objNode.Tag != objThis) // Sanity check
                            continue;
                        objNode.Nodes.RemoveByKey("Location");
                    }
                }, token: token).ConfigureAwait(false);
                return;
            }
            string strText = await objThis.GetLocationAsync(token).ConfigureAwait(false);
            await Utils.RunOnMainThreadAsync(t =>
            {
                foreach (TreeNode objNode in WinFormsTrackedNodes.Get(objThis))
                {
                    t.ThrowIfCancellationRequested();
                    if (objNode.Tag != objThis) // Sanity check
                        continue;
                    if (string.IsNullOrEmpty(strText))
                        objNode.Nodes.RemoveByKey("Location");
                    else if (objNode.Nodes.ContainsKey("Location"))
                    {
                        foreach (TreeNode objInnerNode in objNode.Nodes.Find("Location", true))
                            objInnerNode.Text = strText;
                    }
                    else
                        objNode.Nodes.Add("Location", strText);
                }
            }, token: token).ConfigureAwait(false);
        }
    }
}
