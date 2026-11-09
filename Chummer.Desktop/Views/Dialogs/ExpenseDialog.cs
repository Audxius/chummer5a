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
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Chummer.Desktop.Localization;
using Chummer.Desktop.Services;
using Chummer.Desktop.ViewModels;
using Chummer.Rules;

namespace Chummer.Desktop.Views.Dialogs
{
    /// <summary>Base for the ledger's modal windows: HUD chrome plus a labelled-field layout.</summary>
    public abstract class LedgerDialogBase : HudWindow
    {
        protected LedgerDialogBase(string strTitle, double dblWidth) : base(strTitle, dblWidth)
        {
            KeyDown += (sender, args) =>
            {
                if (args.Key == Key.Escape)
                    Close();
            };
        }

        protected static TextBlock FieldLabel(string strText) => new TextBlock
        {
            Text = (strText ?? string.Empty).ToUpper(GlobalSettings.CultureInfo),
            Classes = { "label" },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };

        /// <summary>Two-column grid: label | control, one row per pair.</summary>
        protected static Grid Fields(params (string Label, Control Field)[] aobjRows)
        {
            Grid objGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            for (int i = 0; i < aobjRows.Length; ++i)
            {
                objGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                if (!string.IsNullOrEmpty(aobjRows[i].Label))
                {
                    TextBlock txtLabel = FieldLabel(aobjRows[i].Label);
                    Grid.SetRow(txtLabel, i);
                    objGrid.Children.Add(txtLabel);
                }
                Control objField = aobjRows[i].Field;
                objField.Margin = new Thickness(0, 0, 0, 10);
                Grid.SetRow(objField, i);
                Grid.SetColumn(objField, 1);
                objGrid.Children.Add(objField);
            }
            return objGrid;
        }

        public Task ShowModalAsync(object owner)
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

    /// <summary>
    /// Native version of the legacy CreateExpense form: amount (with a percentage for nuyen), reason, date, refund, and the
    /// "Working for the Man/People" karma/nuyen exchange with its "Show in Career Karma/Nuyen" option. In edit mode only
    /// reason and date (and the amount of manual entries) can change, like legacy LockFields.
    /// </summary>
    public sealed class ExpenseDialog : LedgerDialogBase
    {
        private readonly ExpenseDialogOptions _objOptions;
        private readonly NumericUpDown _nudAmount;
        private readonly NumericUpDown _nudPercent;
        private readonly TextBox _txtReason;
        private readonly CalendarDatePicker _datDate;
        private readonly CheckBox _chkRefund;
        private readonly CheckBox _chkExchange;
        private readonly CheckBox _chkForceCareerVisible;
        private readonly TextBlock _txtHint;
        private bool _blnBusy;

        public ExpenseInput Result { get; private set; }

        public ExpenseDialog(ExpenseDialogOptions objOptions) : base(objOptions?.Title ?? string.Empty, 560)
        {
            _objOptions = objOptions ?? throw new ArgumentNullException(nameof(objOptions));
            bool blnNuyen = objOptions.Type == ExpenseType.Nuyen;
            decimal decMaximum = 9999999;
            // Legacy CreateExpense.Amount setter + LockFields: negative entries may go down to -max when editable.
            decimal decMinimum = objOptions.Amount < 0
                ? objOptions.AllowAmountEdit ? -decMaximum : objOptions.Amount
                : objOptions.Amount == 0 ? 0 : 1;

            _nudAmount = new NumericUpDown
            {
                Minimum = decMinimum,
                Maximum = decMaximum,
                Value = objOptions.Amount,
                Increment = 1,
                FormatString = blnNuyen ? "#,0.##" : "0",
                IsEnabled = !objOptions.IsEdit || objOptions.AllowAmountEdit,
                MinWidth = 170,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            _nudPercent = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 1000,
                Value = 100,
                Increment = 5,
                FormatString = "0.##",
                IsEnabled = !objOptions.IsEdit,
                Width = 120
            };
            _txtHint = new TextBlock { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Control objAmountRow = _nudAmount;
            if (blnNuyen)
            {
                StackPanel pnlAmount = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                pnlAmount.Children.Add(_nudAmount);
                pnlAmount.Children.Add(new TextBlock { Text = "×", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } });
                pnlAmount.Children.Add(_nudPercent);
                pnlAmount.Children.Add(new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } });
                objAmountRow = pnlAmount;
            }

            _txtReason = new TextBox { Text = objOptions.Reason ?? string.Empty };
            _datDate = new CalendarDatePicker
            {
                SelectedDate = objOptions.Date.Date,
                IsTodayHighlighted = true,
                MinWidth = 220,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            _chkRefund = new CheckBox
            {
                Content = Tr.Get(blnNuyen ? "Checkbox_Expense_RefundNuyen" : "Checkbox_Expense_Refund"),
                IsChecked = objOptions.Refund,
                IsEnabled = !objOptions.IsEdit
            };
            _chkExchange = new CheckBox
            {
                Content = objOptions.ExchangeLabel,
                IsVisible = !string.IsNullOrWhiteSpace(objOptions.ExchangeLabel)
            };
            _chkForceCareerVisible = new CheckBox
            {
                Content = Tr.Get("Checkbox_Expense_ForceCareerVisible"),
                IsChecked = objOptions.ForceCareerVisible,
                IsEnabled = false,
                IsVisible = _chkExchange.IsVisible,
                Margin = new Thickness(24, 0, 0, 0)
            };
            _chkExchange.IsCheckedChanged += (sender, args) => OnExchangeChanged();
            _nudAmount.ValueChanged += (sender, args) => UpdateHint();
            _nudPercent.ValueChanged += (sender, args) => UpdateHint();

            StackPanel pnlOptions = new StackPanel { Spacing = 6, Children = { _chkRefund, _chkExchange, _chkForceCareerVisible } };

            Button cmdOk = new Button { Content = Tr.Get("String_OK"), IsDefault = true, Classes = { "primary" }, MinWidth = 90 };
            Button cmdCancel = new Button { Content = Tr.Get("String_Cancel"), IsCancel = true, MinWidth = 90 };
            cmdOk.Click += async (sender, args) => await AcceptAsync().ConfigureAwait(true);
            cmdCancel.Click += (sender, args) => Close();

            StackPanel pnlBody = new StackPanel
            {
                Children =
                {
                    Heading(Title),
                    Fields((Tr.Get(blnNuyen ? "Label_Expense_NuyenAmount" : "Label_Expense_KarmaAmount"), objAmountRow),
                           (string.Empty, _txtHint),
                           (Tr.Get("Label_Expense_Description"), _txtReason),
                           (Tr.Get("Label_Expense_Date"), _datDate),
                           (string.Empty, pnlOptions)),
                    ButtonRow(cmdOk, cmdCancel)
                }
            };
            if (objOptions.IsEdit && !objOptions.AllowAmountEdit)
                pnlBody.Children.Insert(1, new TextBlock
                {
                    Text = "This entry was made by the sheet; only its reason and date can be edited (undo it to reverse it).",
                    Classes = { "muted" },
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                });
            Content = Frame(pnlBody, objOptions.IsGain ? "lit" : "hot");
            Opened += (sender, args) =>
            {
                if (_nudAmount.IsEnabled)
                    _nudAmount.Focus();
                else
                    _txtReason.Focus();
            };
            UpdateHint();
        }

        /// <summary>Legacy chkKarmaNuyenExchange_CheckedChanged.</summary>
        private void OnExchangeChanged()
        {
            bool blnChecked = _chkExchange.IsChecked == true;
            if (blnChecked && !string.IsNullOrWhiteSpace(_objOptions.ExchangeLabel))
                _txtReason.Text = _objOptions.ExchangeLabel;
            if (blnChecked && _objOptions.Type == ExpenseType.Nuyen && _objOptions.NuyenPerKarma > 0)
            {
                _nudAmount.Increment = _objOptions.NuyenPerKarma;
                _nudAmount.Value = _objOptions.NuyenPerKarma;
            }
            else
                _nudAmount.Increment = 1;
            _chkForceCareerVisible.IsEnabled = blnChecked;
            if (!blnChecked)
                _chkForceCareerVisible.IsChecked = false;
            UpdateHint();
        }

        private decimal FinalAmount()
        {
            decimal decAmount = _nudAmount.Value ?? 0;
            if (_objOptions.Type == ExpenseType.Nuyen)
                decAmount *= (_nudPercent.Value ?? 100) / 100.0m;
            return decAmount;
        }

        private void UpdateHint()
        {
            string strHint = string.Empty;
            if (_objOptions.Type == ExpenseType.Nuyen && (_nudPercent.Value ?? 100) != 100)
                strHint = "= " + FinalAmount().ToString(_objOptions.NuyenFormat, GlobalSettings.CultureInfo) + LanguageManager.GetString("String_NuyenSymbol");
            if (_chkExchange.IsChecked == true && _objOptions.NuyenPerKarma > 0)
            {
                decimal decAmount = _nudAmount.Value ?? 0;
                string strExchange = _objOptions.Type == ExpenseType.Karma
                    ? (_objOptions.IsGain ? "Also pays " : "Also earns ") + (decAmount * _objOptions.NuyenPerKarma).ToString(_objOptions.NuyenFormat, GlobalSettings.CultureInfo)
                      + LanguageManager.GetString("String_NuyenSymbol")
                    : (_objOptions.IsGain ? "Also costs " : "Also earns ") + decimal.Floor(decAmount / _objOptions.NuyenPerKarma).ToString(GlobalSettings.CultureInfo) + " karma";
                strHint = (strHint.Length > 0 ? strHint + "   //   " : string.Empty) + strExchange;
            }
            _txtHint.Text = strHint;
            _txtHint.IsVisible = strHint.Length > 0;
        }

        private async Task AcceptAsync()
        {
            if (_blnBusy)
                return;
            _blnBusy = true;
            try
            {
                DateTime datPicked = _datDate.SelectedDate ?? _objOptions.Date;
                ExpenseInput objInput = new ExpenseInput
                {
                    Amount = FinalAmount(),
                    Reason = _txtReason.Text ?? string.Empty,
                    // Keep the time of day of the original (or "now"), the picker only chooses the day.
                    Date = datPicked.Date + _objOptions.Date.TimeOfDay,
                    Refund = _chkRefund.IsChecked == true,
                    KarmaNuyenExchange = _chkExchange.IsVisible && _chkExchange.IsChecked == true,
                    ForceCareerVisible = _chkForceCareerVisible.IsChecked == true
                };
                if (_objOptions.Validate != null && !await _objOptions.Validate(objInput).ConfigureAwait(true))
                    return;
                Result = objInput;
                Close();
            }
            finally
            {
                _blnBusy = false;
            }
        }
    }
}
