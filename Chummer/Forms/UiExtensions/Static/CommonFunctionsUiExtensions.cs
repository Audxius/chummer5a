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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
using Chummer.Backend.Equipment;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Microsoft.IO;

namespace Chummer
{
    /// <summary>
    /// UI (WinForms) members of <see cref="Chummer.CommonFunctions"/>, moved out of the engine for the native port.
    /// </summary>
    public static class CommonFunctionsUiExtensions
    {
        public static void ShiftTabsOnMouseScroll(object sender, MouseEventArgs e)
        {
            if (!GlobalSettings.SwitchTabsOnHoverScroll || e == null)
                return;
            if (sender is TabControl tabControl && tabControl.DisplayRectangle.Contains(e.Location))
            {
                tabControl.SelectedIndex = (tabControl.SelectedIndex + e.Delta) % tabControl.TabCount;
            }
        }

        /// <summary>
        /// Opens a PDF file using the provided source information.
        /// </summary>
        /// <param name="sender">Control from which this method was called.</param>
        /// <param name="token">Cancellation token to listen to.</param>
        public static async Task OpenPdfFromControl(object sender, CancellationToken token = default)
        {
            if (!(sender is Control objControl))
                return;
            Control objLoopControl = objControl;
            CharacterSettings objSettings = null;
            for (; objLoopControl != null; objLoopControl = await objLoopControl.DoThreadSafeFuncAsync(x => x.Parent, token: token).ConfigureAwait(false))
            {
                if (objLoopControl is IHasCharacterObject objShared)
                {
                    Character objCharacter = objShared.CharacterObject;
                    if (objCharacter != null)
                    {
                        objSettings = await objCharacter.GetSettingsAsync(token).ConfigureAwait(false);
                        break;
                    }
                }
            }
            await OpenPdfFromControl(sender, objSettings, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Opens a PDF file using the provided source information.
        /// </summary>
        /// <param name="sender">Control from which this method was called.</param>
        /// <param name="objSettings">Settings to use for custom data. If null, no custom data will be used.</param>
        /// <param name="token">Cancellation token to listen to.</param>
        public static async Task OpenPdfFromControl(object sender, CharacterSettings objSettings, CancellationToken token = default)
        {
            if (!(sender is Control objControl))
                return;
            CursorWait objCursorWait
                = await CursorWait.NewAsync(await objControl.DoThreadSafeFuncAsync(x => x.FindForm(), token: token).ConfigureAwait(false) ?? objControl, token: token).ConfigureAwait(false);
            try
            {
                await CommonFunctions.OpenPdf(await objControl.DoThreadSafeFuncAsync(x => x.Text, token: token).ConfigureAwait(false), objSettings, string.Empty,
                              string.Empty, true, token).ConfigureAwait(false);
            }
            finally
            {
                await objCursorWait.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
