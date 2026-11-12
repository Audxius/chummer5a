using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Rules;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Rules
{
    [TestClass]
    public class DiceRollerRulesTests
    {
        private static Func<int> Sequence(params int[] values) { var queue = new Queue<int>(values); return () => queue.Dequeue(); }
        [TestMethod]
        public void StandardThresholdAndGlitchesUseOriginalPool()
        {
            var result = DiceRollerRules.Roll(new DiceRollOptions { Dice = 5, Threshold = 2 }, Sequence(1, 1, 2, 5, 6));
            Assert.AreEqual(2, result.Hits); Assert.AreEqual(15, result.Sum);
            Assert.IsTrue(result.Success); Assert.IsFalse(result.IsGlitch); Assert.AreEqual(3, result.GlitchThreshold);
            var glitch = DiceRollerRules.Roll(new DiceRollOptions { Dice = 4 }, Sequence(1, 1, 5, 6));
            Assert.IsTrue(glitch.IsGlitch); Assert.IsFalse(glitch.IsCriticalGlitch);
            var critical = DiceRollerRules.Roll(new DiceRollOptions { Dice = 4 }, Sequence(1, 1, 2, 4));
            Assert.IsTrue(critical.IsCriticalGlitch);
        }
        [TestMethod]
        public void RuleOfSixAddsDiceWithoutRaisingInitialGlitchThreshold()
        {
            var result = DiceRollerRules.Roll(new DiceRollOptions { Dice = 2, RuleOfSix = true }, Sequence(6, 6, 1, 5));
            Assert.AreEqual(4, result.Dice.Count); Assert.AreEqual(3, result.Hits);
            Assert.AreEqual(1, result.GlitchThreshold); Assert.IsTrue(result.IsGlitch);
        }
        [TestMethod]
        public void BubbleDieCannotAddAHitAndCanDecideAGlitch()
        {
            var safe = DiceRollerRules.Roll(new DiceRollOptions { Dice = 4, BubbleDie = true }, Sequence(1, 2, 5, 6, 6));
            Assert.AreEqual(2, safe.Hits); Assert.IsFalse(safe.IsGlitch); Assert.IsTrue(safe.Dice.Last().IsBubble);
            var glitch = DiceRollerRules.Roll(new DiceRollOptions { Dice = 4, BubbleDie = true }, Sequence(1, 2, 5, 6, 1));
            Assert.IsTrue(glitch.IsGlitch); Assert.AreEqual(2, glitch.Hits);
        }
        [TestMethod]
        public void AlternateMethodsRushingCinematicAndGremlinsMatchLegacy()
        {
            var cinematic = DiceRollerRules.Roll(new DiceRollOptions { Dice = 2, Cinematic = true }, Sequence(4, 3));
            Assert.AreEqual(1, cinematic.Hits);
            var large = DiceRollerRules.Roll(new DiceRollOptions { Dice = 3, Method = DiceRollMethod.Large, RuleOfSix = true }, Sequence(3, 4, 6));
            Assert.AreEqual(3, large.Hits); Assert.AreEqual(3, large.Dice.Count);
            var sum = DiceRollerRules.Roll(new DiceRollOptions { Dice = 3, Method = DiceRollMethod.ReallyLarge }, Sequence(2, 3, 6));
            Assert.AreEqual(11, sum.Hits); Assert.IsTrue(sum.IsGlitch);
            var rushed = DiceRollerRules.Roll(new DiceRollOptions { Dice = 4, RushedJob = true, Gremlins = 1 }, Sequence(2, 3, 4, 5));
            Assert.AreEqual(1, rushed.GlitchThreshold); Assert.IsTrue(rushed.IsGlitch);
        }
        [TestMethod]
        public void VariableGlitchAlwaysRollsBubbleAndFloorsGremlinsThreshold()
        {
            var result = DiceRollerRules.Roll(new DiceRollOptions { Dice = 3, VariableGlitch = true, Gremlins = 10, BubbleDie = true }, Sequence(4, 5, 6, 1));
            Assert.AreEqual(2, result.Hits); Assert.AreEqual(1, result.GlitchThreshold);
            Assert.IsTrue(result.IsGlitch); Assert.IsTrue(result.Dice.Last().IsBubble);
        }
        [TestMethod]
        public void RerollKeepsHitsRemovesBubbleAndNeverErasesExistingGlitch()
        {
            var options = new DiceRollOptions { Dice = 4, BubbleDie = true };
            var initial = DiceRollerRules.Roll(options, Sequence(1, 2, 5, 6, 1));
            var result = DiceRollerRules.RerollMisses(initial, options, Sequence(5, 6));
            Assert.AreEqual(4, result.Hits); Assert.AreEqual(4, result.Dice.Count);
            Assert.IsTrue(result.IsGlitch); Assert.IsFalse(result.IsCriticalGlitch);
            Assert.AreSame(result, DiceRollerRules.RerollMisses(result, options, () => throw new Exception("No dice should roll.")));
        }
        [TestMethod]
        public void CriticalGlitchSurvivesRerollAndInvalidDiceAreRejected()
        {
            var options = new DiceRollOptions { Dice = 2 };
            var initial = DiceRollerRules.Roll(options, Sequence(1, 1));
            Assert.IsTrue(DiceRollerRules.RerollMisses(initial, options, Sequence(5, 6)).IsCriticalGlitch);
            Assert.ThrowsExactly<InvalidOperationException>(() => DiceRollerRules.Roll(options, Sequence(7)));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DiceRollerRules.Roll(new DiceRollOptions { Dice = -1 }));
        }
    }
}
