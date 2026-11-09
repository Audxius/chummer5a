/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using System.Threading.Tasks;
using Chummer.Desktop.ViewModels;
using Chummer.Rules;

namespace Chummer.Desktop.Views.Dialogs
{
    /// <summary>The ledger's dialogs as real Avalonia windows, owned by the main window.</summary>
    public sealed class LedgerDialogs : ILedgerDialogs
    {
        public async Task<ExpenseInput> AskExpenseAsync(ExpenseDialogOptions objOptions)
        {
            ExpenseDialog objDialog = new ExpenseDialog(objOptions);
            await objDialog.ShowModalAsync(HostServices.MainWindow).ConfigureAwait(true);
            return objDialog.Result;
        }

        public async Task<(int Year, int Week)?> AskCalendarStartAsync(string strTitle, int intYear, int intWeek)
        {
            CalendarStartDialog objDialog = new CalendarStartDialog(strTitle, intYear, intWeek);
            await objDialog.ShowModalAsync(HostServices.MainWindow).ConfigureAwait(true);
            return objDialog.Result;
        }

        public async Task<(string Notes, System.Drawing.Color Color)?> AskWeekNotesAsync(string strTitle, string strNotes, System.Drawing.Color objColor)
        {
            WeekNotesDialog objDialog = new WeekNotesDialog(strTitle, strNotes, objColor);
            await objDialog.ShowModalAsync(HostServices.MainWindow).ConfigureAwait(true);
            return objDialog.Result;
        }
    }
}
