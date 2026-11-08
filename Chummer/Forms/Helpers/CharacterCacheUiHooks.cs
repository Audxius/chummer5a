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
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using static Chummer.EventHandlerExtensions;

namespace Chummer
{
    /// <summary>
    /// Character roster event hooks for a <see cref="CharacterCache"/> (moved out of the engine type). Plugins can
    /// replace or extend the handlers; the defaults open the character, handle keys and delete from the roster.
    /// </summary>
    public sealed class CharacterCacheUiHooks
    {
        private static readonly ConditionalWeakTable<CharacterCache, CharacterCacheUiHooks> s_dicHooks
            = new ConditionalWeakTable<CharacterCache, CharacterCacheUiHooks>();

        public SafeAsyncEventHandler OnMyDoubleClick { get; set; }

        public SafeAsyncEventHandler OnMyContextMenuDeleteClick { get; set; }

        public SafeAsyncEventHandler<TreeViewEventArgs> OnMyAfterSelect { get; set; }

        public SafeAsyncEventHandler<ValueTuple<KeyEventArgs, TreeNode>> OnMyKeyDown { get; set; }

        public static CharacterCacheUiHooks Get(CharacterCache objCache)
        {
            return s_dicHooks.GetValue(objCache, x =>
            {
                CharacterCacheUiHooks objHooks = new CharacterCacheUiHooks();
                objHooks.OnMyDoubleClick += x.OnDefaultDoubleClick;
                objHooks.OnMyKeyDown += x.OnDefaultKeyDown;
                objHooks.OnMyContextMenuDeleteClick += x.OnDefaultContextMenuDeleteClick;
                return objHooks;
            });
        }
    }
}
