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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Backend.Attributes;
using Chummer.Rules;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Rules
{
    /// <summary>Career bookkeeping (legacy CharacterCareer karma/nuyen buttons, undo, calendar) on a career test character.</summary>
    [TestClass]
    [DoNotParallelize]
    public class ExpenseRulesTests
    {
        /// <summary>Answers every prompt with Yes/OK, like a player confirming.</summary>
        private sealed class YesMessageService : IMessageService
        {
            public int Count { get; private set; }

            public MessageResult Show(object owner, string message, string caption = null, MessageButtons buttons = MessageButtons.OK,
                MessageIcon icon = MessageIcon.None, MessageDefaultButton defaultButton = MessageDefaultButton.Button1)
            {
                ++Count;
                return buttons == MessageButtons.YesNo || buttons == MessageButtons.YesNoCancel ? MessageResult.Yes : MessageResult.OK;
            }

            public Task<MessageResult> ShowAsync(object owner, string message, string caption = null, MessageButtons buttons = MessageButtons.OK,
                MessageIcon icon = MessageIcon.None, MessageDefaultButton defaultButton = MessageDefaultButton.Button1, CancellationToken token = default)
                => Task.FromResult(Show(owner, message, caption, buttons, icon, defaultButton));
        }

        private IMessageService _objOldMessages;
        private YesMessageService _objMessages;
        private Character _objCharacter;

        [TestInitialize]
        public void Setup()
        {
            _objOldMessages = HostServices.Messages;
            _objMessages = new YesMessageService();
            HostServices.Messages = _objMessages;
            _objCharacter = UnitTestUtils.LoadCharacter(new FileInfo(Path.Combine(AppContext.BaseDirectory, "TestFiles", "Soma (Career).chum5")));
            Assert.IsTrue(_objCharacter.Created, "test character must be in career mode");
        }

        [TestCleanup]
        public void TearDown()
        {
            _objCharacter?.Dispose();
            HostServices.Messages = _objOldMessages;
        }

        [TestMethod]
        public async Task GainKarmaAddsManualEntryAndUndoRemovesIt()
        {
            int intKarma = _objCharacter.Karma;
            int intCareer = _objCharacter.CareerKarma;
            int intEntries = _objCharacter.ExpenseEntries.Count;
            DateTime datWhen = new DateTime(2081, 5, 4, 20, 30, 0);

            RuleResult eResult = await ExpenseRules.GainKarmaAsync(_objCharacter, new ExpenseInput { Amount = 5, Reason = "Session reward", Date = datWhen });

            Assert.AreEqual(RuleResult.Changed, eResult);
            Assert.AreEqual(intKarma + 5, _objCharacter.Karma);
            Assert.AreEqual(intCareer + 5, _objCharacter.CareerKarma);
            Assert.AreEqual(intEntries + 1, _objCharacter.ExpenseEntries.Count);
            ExpenseLogEntry objEntry = _objCharacter.ExpenseEntries.Single(x => x.Reason == "Session reward");
            Assert.AreEqual(ExpenseType.Karma, objEntry.Type);
            Assert.AreEqual(5m, objEntry.Amount);
            Assert.AreEqual(datWhen, objEntry.Date);
            Assert.IsFalse(objEntry.Refund);
            Assert.AreEqual(KarmaExpenseType.ManualAdd, objEntry.Undo.KarmaType);
            Assert.IsTrue(ExpenseRules.CanEditAmount(objEntry));

            // Editing a manual entry moves the karma by the difference.
            await ExpenseRules.EditExpenseAsync(_objCharacter, objEntry, 8, "Session reward (edited)", datWhen);
            Assert.AreEqual(intKarma + 8, _objCharacter.Karma);
            Assert.AreEqual("Session reward (edited)", objEntry.Reason);

            Assert.AreEqual(RuleResult.Changed, await ExpenseRules.UndoExpenseAsync(_objCharacter, objEntry));
            Assert.AreEqual(intKarma, _objCharacter.Karma);
            Assert.AreEqual(intEntries, _objCharacter.ExpenseEntries.Count);
        }

        [TestMethod]
        public async Task RefundDoesNotCountTowardsCareerKarma()
        {
            int intCareer = _objCharacter.CareerKarma;
            int intKarma = _objCharacter.Karma;
            await ExpenseRules.GainKarmaAsync(_objCharacter, new ExpenseInput { Amount = 3, Reason = "Refund", Refund = true });
            Assert.AreEqual(intKarma + 3, _objCharacter.Karma);
            Assert.AreEqual(intCareer, _objCharacter.CareerKarma);
        }

        [TestMethod]
        public async Task GainAndSpendNuyen()
        {
            decimal decNuyen = _objCharacter.Nuyen;
            int intEntries = _objCharacter.ExpenseEntries.Count;

            Assert.AreEqual(RuleResult.Changed, await ExpenseRules.GainNuyenAsync(_objCharacter, new ExpenseInput { Amount = 2500, Reason = "Run payout" }));
            Assert.AreEqual(decNuyen + 2500, _objCharacter.Nuyen);
            ExpenseLogEntry objEntry = _objCharacter.ExpenseEntries.Single(x => x.Reason == "Run payout");
            Assert.AreEqual(ExpenseType.Nuyen, objEntry.Type);
            Assert.AreEqual(2500m, objEntry.Amount);
            Assert.AreEqual(NuyenExpenseType.ManualAdd, objEntry.Undo.NuyenType);

            Assert.AreEqual(RuleResult.Changed, await ExpenseRules.SpendNuyenAsync(_objCharacter, new ExpenseInput { Amount = 500, Reason = "Bribe" }));
            Assert.AreEqual(decNuyen + 2000, _objCharacter.Nuyen);
            ExpenseLogEntry objSpent = _objCharacter.ExpenseEntries.Single(x => x.Reason == "Bribe");
            Assert.AreEqual(-500m, objSpent.Amount);
            Assert.AreEqual(NuyenExpenseType.ManualSubtract, objSpent.Undo.NuyenType);
            Assert.AreEqual(intEntries + 2, _objCharacter.ExpenseEntries.Count);

            // Spending more than the character has is refused (legacy Message_NotEnoughNuyen).
            int intMessages = _objMessages.Count;
            Assert.AreEqual(RuleResult.NotAllowed, await ExpenseRules.SpendNuyenAsync(_objCharacter, new ExpenseInput { Amount = _objCharacter.Nuyen + 1, Reason = "Too much" }));
            Assert.AreEqual(intMessages + 1, _objMessages.Count);
            Assert.AreEqual(decNuyen + 2000, _objCharacter.Nuyen);

            await ExpenseRules.UndoExpenseAsync(_objCharacter, objSpent);
            Assert.AreEqual(decNuyen + 2500, _objCharacter.Nuyen);
        }

        [TestMethod]
        public async Task KarmaForNuyenExchange()
        {
            decimal decRate = _objCharacter.Settings.NuyenPerBPWftM;
            decimal decNuyen = _objCharacter.Nuyen;
            int intKarma = _objCharacter.Karma;
            Assume(intKarma >= 2);
            await ExpenseRules.SpendKarmaAsync(_objCharacter, new ExpenseInput { Amount = 2, Reason = "Working for the Man", KarmaNuyenExchange = true });
            Assert.AreEqual(intKarma - 2, _objCharacter.Karma);
            Assert.AreEqual(decNuyen + 2 * decRate, _objCharacter.Nuyen);
        }

        [TestMethod]
        public async Task UndoAttributeImprovementRestoresValueAndKarma()
        {
            // Make sure there is enough karma to raise something.
            await ExpenseRules.GainKarmaAsync(_objCharacter, new ExpenseInput { Amount = 100, Reason = "Test karma" });
            CharacterAttrib objAttribute = _objCharacter.AttributeSection.AttributeList.FirstOrDefault(x => x.CanUpgradeCareer);
            Assert.IsNotNull(objAttribute, "no attribute can be raised");
            int intValue = objAttribute.Value;
            int intKarma = _objCharacter.Karma;
            int intEntries = _objCharacter.ExpenseEntries.Count;
            int intCost = objAttribute.UpgradeKarmaCost;

            Assert.AreEqual(RuleResult.Changed, await AttributeRules.ImproveCareerAsync(_objCharacter, objAttribute));
            Assert.AreEqual(intValue + 1, objAttribute.Value);
            Assert.AreEqual(intKarma - intCost, _objCharacter.Karma);
            ExpenseLogEntry objEntry = _objCharacter.ExpenseEntries
                                                    .Where(x => x.Type == ExpenseType.Karma && x.Undo?.KarmaType == KarmaExpenseType.ImproveAttribute
                                                                && x.Undo.ObjectId == objAttribute.Abbrev)
                                                    .OrderByDescending(x => x.Date).First();
            Assert.AreEqual(-intCost, (int)objEntry.Amount);
            Assert.IsFalse(ExpenseRules.CanEditAmount(objEntry));

            Assert.AreEqual(RuleResult.Changed, await ExpenseRules.UndoExpenseAsync(_objCharacter, objEntry));
            Assert.AreEqual(intValue, objAttribute.Value);
            Assert.AreEqual(intKarma, _objCharacter.Karma);
            Assert.AreEqual(intEntries, _objCharacter.ExpenseEntries.Count);
            Assert.IsFalse(_objCharacter.ExpenseEntries.Contains(objEntry));
        }

        [TestMethod]
        public async Task CalendarAddAndShiftWeeks()
        {
            int intExisting = _objCharacter.Calendar.Count;
            if (intExisting == 0)
                Assert.IsNotNull(await CalendarRules.AddWeekAsync(_objCharacter, 2081, 52));
            CalendarWeek objNewest = _objCharacter.Calendar.First();
            int intYear = objNewest.Year;
            int intWeek = objNewest.Week;
            CalendarWeek objAdded = await CalendarRules.AddWeekAsync(_objCharacter);
            Assert.AreSame(objAdded, _objCharacter.Calendar.First(), "newest week first");
            Assert.AreEqual(new CalendarWeek(intYear, intWeek + 1).Week, objAdded.Week);

            // Move the first week one week later: everything shifts by a week.
            CalendarWeek objFirst = _objCharacter.Calendar.Last();
            DateTime datBefore = CalendarRules.WeekStart(objFirst.Year, objFirst.Week).Value;
            DateTime datNewest = CalendarRules.WeekStart(objAdded.Year, objAdded.Week).Value;
            DateTime datTarget = datBefore.AddDays(7);
            CalendarWeek objTarget = new CalendarWeek(objFirst.Year, objFirst.Week + 1);
            Assert.AreEqual(RuleResult.Changed, await CalendarRules.ChangeStartAsync(_objCharacter, objTarget.Year, objTarget.Week));
            Assert.AreEqual(datTarget, CalendarRules.WeekStart(objFirst.Year, objFirst.Week).Value);
            Assert.AreEqual(datNewest.AddDays(7), CalendarRules.WeekStart(objAdded.Year, objAdded.Week).Value);
        }

        private static void Assume(bool blnCondition)
        {
            if (!blnCondition)
                Assert.Inconclusive("test character does not meet the precondition");
        }
    }
}
