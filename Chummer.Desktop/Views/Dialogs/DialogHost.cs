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

using System.Threading.Tasks;
using Avalonia.Controls;
using Chummer.Desktop.Services;
using Chummer.Selection;

namespace Chummer.Desktop.Views.Dialogs
{
    /// <summary>Shows the editor dialogs modally over the right window, and the catalogue browser for the plain pickers.</summary>
    internal static class DialogHost
    {
        public static Task ShowModalAsync(Window objWindow, object owner)
        {
            Window objOwner = ModalHelper.ResolveOwner(owner);
            if (objOwner != null && objOwner.IsVisible)
                return objWindow.ShowDialog(objOwner);
            TaskCompletionSource<bool> objClosed = new TaskCompletionSource<bool>();
            objWindow.Closed += (sender, args) => objClosed.TrySetResult(true);
            objWindow.Show();
            return objClosed.Task;
        }

        /// <summary>The searchable catalogue browser (list, details, "only what I can take") returning the chosen id.</summary>
        public static async Task<string> BrowseAsync(object owner, DataBrowser objBrowser, string strTitle = null)
        {
            ListItem? objChoice = await PromptWindow.BrowseAsync(owner,
                                                                 strTitle ?? string.Format(GlobalSettings.CultureInfo, LanguageManager.GetString("Title_SelectGeneric"), objBrowser.LocalName),
                                                                 objBrowser.Options, x => objBrowser.Details(x.Value?.ToString()),
                                                                 x => objBrowser.Validate(x.Value?.ToString())).ConfigureAwait(true);
            return objChoice?.Value?.ToString();
        }
    }
}
