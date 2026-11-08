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
using Chummer.Backend.Equipment;
using NLog;

namespace Chummer
{
    /// <summary>
    /// UI (WinForms) members of <see cref="Chummer.Quality"/>, moved out of the engine for the native port.
    /// </summary>
    public static class QualityUiExtensions
    {
        public static async Task<TreeNode> CreateTreeNode(this Chummer.Quality objThis, ContextMenuStrip cmsQuality, TreeView treQualities, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            IAsyncDisposable objLocker = await objThis.LockObject.EnterReadLockAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                QualitySource eOriginSource = await objThis.GetOriginSourceAsync(token).ConfigureAwait(false);
                if ((eOriginSource == QualitySource.BuiltIn ||
                     eOriginSource == QualitySource.Improvement ||
                     eOriginSource == QualitySource.QualityLevelImprovement ||
                     eOriginSource == QualitySource.LifeModule ||
                     eOriginSource == QualitySource.Metatype ||
                     eOriginSource == QualitySource.MetatypeRemovable ||
                     eOriginSource == QualitySource.MetatypeRemovedAtChargen ||
                     eOriginSource == QualitySource.Heritage)
                     && !string.IsNullOrEmpty(objThis.Source)
                     && !await (await objThis._objCharacter.GetSettingsAsync(token).ConfigureAwait(false)).BookEnabledAsync(objThis.Source, token).ConfigureAwait(false))
                    return null;

                TreeNode objNode = new TreeNode
                {
                    Name = objThis.InternalId,
                    Text = await objThis.GetCurrentDisplayNameAsync(token).ConfigureAwait(false),
                    Tag = objThis,
                    ContextMenuStrip = cmsQuality,
                    ForeColor = await objThis.GetPreferredColorAsync(token).ConfigureAwait(false),
                    ToolTipText = (await objThis.GetNotesAsync(token).ConfigureAwait(false)).WordWrap()
                };
                if (await objThis.GetSuppressedAsync(token).ConfigureAwait(false))
                {
                    //Treenodes store their font as null when inheriting from the treeview; have to pull it from the treeview directly to set the fontstyle.
                    objNode.NodeFont = new Font(await treQualities.DoThreadSafeFuncAsync(x => x.Font, token).ConfigureAwait(false), FontStyle.Strikeout);
                }

                return objNode;
            }
            finally
            {
                await objLocker.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
