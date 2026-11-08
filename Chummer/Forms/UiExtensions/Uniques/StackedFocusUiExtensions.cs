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
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using Chummer.Backend.Equipment;

namespace Chummer
{
    /// <summary>
    /// UI (WinForms) members of <see cref="Chummer.StackedFocus"/>, moved out of the engine for the native port.
    /// </summary>
    public static class StackedFocusUiExtensions
    {
        public static async Task<TreeNode> CreateTreeNode(this Chummer.StackedFocus objThis, Gear objGear, ContextMenuStrip cmsStackedFocus, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (objGear == null)
                throw new ArgumentNullException(nameof(objGear));
            IAsyncDisposable objLocker = await objThis.LockObject.EnterReadLockAsync(token).ConfigureAwait(false);
            try
            {
                TreeNode objNode = await objGear.CreateTreeNode(cmsStackedFocus, null, token).ConfigureAwait(false);

                objNode.Name = objThis.InternalId;
                objNode.Text = await LanguageManager.GetStringAsync("String_StackedFocus", token: token).ConfigureAwait(false)
                               + await LanguageManager.GetStringAsync("String_Colon", token: token).ConfigureAwait(false)
                               + await LanguageManager.GetStringAsync("String_Space", token: token).ConfigureAwait(false)
                               + await objThis.GetCurrentDisplayNameAsync(token).ConfigureAwait(false);
                objNode.Tag = objThis;
                objNode.Checked = objThis.Bonded;

                return objNode;
            }
            finally
            {
                await objLocker.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
