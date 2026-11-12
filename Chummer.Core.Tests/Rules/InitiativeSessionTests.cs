using System;
using System.Linq;
using Chummer.Rules;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace Chummer.Tests.Rules
{
    [TestClass]
    public class InitiativeSessionTests
    {
        [TestMethod]
        public void HigherScoresActFirstTiesStayStableAndPassesSubtractTen()
        {
            var session = new InitiativeSession();
            var slow = session.Add("Slow", 4, 1, 8);
            var fast = session.Add("Fast", 10, 3, 25);
            var tie = session.Add("Tie", 12, 2, 25);
            Assert.AreSame(fast, session.Current);
            CollectionAssert.AreEqual(new[] { fast, tie, slow }, session.Entries.ToArray());
            session.Next(); Assert.AreSame(tie, session.Current);
            session.Next(); Assert.AreSame(slow, session.Current);
            session.Next(); Assert.AreEqual(2, session.Pass); Assert.AreEqual(15, fast.Score); Assert.AreEqual(0, slow.Score);
            Assert.AreSame(fast, session.Current);
            session.Next(); session.Next(); Assert.AreEqual(3, session.Pass); Assert.AreEqual(5, fast.Score);
            session.Next(); session.Next(); Assert.IsTrue(session.Finished); Assert.IsNull(session.Current);
            int pass = session.Pass; session.Next(); Assert.AreEqual(pass, session.Pass);
        }
        [TestMethod]
        public void DelayedActionsHoldThePassUntilResumedOrExplicitlyAdvanced()
        {
            var session = new InitiativeSession(); var fast = session.Add("Fast", 10, 2, 20); var slow = session.Add("Slow", 5, 1, 9);
            session.Delay(fast); Assert.AreSame(slow, session.Current);
            session.Next(); Assert.IsNull(session.Current); Assert.AreEqual(1, session.Pass);
            session.Resume(fast); Assert.AreSame(fast, session.Current);
            session.Next(); Assert.AreEqual(2, session.Pass); Assert.AreEqual(10, fast.Score);
            session.Delay(fast); session.NextPass(); Assert.IsTrue(session.Finished); Assert.IsFalse(fast.Delayed);
        }
        [TestMethod]
        public void InterruptsExhaustionRemovalAndManualScoreKeepAValidCurrentActor()
        {
            var session = new InitiativeSession(); var first = session.Add("First", 5, 1, 12); var second = session.Add("Second", 4, 1, 8);
            session.Adjust(first, -15); Assert.AreEqual(-3, first.Score); Assert.AreSame(second, session.Current);
            session.SetScore(second, 0); Assert.IsNull(session.Current);
            session.SetScore(first, 15); Assert.AreSame(first, session.Current);
            session.Remove(first); Assert.IsNull(session.Current);
            Assert.ThrowsExactly<ArgumentException>(() => session.Adjust(first, 1));
        }
        [TestMethod]
        public void NewTurnUsesBasePlusDiceAndManualTotalsWithoutChangingCharacterData()
        {
            var session = new InitiativeSession(); var auto = session.Add("Auto", 7, 2, 12); var manual = session.Add("Manual", 4, 1, 7, false);
            session.Delay(auto);
            session.NewTurn(() => 6, entry => 3);
            Assert.AreEqual(19, auto.Score); Assert.AreEqual(7, manual.Score); Assert.AreEqual(2, session.CombatTurn); Assert.AreEqual(1, session.Pass);
            Assert.IsFalse(auto.Delayed); Assert.IsFalse(auto.Acted); Assert.AreSame(auto, session.Current);
        }
        [TestMethod]
        public void InvalidResetDoesNotPartiallyReplaceAnyScoresOrAdvanceCombat()
        {
            var session = new InitiativeSession(); var auto = session.Add("Auto", 7, 2, 12); var manual = session.Add("Manual", 4, 1, 7, false);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => session.NewTurn(() => 6, entry => 99));
            Assert.AreEqual(12, auto.Score); Assert.AreEqual(7, manual.Score); Assert.AreEqual(1, session.CombatTurn);
            Assert.ThrowsExactly<ArgumentException>(() => session.Add(" ", 0, 1, 1));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => session.Add("Invalid", 0, -1, 1));
        }
    }
}
