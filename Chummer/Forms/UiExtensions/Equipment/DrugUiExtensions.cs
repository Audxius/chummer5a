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
using System.Collections.Specialized;
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
    /// UI (WinForms) members of <see cref="Chummer.Backend.Equipment.Drug"/>, moved out of the engine for the native port.
    /// </summary>
    public static class DrugUiExtensions
    {
        /// <summary>
        /// Add a piece of Armor to the Armor TreeView.
        /// </summary>
        public static async Task<TreeNode> CreateTreeNode(this Chummer.Backend.Equipment.Drug objThis, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            //if (!string.IsNullOrEmpty(ParentID) && !string.IsNullOrEmpty(Source) && !_objCharacter.Settings.BookEnabled(Source))
            //return null;

            TreeNode objNode = new TreeNode
            {
                Name = objThis.InternalId,
                Text = await objThis.GetCurrentDisplayNameAsync(token).ConfigureAwait(false),
                Tag = objThis,
                ForeColor = await objThis.GetPreferredColorAsync(token).ConfigureAwait(false),
                ToolTipText = (await objThis.GetNotesAsync(token).ConfigureAwait(false)).WordWrap()
            };

            if (objNode.GetNodeCount(false) > 0)
                objNode.Expand();

            return objNode;
        }
    }
}
