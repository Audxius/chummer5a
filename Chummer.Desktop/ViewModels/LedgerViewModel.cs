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
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Desktop.Localization;
using Chummer.Rules;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chummer.Desktop.ViewModels
{
    /// <summary>What the expense dialog (legacy CreateExpense form) is opened with.</summary>
    public sealed class ExpenseDialogOptions
    {
        public ExpenseType Type { get; set; }
        public bool IsGain { get; set; }
        public bool IsEdit { get; set; }
        public string Title { get; set; } = string.Empty;
        public decimal Amount { get; set; } = 1;
        public string Reason { get; set; } = string.Empty;
        public DateTime Date { get; set; } = DateTime.Now;
        public bool Refund { get; set; }
        public bool ForceCareerVisible { get; set; }
        /// <summary>Edit mode: only manual entries let the amount change (legacy LockFields).</summary>
        public bool AllowAmountEdit { get; set; } = true;
        /// <summary>Label of the "Working for the Man/People" box; empty hides it (edit mode).</summary>
        public string ExchangeLabel { get; set; } = string.Empty;
        /// <summary>Nuyen per karma when exchanging in nuyen mode (the amount steps in multiples of it).</summary>
        public decimal NuyenPerKarma { get; set; }
        public string NuyenFormat { get; set; } = "#,0.##";
        /// <summary>Checked before the dialog accepts (e.g. nuyen exchange multiples); false keeps the dialog open.</summary>
        public Func<ExpenseInput, Task<bool>> Validate { get; set; }
    }

    /// <summary>Native windows the ledger needs; replaceable so the screenshot harness can script answers.</summary>
    public interface ILedgerDialogs
    {
        Task<ExpenseInput> AskExpenseAsync(ExpenseDialogOptions objOptions);

        /// <summary>SelectCalendarStart: a year and ISO week, or null if cancelled.</summary>
        Task<(int Year, int Week)?> AskCalendarStartAsync(string strTitle, int intYear, int intWeek);

        /// <summary>EditNotes for a calendar week: new notes and colour, or null if cancelled.</summary>
        Task<(string Notes, System.Drawing.Color Color)?> AskWeekNotesAsync(string strTitle, string strNotes, System.Drawing.Color objColor);
    }

    /// <summary>
    /// The career-mode LEDGER section: karma and nuyen totals, manual gains/expenses, the expense logs with edit and undo,
    /// karma/nuyen over time, and the game calendar. Mirrors the legacy CharacterCareer "Karma &amp; Nuyen" and "Calendar" tabs.
    /// </summary>
    public sealed partial class LedgerViewModel : ViewModelBase
    {
        /// <summary>Set by the desktop app (Views/Dialogs/LedgerDialogs); the screenshot harness swaps in scripted answers.</summary>
        public static ILedgerDialogs Dialogs { get; set; } = new Views.Dialogs.LedgerDialogs();

        private readonly CharacterViewModel _objOwner;
        private List<ExpenseRowViewModel> _lstAllKarma = new List<ExpenseRowViewModel>();
        private List<ExpenseRowViewModel> _lstAllNuyen = new List<ExpenseRowViewModel>();

        public LedgerViewModel(CharacterViewModel objOwner)
        {
            _objOwner = objOwner;
        }

        private Character Character => _objOwner.Character;

        [ObservableProperty] private bool _isCareer;
        [ObservableProperty] private bool _showCalendar;
        [ObservableProperty] private string _karmaText = string.Empty;
        [ObservableProperty] private string _careerKarmaText = string.Empty;
        [ObservableProperty] private string _spentKarmaText = string.Empty;
        [ObservableProperty] private string _nuyenText = string.Empty;
        [ObservableProperty] private string _careerNuyenText = string.Empty;
        [ObservableProperty] private string _spentNuyenText = string.Empty;
        [ObservableProperty] private bool _showFreeKarma;
        [ObservableProperty] private bool _showFreeNuyen;
        [ObservableProperty] private IReadOnlyList<LedgerPoint> _karmaChart = Array.Empty<LedgerPoint>();
        [ObservableProperty] private IReadOnlyList<LedgerPoint> _nuyenChart = Array.Empty<LedgerPoint>();
        [ObservableProperty] private string _karmaCountText = string.Empty;
        [ObservableProperty] private string _nuyenCountText = string.Empty;
        [ObservableProperty] private string _calendarSummary = string.Empty;

        public ObservableCollection<ExpenseRowViewModel> KarmaRows { get; } = new ObservableCollection<ExpenseRowViewModel>();
        public ObservableCollection<ExpenseRowViewModel> NuyenRows { get; } = new ObservableCollection<ExpenseRowViewModel>();
        public ObservableCollection<CalendarWeekRowViewModel> Weeks { get; } = new ObservableCollection<CalendarWeekRowViewModel>();

        public bool ShowLog => !ShowCalendar;
        public bool HasKarmaRows => KarmaRows.Count > 0;
        public bool HasNuyenRows => NuyenRows.Count > 0;
        public bool HasWeeks => Weeks.Count > 0;
        public bool HasNoWeeks => Weeks.Count == 0;
        public string AddWeekText => Weeks.Count == 0 ? "START CALENDAR" : "+ NEXT WEEK";

        partial void OnShowCalendarChanged(bool value) => OnPropertyChanged(nameof(ShowLog));

        partial void OnShowFreeKarmaChanged(bool value) => ApplyRows(KarmaRows, _lstAllKarma, value, nameof(HasKarmaRows));

        partial void OnShowFreeNuyenChanged(bool value) => ApplyRows(NuyenRows, _lstAllNuyen, value, nameof(HasNuyenRows));

        [RelayCommand]
        private void ShowLogPage() => ShowCalendar = false;

        [RelayCommand]
        private void ShowCalendarPage() => ShowCalendar = true;

        // ---------- snapshot ----------

        /// <summary>Rebuilds everything from the character (call off the UI thread, from CharacterViewModel.LoadSnapshot).</summary>
        internal void Load()
        {
            Character objCharacter = Character;
            bool blnCareer = objCharacter.Created;
            CultureInfo objCulture = GlobalSettings.CultureInfo;
            string strNuyenFormat = objCharacter.Settings.NuyenFormat;
            string strNuyenSymbol = LanguageManager.GetString("String_NuyenSymbol");
            string strDateFormat = DateFormat();
            List<ExpenseLogEntry> lstEntries = objCharacter.ExpenseEntries.ToList();

            (List<ExpenseRowViewModel> lstKarma, List<LedgerPoint> lstKarmaChart) = BuildRows(lstEntries, ExpenseType.Karma,
                x => x.ToString("#,0.##", objCulture), strDateFormat);
            (List<ExpenseRowViewModel> lstNuyen, List<LedgerPoint> lstNuyenChart) = BuildRows(lstEntries, ExpenseType.Nuyen,
                x => x.ToString(strNuyenFormat, objCulture) + strNuyenSymbol, strDateFormat);

            int intKarma = objCharacter.Karma;
            int intCareerKarma = objCharacter.CareerKarma;
            decimal decNuyen = objCharacter.Nuyen;
            decimal decCareerNuyen = objCharacter.CareerNuyen;
            string strKarma = intKarma.ToString(objCulture);
            string strCareerKarma = intCareerKarma.ToString(objCulture);
            string strSpentKarma = Math.Max(0, intCareerKarma - intKarma).ToString(objCulture);
            string strNuyen = decNuyen.ToString(strNuyenFormat, objCulture) + strNuyenSymbol;
            string strCareerNuyen = decCareerNuyen.ToString(strNuyenFormat, objCulture) + strNuyenSymbol;
            string strSpentNuyen = Math.Max(0, decCareerNuyen - decNuyen).ToString(strNuyenFormat, objCulture) + strNuyenSymbol;

            List<CalendarWeekRowViewModel> lstWeeks = objCharacter.Calendar.ToList().Select(x => new CalendarWeekRowViewModel(this, x)).ToList();
            string strCalendar = lstWeeks.Count == 0
                ? "No weeks yet"
                : lstWeeks.Count.ToString(objCulture) + (lstWeeks.Count == 1 ? " week" : " weeks") + "  //  "
                  + lstWeeks[lstWeeks.Count - 1].Name + " → " + lstWeeks[0].Name;

            OnUi(() =>
            {
                IsCareer = blnCareer;
                KarmaText = strKarma;
                CareerKarmaText = strCareerKarma;
                SpentKarmaText = strSpentKarma;
                NuyenText = strNuyen;
                CareerNuyenText = strCareerNuyen;
                SpentNuyenText = strSpentNuyen;
                _lstAllKarma = lstKarma;
                _lstAllNuyen = lstNuyen;
                ApplyRows(KarmaRows, _lstAllKarma, ShowFreeKarma, nameof(HasKarmaRows));
                ApplyRows(NuyenRows, _lstAllNuyen, ShowFreeNuyen, nameof(HasNuyenRows));
                KarmaChart = lstKarmaChart;
                NuyenChart = lstNuyenChart;
                Weeks.Clear();
                foreach (CalendarWeekRowViewModel objWeek in lstWeeks)
                    Weeks.Add(objWeek);
                CalendarSummary = strCalendar;
                OnPropertyChanged(nameof(HasWeeks));
                OnPropertyChanged(nameof(HasNoWeeks));
                OnPropertyChanged(nameof(AddWeekText));
            });
        }

        private void ApplyRows(ObservableCollection<ExpenseRowViewModel> lstTarget, List<ExpenseRowViewModel> lstAll, bool blnShowFree, string strHasProperty)
        {
            lstTarget.Clear();
            foreach (ExpenseRowViewModel objRow in lstAll)
            {
                if (blnShowFree || !objRow.IsFree)
                    lstTarget.Add(objRow);
            }
            OnPropertyChanged(strHasProperty);
            string strCount = lstTarget.Count.ToString(GlobalSettings.CultureInfo) + (lstTarget.Count == 1 ? " ENTRY" : " ENTRIES");
            if (ReferenceEquals(lstTarget, KarmaRows))
                KarmaCountText = strCount;
            else
                NuyenCountText = strCount;
        }

        /// <summary>Legacy RepopulateKarma/NuyenExpenseList: oldest first for the running total; shown newest first.</summary>
        private (List<ExpenseRowViewModel>, List<LedgerPoint>) BuildRows(List<ExpenseLogEntry> lstEntries, ExpenseType eType,
                                                                        Func<decimal, string> funcFormat, string strDateFormat)
        {
            List<ExpenseRowViewModel> lstRows = new List<ExpenseRowViewModel>();
            List<LedgerPoint> lstChart = new List<LedgerPoint>();
            decimal decTotal = 0;
            DateTime datLast = DateTime.MinValue;
            foreach (ExpenseLogEntry objEntry in lstEntries.Where(x => x.Type == eType).OrderBy(x => x.Date))
            {
                decTotal += objEntry.Amount;
                lstRows.Add(new ExpenseRowViewModel(this, objEntry, decTotal, funcFormat, strDateFormat));
                if (objEntry.Amount == 0)
                    continue;
                if (objEntry.Date > datLast)
                    datLast = objEntry.Date;
                lstChart.Add(new LedgerPoint(objEntry.Date, decimal.ToDouble(decTotal)));
            }
            if (lstChart.Count < 2)
            {
                if (lstChart.Count < 1)
                    lstChart.Add(new LedgerPoint(datLast == DateTime.MinValue ? DateTime.Now.AddDays(-1) : datLast, decimal.ToDouble(decTotal)));
                lstChart.Add(new LedgerPoint(DateTime.Now, decimal.ToDouble(decTotal)));
            }
            lstRows.Reverse();
            return (lstRows, lstChart);
        }

        private static string DateFormat()
        {
            string strSpace = LanguageManager.GetString("String_Space");
            return GlobalSettings.CustomDateTimeFormats
                ? GlobalSettings.CustomDateFormat + strSpace + GlobalSettings.CustomTimeFormat
                : GlobalSettings.CultureInfo.DateTimeFormat.ShortDatePattern + strSpace + GlobalSettings.CultureInfo.DateTimeFormat.ShortTimePattern;
        }

        // ---------- manual entries ----------

        [RelayCommand]
        private Task GainKarma() => NewExpenseAsync(ExpenseType.Karma, true);

        [RelayCommand]
        private Task SpendKarma() => NewExpenseAsync(ExpenseType.Karma, false);

        [RelayCommand]
        private Task GainNuyen() => NewExpenseAsync(ExpenseType.Nuyen, true);

        [RelayCommand]
        private Task SpendNuyen() => NewExpenseAsync(ExpenseType.Nuyen, false);

        private async Task NewExpenseAsync(ExpenseType eType, bool blnGain)
        {
            if (Dialogs == null)
                return;
            CharacterSettings objSettings = Character.Settings;
            ExpenseDialogOptions objOptions = new ExpenseDialogOptions
            {
                Type = eType,
                IsGain = blnGain,
                Title = ExpenseTitle(eType, blnGain),
                Reason = LanguageManager.GetString("String_ExpenseDefault"),
                ExchangeLabel = LanguageManager.GetString(ExpenseRules.ExchangeLabelKey(eType, blnGain)),
                NuyenPerKarma = objSettings.NuyenPerBPWftP,
                NuyenFormat = objSettings.NuyenFormat,
                Validate = objInput => eType == ExpenseType.Nuyen && objInput.KarmaNuyenExchange
                    ? Task.Run(() => ExpenseRules.ValidateNuyenExchangeAsync(Character, objInput.Amount))
                    : Task.FromResult(true)
            };
            ExpenseInput objInput = await Dialogs.AskExpenseAsync(objOptions).ConfigureAwait(true);
            if (objInput == null)
                return;
            await RunAsync(t => eType == ExpenseType.Karma
                               ? blnGain ? ExpenseRules.GainKarmaAsync(Character, objInput, t) : ExpenseRules.SpendKarmaAsync(Character, objInput, t)
                               : blnGain ? ExpenseRules.GainNuyenAsync(Character, objInput, t) : ExpenseRules.SpendNuyenAsync(Character, objInput, t)).ConfigureAwait(true);
        }

        private static string ExpenseTitle(ExpenseType eType, bool blnGain)
            => Tr.Get(eType == ExpenseType.Karma
                          ? blnGain ? "Button_KarmaGained" : "Button_KarmaSpent"
                          : blnGain ? "Button_NuyenGained" : "Button_NuyenSpent");

        /// <summary>Scripted entry point (screenshot harness, tests): same as the buttons without the dialog.</summary>
        public Task<RuleResult> AddExpenseAsync(ExpenseType eType, bool blnGain, ExpenseInput objInput)
            => RunAsync(t => eType == ExpenseType.Karma
                            ? blnGain ? ExpenseRules.GainKarmaAsync(Character, objInput, t) : ExpenseRules.SpendKarmaAsync(Character, objInput, t)
                            : blnGain ? ExpenseRules.GainNuyenAsync(Character, objInput, t) : ExpenseRules.SpendNuyenAsync(Character, objInput, t));

        // ---------- per-entry actions ----------

        internal async Task EditAsync(ExpenseRowViewModel objRow)
        {
            if (Dialogs == null || objRow == null)
                return;
            ExpenseLogEntry objEntry = objRow.Entry;
            ExpenseDialogOptions objOptions = new ExpenseDialogOptions
            {
                Type = objEntry.Type,
                IsGain = objEntry.Amount >= 0,
                IsEdit = true,
                Title = Tr.Get("Button_EditExpense"),
                Amount = objEntry.Amount,
                Reason = objEntry.Reason,
                Date = objEntry.Date,
                Refund = objEntry.Refund,
                ForceCareerVisible = objEntry.ForceCareerVisible,
                AllowAmountEdit = ExpenseRules.CanEditAmount(objEntry),
                NuyenFormat = Character.Settings.NuyenFormat
            };
            ExpenseInput objInput = await Dialogs.AskExpenseAsync(objOptions).ConfigureAwait(true);
            if (objInput == null)
                return;
            await RunAsync(t => ExpenseRules.EditExpenseAsync(Character, objEntry, objInput.Amount, objInput.Reason, objInput.Date, t)).ConfigureAwait(true);
        }

        internal Task UndoAsync(ExpenseRowViewModel objRow)
            => objRow == null ? Task.CompletedTask : RunAsync(t => ExpenseRules.UndoExpenseAsync(Character, objRow.Entry, t));

        // ---------- calendar ----------

        [RelayCommand]
        private async Task AddWeek()
        {
            int? intYear = null;
            int? intWeek = null;
            if (Weeks.Count == 0)
            {
                if (Dialogs == null)
                    return;
                (int Year, int Week)? objStart = await Dialogs.AskCalendarStartAsync(Tr.Get("Title_CalendarStart"), CalendarRules.DefaultStartYear, 1).ConfigureAwait(true);
                if (objStart == null)
                    return;
                intYear = objStart.Value.Year;
                intWeek = objStart.Value.Week;
            }
            await RunCalendarAsync(async t => await CalendarRules.AddWeekAsync(Character, intYear, intWeek, t).ConfigureAwait(false) != null
                                                  ? RuleResult.Changed
                                                  : RuleResult.Cancelled).ConfigureAwait(true);
        }

        [RelayCommand]
        private async Task ChangeStart()
        {
            if (Dialogs == null || Weeks.Count == 0)
                return;
            CalendarWeek objStart = Weeks[Weeks.Count - 1].Week;
            (int Year, int Week)? objNew = await Dialogs.AskCalendarStartAsync(Tr.Get("Button_ChangeStartWeek"), objStart.Year, objStart.Week).ConfigureAwait(true);
            if (objNew == null)
                return;
            await RunCalendarAsync(t => CalendarRules.ChangeStartAsync(Character, objNew.Value.Year, objNew.Value.Week, t)).ConfigureAwait(true);
        }

        internal async Task EditWeekAsync(CalendarWeekRowViewModel objRow)
        {
            if (Dialogs == null || objRow == null)
                return;
            (string Notes, System.Drawing.Color Color)? objResult = await Dialogs.AskWeekNotesAsync(objRow.Name, objRow.Week.Notes, objRow.Week.NotesColor).ConfigureAwait(true);
            if (objResult == null)
                return;
            await RunCalendarAsync(t => CalendarRules.SetNotesAsync(Character, objRow.Week, objResult.Value.Notes, objResult.Value.Color, t)).ConfigureAwait(true);
        }

        internal Task DeleteWeekAsync(CalendarWeekRowViewModel objRow)
            => objRow == null ? Task.CompletedTask : RunCalendarAsync(t => CalendarRules.DeleteWeekAsync(Character, objRow.Week, t));

        /// <summary>Scripted calendar entry point for the screenshot harness.</summary>
        public Task<RuleResult> AddWeekScriptedAsync(int intYear, int intWeek, string strNotes)
            => RunCalendarAsync(async t =>
            {
                CalendarWeek objWeek = await CalendarRules.AddWeekAsync(Character, intYear, intWeek, t).ConfigureAwait(false);
                if (objWeek != null && !string.IsNullOrEmpty(strNotes))
                    await objWeek.SetNotesAsync(strNotes, t).ConfigureAwait(false);
                return objWeek != null ? RuleResult.Changed : RuleResult.Cancelled;
            });

        private async Task<RuleResult> RunCalendarAsync(Func<CancellationToken, Task<RuleResult>> funcRule)
        {
            RuleResult eResult = await RunAsync(funcRule).ConfigureAwait(true);
            // Calendar weeks do not raise Character.PropertyChanged.
            if (eResult == RuleResult.Changed)
                _objOwner.MarkDirty();
            return eResult;
        }

        private async Task<RuleResult> RunAsync(Func<CancellationToken, Task<RuleResult>> funcRule)
        {
            RuleResult eResult;
            try
            {
                eResult = await Task.Run(() => funcRule(CancellationToken.None)).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return RuleResult.Cancelled;
            }
            if (eResult == RuleResult.Changed)
                _objOwner.Reload();
            return eResult;
        }
    }

    /// <summary>A point on the karma/nuyen-over-time chart.</summary>
    public readonly struct LedgerPoint
    {
        public LedgerPoint(DateTime datWhen, double dblValue)
        {
            When = datWhen;
            Value = dblValue;
        }

        public DateTime When { get; }
        public double Value { get; }
    }

    /// <summary>One line of the karma or nuyen log.</summary>
    public sealed class ExpenseRowViewModel
    {
        private readonly LedgerViewModel _objOwner;

        public ExpenseRowViewModel(LedgerViewModel objOwner, ExpenseLogEntry objEntry, decimal decBalance, Func<decimal, string> funcFormat, string strDateFormat)
        {
            _objOwner = objOwner;
            Entry = objEntry;
            decimal decAmount = objEntry.Amount;
            IsGain = decAmount > 0;
            IsLoss = decAmount < 0;
            IsFree = decAmount == 0;
            AmountText = (IsGain ? "+" : IsLoss ? "−" : string.Empty) + funcFormat(Math.Abs(decAmount));
            BalanceText = funcFormat(decBalance);
            DateText = objEntry.Date.ToString(strDateFormat, GlobalSettings.CultureInfo);
            Reason = objEntry.DisplayReason(GlobalSettings.Language);
            IsRefund = objEntry.Refund;
            CanUndo = objEntry.Undo != null;
            Kind = KindOf(objEntry);
            IsManual = ExpenseRules.CanEditAmount(objEntry);
            UndoTip = CanUndo
                ? "Undo: " + (IsGain ? "take back " : "refund ") + funcFormat(Math.Abs(decAmount))
                  + (IsManual || string.IsNullOrEmpty(objEntry.Undo?.ObjectId) ? string.Empty : " and reverse the " + Kind.ToLowerInvariant())
                : "Nothing to undo (no undo history for this entry)";
            EditTip = IsManual ? "Edit amount, reason and date" : "Edit reason and date";
            EditCommand = new AsyncRelayCommand(() => _objOwner.EditAsync(this));
            UndoCommand = new AsyncRelayCommand(() => _objOwner.UndoAsync(this), () => CanUndo);
        }

        public ExpenseLogEntry Entry { get; }
        public string DateText { get; }
        public string Reason { get; }
        public string AmountText { get; }
        public string BalanceText { get; }
        public string Kind { get; }
        public bool HasKind => Kind.Length > 0;
        public bool IsGain { get; }
        public bool IsLoss { get; }
        public bool IsFree { get; }
        public bool IsRefund { get; }
        public bool IsManual { get; }
        public bool CanUndo { get; }
        public string UndoTip { get; }
        public string EditTip { get; }
        public IAsyncRelayCommand EditCommand { get; }
        public IAsyncRelayCommand UndoCommand { get; }

        /// <summary>Short label of what the entry was for, from its undo record (ImproveAttribute → "IMPROVE ATTRIBUTE").</summary>
        private static string KindOf(ExpenseLogEntry objEntry)
        {
            if (objEntry.Undo == null)
                return string.Empty;
            string strName = objEntry.Type == ExpenseType.Karma ? objEntry.Undo.KarmaType.ToString() : objEntry.Undo.NuyenType.ToString();
            if (strName == "ManualAdd" || strName == "ManualSubtract")
                return "MANUAL";
            StringBuilder sbdReturn = new StringBuilder(strName.Length + 4);
            for (int i = 0; i < strName.Length; ++i)
            {
                char chr = strName[i];
                if (i > 0 && char.IsUpper(chr) && !char.IsUpper(strName[i - 1]))
                    sbdReturn.Append(' ');
                sbdReturn.Append(char.ToUpperInvariant(chr));
            }
            return sbdReturn.ToString().Replace("A I ", "AI ");
        }
    }

    /// <summary>One week of the career calendar.</summary>
    public sealed class CalendarWeekRowViewModel
    {
        private readonly LedgerViewModel _objOwner;

        public CalendarWeekRowViewModel(LedgerViewModel objOwner, CalendarWeek objWeek)
        {
            _objOwner = objOwner;
            Week = objWeek;
            Name = objWeek.CurrentDisplayName;
            DateTime? datStart = CalendarRules.WeekStart(objWeek.Year, objWeek.Week);
            DateSpan = datStart == null
                ? string.Empty
                : datStart.Value.ToString("ddd MMM d", GlobalSettings.CultureInfo) + "  –  " + datStart.Value.AddDays(6).ToString("ddd MMM d, yyyy", GlobalSettings.CultureInfo);
            DateSpanLong = CalendarRules.DateSpan(objWeek.Year, objWeek.Week);
            Notes = (objWeek.Notes ?? string.Empty).Trim();
            System.Drawing.Color objColor = objWeek.NotesColor;
            NotesBrush = objColor.A > 0
                ? new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.FromArgb(255, objColor.R, objColor.G, objColor.B))
                : new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.FromArgb(255, 0xD8, 0xE4, 0xF5));
            EditCommand = new AsyncRelayCommand(() => _objOwner.EditWeekAsync(this));
            DeleteCommand = new AsyncRelayCommand(() => _objOwner.DeleteWeekAsync(this));
        }

        public CalendarWeek Week { get; }
        public string Name { get; }
        public string DateSpan { get; }
        public string DateSpanLong { get; }
        public string Notes { get; }
        public bool HasNotes => Notes.Length > 0;
        public bool HasNoNotes => Notes.Length == 0;
        public Avalonia.Media.IBrush NotesBrush { get; }
        public IAsyncRelayCommand EditCommand { get; }
        public IAsyncRelayCommand DeleteCommand { get; }
    }
}
