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
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Chummer.Desktop.Localization;
using Chummer.Desktop.Services;

namespace Chummer.Desktop.Views.Dialogs
{
    /// <summary>
    /// The legacy editable combo box as a window: type to filter the list, pick an entry, or (when free text is allowed)
    /// accept exactly what you typed — e.g. SelectSpec's "Custom" specialization or a knowledge skill nobody listed.
    /// </summary>
    public sealed class ChooseOrTypeWindow : HudWindow
    {
        private string _strResult;

        private ChooseOrTypeWindow(string strTitle) : base(strTitle, 520)
        {
        }

        /// <summary>Shows the picker on the UI thread; returns the chosen value (ListItem.Value or typed text), or null when cancelled.</summary>
        public static Task<string> ShowAsync(string strTitle, IReadOnlyList<ListItem> lstItems, bool blnAllowFreeText, string strInitial = null, string strHint = null)
        {
            if (Dispatcher.UIThread.CheckAccess())
                return ShowCoreAsync(strTitle, lstItems, blnAllowFreeText, strInitial, strHint);
            return Dispatcher.UIThread.InvokeAsync(() => ShowCoreAsync(strTitle, lstItems, blnAllowFreeText, strInitial, strHint));
        }

        private static async Task<string> ShowCoreAsync(string strTitle, IReadOnlyList<ListItem> lstItems, bool blnAllowFreeText, string strInitial, string strHint)
        {
            lstItems = lstItems ?? Array.Empty<ListItem>();
            ChooseOrTypeWindow objWindow = new ChooseOrTypeWindow(strTitle);
            TextBox txtInput = new TextBox
            {
                Text = strInitial ?? string.Empty,
                PlaceholderText = blnAllowFreeText ? "Type a name or pick from the list" : Tr.Get("String_Search")
            };
            ListBox lstChoices = new ListBox { Height = lstItems.Count == 0 ? 0 : 320, ItemsSource = lstItems, IsVisible = lstItems.Count > 0 };
            lstChoices.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ListItem>((x, _) => new TextBlock { Text = x.Name });
            TextBlock txtHint = new TextBlock
            {
                Text = strHint ?? string.Empty,
                IsVisible = !string.IsNullOrEmpty(strHint),
                TextWrapping = TextWrapping.Wrap,
                Classes = { "muted" },
                FontSize = 12
            };
            bool blnSyncing = false;
            txtInput.TextChanged += (sender, args) =>
            {
                if (blnSyncing)
                    return;
                string strFilter = txtInput.Text ?? string.Empty;
                lstChoices.ItemsSource = strFilter.Length == 0
                    ? lstItems
                    : lstItems.Where(x => (x.Name ?? string.Empty).IndexOf(strFilter, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            };
            lstChoices.SelectionChanged += (sender, args) =>
            {
                if (blnAllowFreeText && lstChoices.SelectedItem is ListItem objItem)
                {
                    blnSyncing = true;
                    txtInput.Text = objItem.Name;
                    blnSyncing = false;
                }
            };
            Button cmdOk = new Button { Content = Tr.Get("String_OK"), IsDefault = true, Classes = { "primary" } };
            Button cmdCancel = new Button { Content = Tr.Get("String_Cancel"), IsCancel = true };
            void Accept()
            {
                if (lstChoices.SelectedItem is ListItem objItem
                    && (!blnAllowFreeText || string.Equals(objItem.Name, txtInput.Text, StringComparison.CurrentCulture)))
                    objWindow._strResult = objItem.Value?.ToString();
                else if (blnAllowFreeText && !string.IsNullOrWhiteSpace(txtInput.Text))
                {
                    // Typed text that matches an entry's display name means that entry.
                    ListItem objMatch = lstItems.FirstOrDefault(x => string.Equals(x.Name, txtInput.Text.Trim(), StringComparison.CurrentCultureIgnoreCase));
                    objWindow._strResult = objMatch.Value?.ToString() ?? txtInput.Text.Trim();
                }
                else if (!blnAllowFreeText && lstChoices.ItemsSource is IReadOnlyList<ListItem> lstShown && lstShown.Count == 1)
                    objWindow._strResult = lstShown[0].Value?.ToString();
                if (!string.IsNullOrEmpty(objWindow._strResult))
                    objWindow.Close();
            }
            cmdOk.Click += (sender, args) => Accept();
            lstChoices.DoubleTapped += (sender, args) => Accept();
            cmdCancel.Click += (sender, args) => objWindow.Close();
            objWindow.KeyDown += (sender, args) =>
            {
                if (args.Key == Key.Escape)
                    objWindow.Close();
            };
            objWindow.Content = Frame(new StackPanel
            {
                Spacing = 8,
                Children = { Heading(strTitle), txtHint, txtInput, lstChoices, ButtonRow(cmdOk, cmdCancel) }
            });
            objWindow.Opened += (sender, args) => txtInput.Focus();
            Window objOwner = ModalHelper.ResolveOwner(null);
            if (objOwner != null && objOwner.IsVisible)
                await objWindow.ShowDialog(objOwner).ConfigureAwait(true);
            else
            {
                TaskCompletionSource<bool> objClosed = new TaskCompletionSource<bool>();
                objWindow.Closed += (sender, args) => objClosed.TrySetResult(true);
                objWindow.Show();
                await objClosed.Task.ConfigureAwait(true);
            }
            return objWindow._strResult;
        }
    }
}
