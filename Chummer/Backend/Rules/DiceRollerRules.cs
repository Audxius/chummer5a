/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chummer.Rules
{
    public enum DiceRollMethod { Standard, Large, ReallyLarge }

    public sealed class DiceRollOptions
    {
        public int Dice { get; set; } = 1;
        public int Threshold { get; set; }
        public int Gremlins { get; set; }
        public DiceRollMethod Method { get; set; }
        public bool RuleOfSix { get; set; }
        public bool RushedJob { get; set; }
        public bool Cinematic { get; set; }
        public bool VariableGlitch { get; set; }
        public bool BubbleDie { get; set; }
        internal int Target => Method == DiceRollMethod.ReallyLarge ? 1 : Method == DiceRollMethod.Large ? 3 : Cinematic ? 4 : 5;
        internal int GlitchFace => Method == DiceRollMethod.ReallyLarge ? 7 : RushedJob ? 2 : 1;
    }

    public sealed class RolledDie
    {
        public RolledDie(int value, bool bubble = false) { Value = value; IsBubble = bubble; }
        public int Value { get; }
        public bool IsBubble { get; }
    }

    public sealed class DiceRollResult
    {
        internal DiceRollResult(IEnumerable<RolledDie> dice, int hits, int glitches, int glitchThreshold, bool glitch, bool critical, int threshold)
        { Dice = Array.AsReadOnly(dice.ToArray()); Hits = hits; GlitchDice = glitches; GlitchThreshold = glitchThreshold; IsGlitch = glitch; IsCriticalGlitch = critical; Threshold = threshold; }
        public IReadOnlyList<RolledDie> Dice { get; }
        public int Hits { get; }
        public int Sum => Dice.Sum(x => x.Value);
        public int GlitchDice { get; }
        public int GlitchThreshold { get; }
        public int Threshold { get; }
        public bool IsGlitch { get; }
        public bool IsCriticalGlitch { get; }
        public bool Success => !IsCriticalGlitch && Hits >= Threshold;
    }

    /// <summary>Local roller extracted from Utility Forms/DiceRoller.cs, including its alternate methods and reroll semantics.</summary>
    public static class DiceRollerRules
    {
        public static DiceRollResult Roll(DiceRollOptions options, Func<int> nextDie = null)
        {
            Validate(options);
            var dice = new List<RolledDie>();
            AddDice(dice, options.Dice, options, nextDie ?? (() => Utils.GlobalRandom.NextD6ModuloBiasRemoved()));
            return Finish(dice, options, options.Dice, false, false, nextDie);
        }

        public static DiceRollResult RerollMisses(DiceRollResult previous, DiceRollOptions options, Func<int> nextDie = null)
        {
            if (previous == null) throw new ArgumentNullException(nameof(previous));
            Validate(options);
            var dice = previous.Dice.Where(x => !x.IsBubble).ToList();
            int oldHits = Hits(dice, options);
            bool oldGlitch = previous.IsGlitch || dice.Count(x => x.Value <= options.GlitchFace) >= GlitchThreshold(dice.Count, oldHits, options);
            bool oldCritical = previous.IsCriticalGlitch || oldGlitch && oldHits == 0;
            int misses = dice.Count(x => x.Value < options.Target);
            if (misses == 0) return previous;
            dice.RemoveAll(x => x.Value < options.Target);
            AddDice(dice, misses, options, nextDie ?? (() => Utils.GlobalRandom.NextD6ModuloBiasRemoved()));
            // Legacy rerolls use the resulting pool size, including exploding dice, for their glitch threshold.
            return Finish(dice, options, dice.Count, oldGlitch, oldCritical, nextDie);
        }

        private static void Validate(DiceRollOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.Dice < 0 || options.Dice > 100 || options.Gremlins < 0 || options.Gremlins > 100 || options.Threshold < 0
                || !Enum.IsDefined(typeof(DiceRollMethod), options.Method)) throw new ArgumentOutOfRangeException(nameof(options));
        }
        private static int Next(Func<int> next)
        {
            int value = next();
            if (value < 1 || value > 6) throw new InvalidOperationException("A d6 must return a value from 1 to 6.");
            return value;
        }
        private static void AddDice(List<RolledDie> dice, int count, DiceRollOptions options, Func<int> next)
        {
            for (int i = 0; i < count; i++)
            {
                int value;
                do
                {
                    if (dice.Count >= 100000) throw new InvalidOperationException("Exploding dice exceeded the roll size limit.");
                    value = Next(next); dice.Add(new RolledDie(value));
                } while (value == 6 && options.RuleOfSix && options.Method == DiceRollMethod.Standard);
            }
        }
        private static int Hits(List<RolledDie> dice, DiceRollOptions options) => options.Method == DiceRollMethod.ReallyLarge
            ? dice.Sum(x => x.Value) : dice.Count(x => x.Value >= options.Target);
        private static int GlitchThreshold(int count, int hits, DiceRollOptions options) => Math.Max(1, (options.VariableGlitch ? hits + 1 : (count + 1) / 2) - options.Gremlins);
        private static DiceRollResult Finish(List<RolledDie> dice, DiceRollOptions options, int pool, bool oldGlitch, bool oldCritical, Func<int> next)
        {
            int hits = Hits(dice, options);
            int glitches = dice.Count(x => x.Value <= options.GlitchFace);
            int threshold = GlitchThreshold(pool, hits, options);
            if (!oldGlitch && options.BubbleDie && (options.VariableGlitch || glitches == threshold - 1 && (pool & 1) == 0))
            {
                var bubble = new RolledDie(Next(next ?? (() => Utils.GlobalRandom.NextD6ModuloBiasRemoved())), true);
                dice.Add(bubble); if (bubble.Value <= options.GlitchFace) glitches++;
            }
            bool glitch = oldGlitch || glitches >= threshold;
            return new DiceRollResult(dice, hits, glitches, threshold, glitch, oldCritical || glitch && hits == 0, options.Threshold);
        }
    }
}
