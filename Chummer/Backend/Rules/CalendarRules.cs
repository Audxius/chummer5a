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
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace Chummer.Rules
{
    /// <summary>
    /// The career calendar (game weeks with notes), lifted from the legacy CharacterCareer form: cmdAddWeek_Click,
    /// cmdDeleteWeek_Click, cmdEditWeek_Click and cmdChangeStartWeek_Click, plus SelectCalendarStart's date span.
    /// The calendar list is kept newest week first, as in legacy.
    /// </summary>
    public static class CalendarRules
    {
        /// <summary>Default year SelectCalendarStart offers for a new calendar.</summary>
        public static int DefaultStartYear => DateTime.UtcNow.Year + 62;

        public static int WeeksInYear(int intYear) => intYear.IsYearLongYear() ? 53 : 52;

        /// <summary>Monday of an ISO 8601 week (week 1 is the week containing January 4th), or null outside DateTime's range.</summary>
        public static DateTime? WeekStart(int intYear, int intWeek)
        {
            if (intYear < 2 || intYear > 9998 || intWeek < 1)
                return null;
            DateTime datJan4 = new DateTime(intYear, 1, 4);
            int intDaysSinceMonday = ((int)datJan4.DayOfWeek + 6) % 7;
            return datJan4.AddDays(-intDaysSinceMonday + (intWeek - 1) * 7);
        }

        /// <summary>SelectCalendarStart.UpdateDateSpan: "Monday - Sunday" of the week.</summary>
        public static string DateSpan(int intYear, int intWeek)
        {
            DateTime? datStart = WeekStart(intYear, intWeek);
            if (datStart == null)
                return string.Empty;
            return StringExtensions.FastFormat(LanguageManager.GetString("String_DateSpan"),
                datStart.Value.ToString("D", GlobalSettings.CultureInfo),
                datStart.Value.AddDays(6).ToString("D", GlobalSettings.CultureInfo));
        }

        /// <summary>
        /// Legacy cmdAddWeek_Click: the week after the newest one, or (for an empty calendar) the week the player picked
        /// as the start (<paramref name="intStartYear"/>/<paramref name="intStartWeek"/>; required then).
        /// </summary>
        public static async Task<CalendarWeek> AddWeekAsync(Character objCharacter, int? intStartYear = null, int? intStartWeek = null, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            ThreadSafeBindingList<CalendarWeek> lstWeeks = await objCharacter.GetCalendarAsync(token).ConfigureAwait(false);
            CalendarWeek objLastWeek = await lstWeeks.FirstOrDefaultAsync(token).ConfigureAwait(false);
            CalendarWeek objWeek;
            if (objLastWeek != null)
                objWeek = new CalendarWeek(await objLastWeek.GetYearAsync(token).ConfigureAwait(false), await objLastWeek.GetWeekAsync(token).ConfigureAwait(false) + 1);
            else if (intStartYear != null && intStartWeek != null)
                objWeek = new CalendarWeek(intStartYear.Value, intStartWeek.Value);
            else
                return null;
            try
            {
                await lstWeeks.AddWithSortAsync(objWeek, (x, y) => y.CompareTo(x), token: token).ConfigureAwait(false);
            }
            catch
            {
                await objWeek.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            return objWeek;
        }

        /// <summary>Legacy cmdDeleteWeek_Click (with its delete confirmation).</summary>
        public static async Task<RuleResult> DeleteWeekAsync(Character objCharacter, CalendarWeek objWeek, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objWeek == null)
                return RuleResult.NotAllowed;
            if (!await CommonFunctions.ConfirmDeleteAsync(
                    await LanguageManager.GetStringAsync("Message_DeleteCalendarWeek", token: token).ConfigureAwait(false), token).ConfigureAwait(false))
                return RuleResult.Cancelled;
            await (await objCharacter.GetCalendarAsync(token).ConfigureAwait(false)).RemoveAsync(objWeek, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        /// <summary>Legacy cmdEditWeek_Click after the notes editor closed with OK.</summary>
        public static async Task<RuleResult> SetNotesAsync(Character objCharacter, CalendarWeek objWeek, string strNotes, Color objColor, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            if (objWeek == null)
                return RuleResult.NotAllowed;
            await objWeek.SetNotesAsync(strNotes ?? string.Empty, token).ConfigureAwait(false);
            await objWeek.SetNotesColorAsync(objColor, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        /// <summary>
        /// Legacy cmdChangeStartWeek_Click: moves the oldest week to the picked year/week and shifts every other week by the same
        /// number of weeks. (Legacy's own year arithmetic and CalendarWeek.ModifyWeekAsync are broken; this shifts by real ISO
        /// weeks, which is what the button is meant to do.)
        /// </summary>
        public static async Task<RuleResult> ChangeStartAsync(Character objCharacter, int intNewYear, int intNewWeek, CancellationToken token = default)
        {
            if (objCharacter == null)
                throw new ArgumentNullException(nameof(objCharacter));
            ThreadSafeBindingList<CalendarWeek> lstWeeks = await objCharacter.GetCalendarAsync(token).ConfigureAwait(false);
            int intCount = await lstWeeks.GetCountAsync(token).ConfigureAwait(false);
            if (intCount == 0)
                return RuleResult.NotAllowed;
            CalendarWeek objStart = await lstWeeks.GetValueAtAsync(intCount - 1, token).ConfigureAwait(false);
            DateTime? datOld = WeekStart(await objStart.GetYearAsync(token).ConfigureAwait(false), await objStart.GetWeekAsync(token).ConfigureAwait(false));
            DateTime? datNew = WeekStart(intNewYear, intNewWeek);
            if (datOld == null || datNew == null)
                return RuleResult.NotAllowed;
            int intDiff = (int)Math.Round((datNew.Value - datOld.Value).TotalDays / 7);
            if (intDiff == 0)
                return RuleResult.Cancelled;
            for (int i = 0; i < intCount; ++i)
            {
                CalendarWeek objWeek = await lstWeeks.GetValueAtAsync(i, token).ConfigureAwait(false);
                // Normalise through the constructor's (correct) rollover logic, then apply year first so week 53 survives.
                using (CalendarWeek objTarget = new CalendarWeek(await objWeek.GetYearAsync(token).ConfigureAwait(false),
                                                                 await objWeek.GetWeekAsync(token).ConfigureAwait(false) + intDiff))
                {
                    await objWeek.SetYearAsync(objTarget.Year, token).ConfigureAwait(false);
                    await objWeek.SetWeekAsync(objTarget.Week, token).ConfigureAwait(false);
                }
            }
            return RuleResult.Changed;
        }
    }
}
