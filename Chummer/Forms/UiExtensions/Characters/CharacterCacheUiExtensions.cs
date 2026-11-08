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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;
using System.Xml.XPath;
using Newtonsoft.Json;
using static Chummer.EventHandlerExtensions;

namespace Chummer
{
    /// <summary>
    /// UI (WinForms) members of <see cref="Chummer.CharacterCache"/>, moved out of the engine for the native port.
    /// </summary>
    public static class CharacterCacheUiExtensions
    {
        public static async Task OnDefaultKeyDown(this Chummer.CharacterCache objThis, object sender, ValueTuple<KeyEventArgs, TreeNode> args, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (args.Item1.KeyCode == Keys.Delete)
            {
                switch (args.Item2.Parent.Tag.ToString())
                {
                    case "Recent":
                        await GlobalSettings.MostRecentlyUsedCharacters.RemoveAsync(await objThis.GetFilePathAsync(token).ConfigureAwait(false), token).ConfigureAwait(false);
                        break;

                    case "Favorite":
                        await GlobalSettings.FavoriteCharacters.RemoveAsync(await objThis.GetFilePathAsync(token).ConfigureAwait(false), token).ConfigureAwait(false);
                        break;
                }
            }
        }

        public static async Task OnDefaultContextMenuDeleteClick(this Chummer.CharacterCache objThis,object sender, EventArgs e, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (sender is TreeNode t)
            {
                switch (t.Parent.Tag?.ToString())
                {
                    case "Recent":
                        await GlobalSettings.MostRecentlyUsedCharacters.RemoveAsync(await objThis.GetFilePathAsync(token).ConfigureAwait(false), token)
                            .ConfigureAwait(false);
                        break;

                    case "Favorite":
                        await GlobalSettings.FavoriteCharacters.RemoveAsync(await objThis.GetFilePathAsync(token).ConfigureAwait(false), token)
                            .ConfigureAwait(false);
                        break;
                }
            }
        }
    }
}
