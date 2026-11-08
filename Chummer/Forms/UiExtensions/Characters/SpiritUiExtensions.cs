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
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.XPath;
using Chummer.Annotations;
using Chummer.Backend.Enums;
using Chummer.Backend.Skills;

namespace Chummer
{
    /// <summary>
    /// UI (WinForms) members of <see cref="Chummer.Spirit"/>, moved out of the engine for the native port.
    /// </summary>
    public static class SpiritUiExtensions
    {
        public static bool StartTrackingTreeNodeForUpdates(this Chummer.Spirit objThis, TreeNode objNode)
        {
            return objThis._setNodesToTrackForUpdates.TryAdd(objNode);
        }

        public static bool StopTrackingTreeNodeForUpdates(this Chummer.Spirit objThis, TreeNode objNode)
        {
            return objThis._setNodesToTrackForUpdates.Remove(objNode);
        }

        /// <summary>
        /// Build up the Tree for the current spirit.
        /// </summary>
        /// <param name="cmsSpirit">ContextMenuStrip for the Spirit to use.</param>
        /// <param name="token">Cancellation token to listen to.</param>
        public static async Task<TreeNode> CreateTreeNode(this Chummer.Spirit objThis, ContextMenuStrip cmsSpirit, CancellationToken token = default)
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
                ContextMenuStrip = cmsSpirit,
                ForeColor = await objThis.GetPreferredColorAsync(token).ConfigureAwait(false),
                ToolTipText = (await objThis.GetNotesAsync(token).ConfigureAwait(false)).WordWrap()
            };

            string strSpace = await LanguageManager.GetStringAsync("String_Space", token: token).ConfigureAwait(false);
            string strTypeData = await objThis.GetNameAsync(token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(strTypeData))
                strTypeData = await LanguageManager.GetStringAsync(await objThis.GetRatingLabelAsync(token).ConfigureAwait(false), token: token).ConfigureAwait(false)
                    + strSpace + (await objThis.GetForceAsync(token).ConfigureAwait(false)).ToString(GlobalSettings.CultureInfo);
            else
                strTypeData += await LanguageManager.GetStringAsync("String_Comma", token: token).ConfigureAwait(false) + strSpace + await LanguageManager.GetStringAsync(await objThis.GetRatingLabelAsync(token).ConfigureAwait(false), token: token).ConfigureAwait(false)
                    + strSpace + (await objThis.GetForceAsync(token).ConfigureAwait(false)).ToString(GlobalSettings.CultureInfo);
            objNode.Nodes.Add("TypeData", strTypeData);
            objNode.Nodes.Add("Services", await LanguageManager.GetStringAsync("Label_Spirit_ServicesOwed", token: token)
                + strSpace + (await objThis.GetServicesOwedAsync(token).ConfigureAwait(false)).ToString(GlobalSettings.CultureInfo));
            objNode.Expand();
            return objNode;
        }

        /// <summary>
        /// Alias map for SourceDetail control text and tooltip assignation.
        /// </summary>
        /// <param name="sourceControl"></param>
        public static void SetSourceDetail(this Chummer.Spirit objThis, Control sourceControl)
        {
            objThis.SourceDetail.SetControl(sourceControl);
        }

        public static async Task SetSourceDetailAsync(this Chummer.Spirit objThis, Control sourceControl, CancellationToken token = default)
        {
            await (await objThis.GetSourceDetailAsync(token).ConfigureAwait(false)).SetControlAsync(sourceControl, token).ConfigureAwait(false);
        }
    }
}
