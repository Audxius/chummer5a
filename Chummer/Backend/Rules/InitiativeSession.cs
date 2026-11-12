/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chummer.Rules
{
    /// <summary>Temporary combat state, like the legacy dashboard. Never owns or disposes character models.</summary>
    public sealed class InitiativeEntry
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Name { get; internal set; }
        public int Base { get; internal set; }
        public int Dice { get; internal set; } = 1;
        public int Score { get; internal set; }
        public bool Delayed { get; internal set; }
        public bool Acted { get; internal set; }
        public bool Automatic { get; internal set; } = true;
    }

    public sealed class InitiativeSession
    {
        private readonly List<InitiativeEntry> _entries = new List<InitiativeEntry>();
        private bool _started;
        public IReadOnlyList<InitiativeEntry> Entries => _entries.AsReadOnly();
        public InitiativeEntry Current { get; private set; }
        public int CombatTurn { get; private set; } = 1;
        public int Pass { get; private set; } = 1;
        public bool Finished => !_entries.Any(x => x.Score > 0);
        public InitiativeEntry Add(string name, int basis, int dice, int score, bool automatic = true)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a combatant name.", nameof(name));
            if (dice < 0 || dice > 100) throw new ArgumentOutOfRangeException(nameof(dice));
            var entry = new InitiativeEntry { Name = name.Trim(), Base = basis, Dice = dice, Score = score, Automatic = automatic };
            _entries.Add(entry); Sort(); if (Current == null) SelectNext(); return entry;
        }
        public void Remove(InitiativeEntry entry)
        { Require(entry); _entries.Remove(entry); if (ReferenceEquals(Current, entry)) { Current = null; SelectNext(); } }
        public void Sort()
        {
            var ordered = _entries.OrderByDescending(x => x.Score).ToArray();
            _entries.Clear(); _entries.AddRange(ordered);
            if (!_started) SelectNext();
        }
        private void Require(InitiativeEntry entry)
        { if (entry == null || !_entries.Contains(entry)) throw new ArgumentException("Select a combatant in this tracker.", nameof(entry)); }
        public void Adjust(InitiativeEntry entry, int delta)
        { Require(entry); entry.Score = checked(entry.Score + delta); if (entry.Score <= 0 && ReferenceEquals(Current, entry)) { Current = null; SelectNext(); } }
        public void SetScore(InitiativeEntry entry, int score)
        { Require(entry); entry.Score = score; if (Current == null || ReferenceEquals(Current, entry) && score <= 0) SelectNext(); }
        public void Delay(InitiativeEntry entry)
        {
            Require(entry); if (entry.Score <= 0 || entry.Acted) return;
            entry.Delayed = true; if (ReferenceEquals(Current, entry)) { Current = null; SelectNext(); }
        }
        public void Resume(InitiativeEntry entry)
        {
            Require(entry); if (!entry.Delayed || entry.Score <= 0) return;
            entry.Delayed = false; Current = entry;
        }
        private void SelectNext() => Current = _entries.FirstOrDefault(x => x.Score > 0 && !x.Acted && !x.Delayed);
        public void Next()
        {
            _started = true;
            if (Current != null) { Current.Acted = true; Current.Delayed = false; }
            SelectNext();
            if (Current == null && !_entries.Any(x => x.Score > 0 && !x.Acted && x.Delayed)) NextPass();
        }
        public void NextPass()
        {
            if (Finished) { Current = null; return; }
            foreach (var entry in _entries)
            {
                if (entry.Score > 0) entry.Score = Math.Max(0, entry.Score - 10);
                entry.Acted = false; entry.Delayed = false;
            }
            _started = true; Pass++; Sort(); SelectNext();
        }
        public void NewTurn(Func<int> nextDie = null, Func<InitiativeEntry, int> manualTotal = null)
        {
            nextDie = nextDie ?? (() => Utils.GlobalRandom.NextD6ModuloBiasRemoved());
            // Calculate before changing state so cancellation/invalid random input cannot partially reset combat.
            var scores = _entries.Select(entry =>
            {
                int score = entry.Base;
                if (!entry.Automatic)
                {
                    if (manualTotal == null) throw new InvalidOperationException("Enter dice totals for manually rolled combatants.");
                    int total = manualTotal(entry);
                    if (total < entry.Dice || total > entry.Dice * 6) throw new ArgumentOutOfRangeException(nameof(manualTotal));
                    return checked(score + total);
                }
                for (int i = 0; i < entry.Dice; i++)
                { int face = nextDie(); if (face < 1 || face > 6) throw new InvalidOperationException("Invalid d6 result."); score = checked(score + face); }
                return score;
            }).ToArray();
            for (int i = 0; i < scores.Length; i++) { _entries[i].Score = scores[i]; _entries[i].Acted = false; _entries[i].Delayed = false; }
            CombatTurn++; Pass = 1; _started = false; Sort(); SelectNext();
        }
    }
}
