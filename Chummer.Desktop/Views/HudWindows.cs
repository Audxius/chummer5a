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
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Chummer.Desktop.Localization;
using Chummer.Desktop.Services;

namespace Chummer.Desktop.Views
{
    /// <summary>
    /// Shared chrome for the app's small modal windows: HUD panel with a title strip.
    /// </summary>
    public abstract class HudWindow : Window
    {
        protected HudWindow(string strTitle, double dblWidth)
        {
            Title = strTitle;
            Width = dblWidth;
            SizeToContent = SizeToContent.Height;
            MaxHeight = 720;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
        }

        protected static TextBlock Heading(string strText) => new TextBlock
        {
            Text = (strText ?? string.Empty).ToUpper(GlobalSettings.CultureInfo),
            Classes = { "h2" },
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };

        protected static Border Frame(Control objContent, string strClass = "lit") => new Border
        {
            Classes = { "panel", strClass },
            Margin = new Thickness(10),
            Child = objContent
        };

        protected static StackPanel ButtonRow(params Button[] aobjButtons)
        {
            StackPanel objRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 14, 0, 0)
            };
            foreach (Button objButton in aobjButtons)
                objRow.Children.Add(objButton);
            return objRow;
        }
    }

    public sealed class MessageWindow : HudWindow
    {
        public MessageResult Result { get; private set; } = MessageResult.Cancel;

        public MessageWindow(string strMessage, string strCaption, MessageButtons eButtons, MessageIcon eIcon, MessageDefaultButton eDefault)
            : base(string.IsNullOrEmpty(strCaption) ? "Chummer" : strCaption, 520)
        {
            bool blnAlarm = eIcon == MessageIcon.Error || eIcon == MessageIcon.Warning;
            List<(MessageResult Result, string Key)> lstButtons = new List<(MessageResult, string)>();
            switch (eButtons)
            {
                case MessageButtons.OKCancel:
                    lstButtons.Add((MessageResult.OK, "String_OK"));
                    lstButtons.Add((MessageResult.Cancel, "String_Cancel"));
                    break;
                case MessageButtons.YesNo:
                    lstButtons.Add((MessageResult.Yes, "String_Yes"));
                    lstButtons.Add((MessageResult.No, "String_No"));
                    break;
                case MessageButtons.YesNoCancel:
                    lstButtons.Add((MessageResult.Yes, "String_Yes"));
                    lstButtons.Add((MessageResult.No, "String_No"));
                    lstButtons.Add((MessageResult.Cancel, "String_Cancel"));
                    break;
                case MessageButtons.RetryCancel:
                    lstButtons.Add((MessageResult.Retry, "String_Retry"));
                    lstButtons.Add((MessageResult.Cancel, "String_Cancel"));
                    break;
                case MessageButtons.AbortRetryIgnore:
                    lstButtons.Add((MessageResult.Abort, "String_Abort"));
                    lstButtons.Add((MessageResult.Retry, "String_Retry"));
                    lstButtons.Add((MessageResult.Ignore, "String_Ignore"));
                    break;
                default:
                    lstButtons.Add((MessageResult.OK, "String_OK"));
                    break;
            }
            int intDefault = eDefault == MessageDefaultButton.Button3 ? 2 : eDefault == MessageDefaultButton.Button2 ? 1 : 0;
            Button[] aobjButtons = lstButtons.Select((x, i) =>
            {
                Button objButton = new Button { Content = ButtonText(x.Key, x.Result), IsDefault = i == intDefault };
                if (i == intDefault)
                    objButton.Classes.Add(blnAlarm ? "hot" : "primary");
                objButton.Click += (sender, args) =>
                {
                    Result = x.Result;
                    Close(x.Result);
                };
                return objButton;
            }).ToArray();
            if (lstButtons.Any(x => x.Result == MessageResult.Cancel))
                KeyDown += (sender, args) =>
                {
                    if (args.Key == Key.Escape)
                        Close(MessageResult.Cancel);
                };

            StackPanel objBody = new StackPanel
            {
                Children =
                {
                    Heading(Title),
                    new ScrollViewer
                    {
                        MaxHeight = 480,
                        Content = new SelectableTextBlock { Text = strMessage ?? string.Empty, TextWrapping = TextWrapping.Wrap }
                    },
                    ButtonRow(aobjButtons)
                }
            };
            Content = Frame(objBody, blnAlarm ? "hot" : "lit");
        }

        private static string ButtonText(string strKey, MessageResult eResult)
        {
            string strText = LanguageManager.GetString(strKey, false);
            return string.IsNullOrEmpty(strText) ? eResult.ToString() : strText;
        }
    }

    /// <summary>
    /// Generic prompts: pick from a searchable list, enter a number, enter text.
    /// </summary>
    public sealed class PromptWindow : HudWindow
    {
        private object _objResult;

        private PromptWindow(string strTitle) : base(strTitle, 560)
        {
        }

        /// <summary>
        /// Search-first browser: list on the left, details of the highlighted entry on the right, an "only show what I can take"
        /// toggle (re-queries <paramref name="funcSource"/>), and a validation hook before accepting.
        /// </summary>
        public static async Task<ListItem?> BrowseAsync(object owner, string strTitle, Func<bool, IReadOnlyList<ListItem>> funcSource,
                                                        Func<ListItem, string> funcDetails, Func<ListItem, bool> funcValidate = null, string strLimitLabel = null)
        {
            PromptWindow objWindow = new PromptWindow(strTitle) { Width = 860 };
            bool blnLimit = true;
            IReadOnlyList<ListItem> lstAll = funcSource(blnLimit);
            TextBox txtSearch = new TextBox { PlaceholderText = Tr.Get("String_Search") };
            ListBox lstChoices = new ListBox { Height = 420, ItemsSource = lstAll };
            lstChoices.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ListItem>((x, _) => new TextBlock { Text = x.Name });
            TextBlock txtDetails = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            CheckBox chkLimit = new CheckBox { Content = strLimitLabel ?? "Only show what this runner can take", IsChecked = true };
            void Refilter()
            {
                string strFilter = txtSearch.Text ?? string.Empty;
                lstChoices.ItemsSource = strFilter.Length == 0
                    ? lstAll
                    : lstAll.Where(x => (x.Name ?? string.Empty).IndexOf(strFilter, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            }
            txtSearch.TextChanged += (sender, args) => Refilter();
            chkLimit.IsCheckedChanged += (sender, args) =>
            {
                blnLimit = chkLimit.IsChecked == true;
                lstAll = funcSource(blnLimit);
                Refilter();
            };
            lstChoices.SelectionChanged += (sender, args) =>
                txtDetails.Text = lstChoices.SelectedItem is ListItem objItem ? funcDetails?.Invoke(objItem) ?? string.Empty : string.Empty;
            Button cmdOk = new Button { Content = Tr.Get("String_OK"), IsDefault = true, Classes = { "primary" } };
            Button cmdCancel = new Button { Content = Tr.Get("String_Cancel"), IsCancel = true };
            void Accept()
            {
                if (lstChoices.SelectedItem is ListItem objItem && (funcValidate == null || funcValidate(objItem)))
                {
                    objWindow._objResult = objItem;
                    objWindow.Close();
                }
            }
            cmdOk.Click += (sender, args) => Accept();
            lstChoices.DoubleTapped += (sender, args) => Accept();
            cmdCancel.Click += (sender, args) => objWindow.Close();
            Grid grdBody = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
            grdBody.Children.Add(lstChoices);
            ScrollViewer scrDetails = new ScrollViewer { Content = txtDetails, Margin = new Avalonia.Thickness(14, 0, 0, 0), MaxHeight = 420 };
            Grid.SetColumn(scrDetails, 1);
            grdBody.Children.Add(scrDetails);
            objWindow.Content = Frame(new StackPanel
            {
                Spacing = 8,
                Children = { Heading(strTitle), txtSearch, chkLimit, grdBody, ButtonRow(cmdOk, cmdCancel) }
            });
            objWindow.Opened += (sender, args) => txtSearch.Focus();
            await objWindow.ShowModalAsync(owner).ConfigureAwait(true);
            return objWindow._objResult is ListItem objResult ? objResult : (ListItem?)null;
        }

        public static async Task<ListItem?> ChooseAsync(object owner, string strTitle, IReadOnlyList<ListItem> lstItems, string strPreselected)
        {
            PromptWindow objWindow = new PromptWindow(strTitle);
            TextBox txtSearch = new TextBox { PlaceholderText = Tr.Get("String_Search") };
            ListBox lstChoices = new ListBox { Height = 360, ItemsSource = lstItems };
            lstChoices.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ListItem>((x, _) => new TextBlock { Text = x.Name });
            if (!string.IsNullOrEmpty(strPreselected))
                lstChoices.SelectedItem = lstItems.FirstOrDefault(x => x.Value?.ToString() == strPreselected);
            txtSearch.TextChanged += (sender, args) =>
            {
                string strFilter = txtSearch.Text ?? string.Empty;
                lstChoices.ItemsSource = strFilter.Length == 0
                    ? lstItems
                    : lstItems.Where(x => (x.Name ?? string.Empty).IndexOf(strFilter, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            };
            Button cmdOk = new Button { Content = Tr.Get("String_OK"), IsDefault = true, Classes = { "primary" } };
            Button cmdCancel = new Button { Content = Tr.Get("String_Cancel"), IsCancel = true };
            void Accept()
            {
                if (lstChoices.SelectedItem is ListItem objItem)
                {
                    objWindow._objResult = objItem;
                    objWindow.Close();
                }
            }
            cmdOk.Click += (sender, args) => Accept();
            lstChoices.DoubleTapped += (sender, args) => Accept();
            cmdCancel.Click += (sender, args) => objWindow.Close();
            objWindow.Content = Frame(new StackPanel
            {
                Spacing = 8,
                Children = { Heading(strTitle), txtSearch, lstChoices, ButtonRow(cmdOk, cmdCancel) }
            });
            objWindow.Opened += (sender, args) => txtSearch.Focus();
            await objWindow.ShowModalAsync(owner).ConfigureAwait(true);
            return objWindow._objResult is ListItem objResult ? objResult : (ListItem?)null;
        }

        public static async Task<decimal?> AskNumberAsync(object owner, string strTitle, decimal decMinimum, decimal decMaximum, bool blnAllowCancel)
        {
            PromptWindow objWindow = new PromptWindow(strTitle);
            NumericUpDown nudValue = new NumericUpDown
            {
                Minimum = decMinimum,
                Maximum = decMaximum > decMinimum ? decMaximum : decimal.MaxValue,
                Value = decMinimum,
                Increment = 1
            };
            Button cmdOk = new Button { Content = Tr.Get("String_OK"), IsDefault = true, Classes = { "primary" } };
            cmdOk.Click += (sender, args) =>
            {
                objWindow._objResult = nudValue.Value ?? decMinimum;
                objWindow.Close();
            };
            List<Button> lstButtons = new List<Button> { cmdOk };
            if (blnAllowCancel)
            {
                Button cmdCancel = new Button { Content = Tr.Get("String_Cancel"), IsCancel = true };
                cmdCancel.Click += (sender, args) => objWindow.Close();
                lstButtons.Add(cmdCancel);
            }
            objWindow.Content = Frame(new StackPanel { Spacing = 8, Children = { Heading(strTitle), nudValue, ButtonRow(lstButtons.ToArray()) } });
            await objWindow.ShowModalAsync(owner).ConfigureAwait(true);
            return objWindow._objResult is decimal decResult ? decResult : blnAllowCancel ? (decimal?)null : decMinimum;
        }

        public static async Task<string> AskTextAsync(object owner, string strTitle, string strDefault)
        {
            PromptWindow objWindow = new PromptWindow(strTitle);
            TextBox txtValue = new TextBox { Text = strDefault ?? string.Empty };
            Button cmdOk = new Button { Content = Tr.Get("String_OK"), IsDefault = true, Classes = { "primary" } };
            Button cmdCancel = new Button { Content = Tr.Get("String_Cancel"), IsCancel = true };
            cmdOk.Click += (sender, args) =>
            {
                objWindow._objResult = txtValue.Text ?? string.Empty;
                objWindow.Close();
            };
            cmdCancel.Click += (sender, args) => objWindow.Close();
            objWindow.Content = Frame(new StackPanel { Spacing = 8, Children = { Heading(strTitle), txtValue, ButtonRow(cmdOk, cmdCancel) } });
            objWindow.Opened += (sender, args) => txtValue.Focus();
            await objWindow.ShowModalAsync(owner).ConfigureAwait(true);
            return objWindow._objResult as string;
        }

        private Task ShowModalAsync(object owner)
        {
            Window objOwner = ModalHelper.ResolveOwner(owner);
            if (objOwner != null && objOwner.IsVisible)
                return ShowDialog(objOwner);
            TaskCompletionSource<bool> objClosed = new TaskCompletionSource<bool>();
            Closed += (sender, args) => objClosed.TrySetResult(true);
            Show();
            return objClosed.Task;
        }
    }
}
