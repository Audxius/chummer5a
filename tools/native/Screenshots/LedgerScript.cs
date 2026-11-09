// Drives the career LEDGER section (karma/nuyen log, undo, edit, calendar) for the screenshot harness.
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Chummer;
using Chummer.Desktop.ViewModels;
using Chummer.Desktop.Views;
using Chummer.Desktop.Views.Dialogs;
using Chummer.Rules;

internal static class LedgerScript
{
    /// <summary>Shows each real dialog, captures it, then answers with a scripted value.</summary>
    private sealed class ScriptedDialogs : ILedgerDialogs
    {
        private readonly string _strDir;
        public Func<ExpenseDialogOptions, ExpenseInput> NextExpense { get; set; }
        public (int Year, int Week)? NextStart { get; set; }
        public (string Notes, System.Drawing.Color Color)? NextNotes { get; set; }
        public string ShotName { get; set; }

        public ScriptedDialogs(string strDir) => _strDir = strDir;

        public async Task<ExpenseInput> AskExpenseAsync(ExpenseDialogOptions objOptions)
        {
            await Capture(new ExpenseDialog(objOptions));
            return NextExpense?.Invoke(objOptions);
        }

        public async Task<(int Year, int Week)?> AskCalendarStartAsync(string strTitle, int intYear, int intWeek)
        {
            await Capture(new CalendarStartDialog(strTitle, intYear, intWeek));
            return NextStart;
        }

        public async Task<(string Notes, System.Drawing.Color Color)?> AskWeekNotesAsync(string strTitle, string strNotes, System.Drawing.Color objColor)
        {
            await Capture(new WeekNotesDialog(strTitle, strNotes, objColor));
            return NextNotes;
        }

        private async Task Capture(Window objDialog)
        {
            if (string.IsNullOrEmpty(ShotName))
                return;
            objDialog.Show();
            await Settle();
            Save(objDialog, _strDir, ShotName);
            objDialog.Close();
            ShotName = null;
        }
    }

    public static async Task RunAsync(Window objWindow, CharacterViewModel objCharacter, string strOut)
    {
        ScriptedDialogs objDialogs = new ScriptedDialogs(strOut);
        LedgerViewModel.Dialogs = objDialogs;
        LedgerViewModel objLedger = objCharacter.Ledger;
        objCharacter.ShowSectionCommand.Execute(CharacterSection.Ledger);
        await Settle();
        Save(objWindow, strOut, "08-ledger");
        Console.WriteLine("ledger: karma " + objLedger.KarmaText + " (career " + objLedger.CareerKarmaText + "), nuyen " + objLedger.NuyenText
                          + ", " + objLedger.KarmaRows.Count + " karma rows, " + objLedger.NuyenRows.Count + " nuyen rows");

        // + KARMA through the real command (dialog captured, then answered).
        string strBefore = objLedger.KarmaText;
        objDialogs.ShotName = "08a-dialog-karma";
        objDialogs.NextExpense = o => new ExpenseInput { Amount = 7, Reason = "Run: Bug City extraction", Date = DateTime.Now };
        await objLedger.GainKarmaCommand.ExecuteAsync(null);
        await Settle();
        Console.WriteLine("gain karma 7: " + strBefore + " -> " + objLedger.KarmaText + "; top row: " + objLedger.KarmaRows.FirstOrDefault()?.Reason);

        // - NUYEN with the percentage field.
        strBefore = objLedger.NuyenText;
        objDialogs.ShotName = "08b-dialog-nuyen";
        objDialogs.NextExpense = o => new ExpenseInput { Amount = 1500, Reason = "Fixer's cut", Date = DateTime.Now };
        await objLedger.SpendNuyenCommand.ExecuteAsync(null);
        await Settle();
        Console.WriteLine("spend nuyen 1500: " + strBefore + " -> " + objLedger.NuyenText);
        Save(objWindow, strOut, "08c-ledger-after-entries");

        // Undo the attribute the harness raised earlier (newest ImproveAttribute entry).
        ExpenseRowViewModel objAttributeRow = objLedger.KarmaRows.FirstOrDefault(x => x.Kind == "IMPROVE ATTRIBUTE");
        if (objAttributeRow != null)
        {
            string strAttribute = objAttributeRow.Entry.Undo.ObjectId;
            int intValueBefore = objCharacter.Character.GetAttribute(strAttribute).Value;
            strBefore = objLedger.KarmaText;
            await objAttributeRow.UndoCommand.ExecuteAsync(null);
            await Settle();
            Console.WriteLine("undo " + objAttributeRow.Reason + ": " + strAttribute + " " + intValueBefore + " -> "
                              + objCharacter.Character.GetAttribute(strAttribute).Value + ", karma " + strBefore + " -> " + objLedger.KarmaText);
        }

        // Edit the manual entry (dialog captured in edit mode).
        ExpenseRowViewModel objManual = objLedger.KarmaRows.FirstOrDefault(x => x.IsManual);
        if (objManual != null)
        {
            objDialogs.ShotName = "08d-dialog-edit";
            objDialogs.NextExpense = o => new ExpenseInput { Amount = o.Amount + 1, Reason = o.Reason + " (bonus)", Date = o.Date };
            await objManual.EditCommand.ExecuteAsync(null);
            await Settle();
            Console.WriteLine("edit: " + objLedger.KarmaRows.FirstOrDefault()?.Reason + " " + objLedger.KarmaRows.FirstOrDefault()?.AmountText + ", karma " + objLedger.KarmaText);
        }
        ExpenseRowViewModel objSheetEntry = objLedger.NuyenRows.FirstOrDefault(x => !x.IsManual && x.CanUndo);
        if (objSheetEntry != null)
        {
            objDialogs.ShotName = "08e-dialog-edit-locked";
            objDialogs.NextExpense = o => null;
            await objSheetEntry.EditCommand.ExecuteAsync(null);
        }
        Save(objWindow, strOut, "08f-ledger-after-undo");

        // Calendar: start it, add weeks, write notes.
        objLedger.ShowCalendarPageCommand.Execute(null);
        await Settle();
        Save(objWindow, strOut, "09-calendar-empty");
        objDialogs.ShotName = "09a-dialog-calendar-start";
        objDialogs.NextStart = (2082, 10);
        await objLedger.AddWeekCommand.ExecuteAsync(null);
        await objLedger.AddWeekCommand.ExecuteAsync(null);
        await objLedger.AddWeekCommand.ExecuteAsync(null);
        await Settle();
        CalendarWeekRowViewModel objWeek = objLedger.Weeks.Skip(1).FirstOrDefault();
        if (objWeek != null)
        {
            objDialogs.ShotName = "09b-dialog-week-notes";
            objDialogs.NextNotes = ("Bug City extraction. Mr. Johnson paid half up front; Lone Star patrols doubled in the Barrens.", System.Drawing.Color.FromArgb(0x00, 0xE5, 0xFF));
            await objWeek.EditCommand.ExecuteAsync(null);
            await Settle();
        }
        objDialogs.ShotName = null;
        objDialogs.NextStart = (2082, 20);
        await objLedger.ChangeStartCommand.ExecuteAsync(null);
        await Settle();
        Console.WriteLine("calendar: " + objLedger.CalendarSummary + " | " + string.Join(", ", objLedger.Weeks.Select(x => x.Name + (x.HasNotes ? "*" : string.Empty))));
        Save(objWindow, strOut, "09c-calendar");
        objLedger.ShowLogPageCommand.Execute(null);
        await Settle();
    }

    private static async Task Settle()
    {
        for (int i = 0; i < 20; ++i)
        {
            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void Save(Window objWindow, string strDir, string strName)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        WriteableBitmap objFrame = objWindow.CaptureRenderedFrame();
        objFrame?.Save(Path.Combine(strDir, strName + ".png"));
        Console.WriteLine("saved " + strName);
    }
}
