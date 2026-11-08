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
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;

namespace Chummer
{
    /// <summary>
    /// UI (WinForms) members of <see cref="Chummer.CompareTreeNodes"/>, moved out of the engine for the native port.
    /// </summary>
    public static class CompareTreeNodesUiExtensions
    {
        /// <summary>
        /// Sort TreeNodes in alphabetical order, ignoring [].
        /// </summary>
        public static int CompareText(TreeNode tx, TreeNode ty)
        {
            if (tx == null)
            {
                if (ty == null)
                    return 0;
                return -1;
            }
            return ty == null ? 1 : string.Compare(tx.Text.FastEscape('[', ']'), ty.Text.FastEscape('[', ']'), GlobalSettings.CultureInfo, CompareOptions.StringSort);
        }
    }
}
