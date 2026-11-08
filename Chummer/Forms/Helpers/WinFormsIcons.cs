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
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace Chummer
{
    /// <summary>
    /// Windows icon helpers for the WinForms UI (moved out of the engine's Utils).
    /// </summary>
    public static class WinFormsIcons
    {
        private static readonly ConcurrentDictionary<Icon, Bitmap> s_dicCachedIconBitmaps = new ConcurrentDictionary<Icon, Bitmap>();

        /// <summary>
        /// Dictionary assigning icons to singly-initialized instances of their bitmaps.
        /// Mainly intended for SystemIcons.
        /// </summary>
        public static Bitmap GetCachedIconBitmap(Icon objIcon)
        {
            return s_dicCachedIconBitmaps.GetOrAdd(objIcon, x => x.ToBitmap());
        }

        /// <summary>
        /// Dictionary assigning icons to singly-initialized instances of their bitmaps.
        /// Mainly intended for SystemIcons.
        /// </summary>
        public static Task<Bitmap> GetCachedIconBitmapAsync(Icon objIcon, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return s_dicCachedIconBitmaps.GetOrAddAsync(objIcon, (x, t) => TaskExtensions.RunWithoutEC(x.ToBitmap, t), token);
        }

        private static readonly ConcurrentDictionary<Icon, Bitmap> s_dicStockIconBitmapsForSystemIcons = new ConcurrentDictionary<Icon, Bitmap>();

        /// <summary>
        /// Dictionary assigning Windows stock icons' bitmaps to SystemIcons equivalents.
        /// Needed where the graphics used in dialog windows in newer versions of windows are different from those in SystemIcons.
        /// </summary>
        public static Bitmap GetStockIconBitmapsForSystemIcon(Icon objIcon)
        {
            return s_dicStockIconBitmapsForSystemIcons.GetOrAdd(objIcon, x =>
            {
                if (x == SystemIcons.Application)
                {
                    return NativeMethods.GetStockIcon(NativeMethods.SHSTOCKICONID.SIID_APPLICATION).ToBitmap();
                }

                if (x == SystemIcons.Asterisk || x == SystemIcons.Information)
                {
                    return NativeMethods.GetStockIcon(NativeMethods.SHSTOCKICONID.SIID_INFO).ToBitmap();
                }

                if (x == SystemIcons.Error || x == SystemIcons.Hand)
                {
                    return NativeMethods.GetStockIcon(NativeMethods.SHSTOCKICONID.SIID_ERROR).ToBitmap();
                }

                if (x == SystemIcons.Exclamation || x == SystemIcons.Warning)
                {
                    return NativeMethods.GetStockIcon(NativeMethods.SHSTOCKICONID.SIID_WARNING).ToBitmap();
                }

                if (x == SystemIcons.Question)
                {
                    return NativeMethods.GetStockIcon(NativeMethods.SHSTOCKICONID.SIID_HELP).ToBitmap();
                }

                if (x == SystemIcons.Shield)
                {
                    return NativeMethods.GetStockIcon(NativeMethods.SHSTOCKICONID.SIID_SHIELD).ToBitmap();
                }

                if (x == SystemIcons.WinLogo)
                {
                    return SystemIcons.WinLogo.ToBitmap();
                }

                throw new ArgumentOutOfRangeException(nameof(objIcon));
            });
        }

        /// <summary>
        /// Dictionary assigning Windows stock icons' bitmaps to SystemIcons equivalents.
        /// Needed where the graphics used in dialog windows in newer versions of windows are different from those in SystemIcons.
        /// </summary>
        public static Task<Bitmap> GetStockIconBitmapsForSystemIconAsync(Icon objIcon, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return s_dicStockIconBitmapsForSystemIcons.GetOrAddAsync(objIcon, (x, t) => TaskExtensions.RunWithoutEC(() =>
            {
                if (x == SystemIcons.Application)
                {
                    return NativeMethods.GetStockIcon(NativeMethods.SHSTOCKICONID.SIID_APPLICATION).ToBitmap();
                }

                if (x == SystemIcons.Asterisk || x == SystemIcons.Information)
                {
                    return NativeMethods.GetStockIcon(NativeMethods.SHSTOCKICONID.SIID_INFO).ToBitmap();
                }

                if (x == SystemIcons.Error || x == SystemIcons.Hand)
                {
                    return NativeMethods.GetStockIcon(NativeMethods.SHSTOCKICONID.SIID_ERROR).ToBitmap();
                }

                if (x == SystemIcons.Exclamation || x == SystemIcons.Warning)
                {
                    return NativeMethods.GetStockIcon(NativeMethods.SHSTOCKICONID.SIID_WARNING).ToBitmap();
                }

                if (x == SystemIcons.Question)
                {
                    return NativeMethods.GetStockIcon(NativeMethods.SHSTOCKICONID.SIID_HELP).ToBitmap();
                }

                if (x == SystemIcons.Shield)
                {
                    return NativeMethods.GetStockIcon(NativeMethods.SHSTOCKICONID.SIID_SHIELD).ToBitmap();
                }

                if (x == SystemIcons.WinLogo)
                {
                    return SystemIcons.WinLogo.ToBitmap();
                }

                throw new ArgumentOutOfRangeException(nameof(objIcon));
            }, t), token);
        }

    }
}
