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
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
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

namespace Chummer.Backend.Equipment
{
    /// <summary>
    /// UI (WinForms) members of <see cref="Chummer.Backend.Equipment.Lifestyle"/>, moved out of the engine for the native port.
    /// </summary>
    public static class LifestyleUiExtensions
    {
        public static async Task<TreeNode> CreateTreeNode(this Chummer.Backend.Equipment.Lifestyle objThis, ContextMenuStrip cmsBasicLifestyle, ContextMenuStrip cmsAdvancedLifestyle, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            IAsyncDisposable objLocker = await objThis.LockObject.EnterReadLockAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                //if (!string.IsNullOrEmpty(ParentID) && !string.IsNullOrEmpty(Source) && !_objCharacter.Settings.BookEnabled(Source))
                //return null;
                TreeNode objNode = new TreeNode
                {
                    Name = objThis.InternalId,
                    Text = await objThis.GetCurrentDisplayNameAsync(token).ConfigureAwait(false),
                    Tag = objThis,
                    ContextMenuStrip = await objThis.GetStyleTypeAsync(token).ConfigureAwait(false) == LifestyleType.Standard
                        ? cmsBasicLifestyle
                        : cmsAdvancedLifestyle,
                    ForeColor = await objThis.GetPreferredColorAsync(token).ConfigureAwait(false),
                    ToolTipText = (await objThis.GetNotesAsync(token).ConfigureAwait(false)).WordWrap()
                };
                return objNode;
            }
            finally
            {
                await objLocker.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
