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
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.XPath;
using NLog;

namespace Chummer
{
    /// <summary>
    /// UI (WinForms) members of <see cref="Chummer.MartialArtTechnique"/>, moved out of the engine for the native port.
    /// </summary>
    public static class MartialArtTechniqueUiExtensions
    {
        public static async Task<TreeNode> CreateTreeNode(this Chummer.MartialArtTechnique objThis, ContextMenuStrip cmsMartialArtTechnique, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            //if (!string.IsNullOrEmpty(ParentID) && !string.IsNullOrEmpty(Source) && !_objCharacter.Settings.BookEnabled(Source))
            //return null;

            TreeNode objNode = new TreeNode
            {
                Name = objThis.InternalId,
                Text = await objThis.GetCurrentDisplayNameAsync(token).ConfigureAwait(false),
                Tag = objThis,
                ContextMenuStrip = cmsMartialArtTechnique,
                ForeColor = await objThis.GetPreferredColorAsync(token).ConfigureAwait(false),
                ToolTipText = (await objThis.GetNotesAsync(token).ConfigureAwait(false)).WordWrap()
            };

            return objNode;
        }
    }
}
