/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Chummer.Desktop.Views;
using Chummer.Rules;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chummer.Desktop.ViewModels
{
    public sealed class InitiativeRow
    {
        internal InitiativeEntry Entry { get; }
        public string Name { get; }
        public int Score { get; }
        public string Status { get; }
        public InitiativeRow(InitiativeEntry entry, InitiativeEntry current)
        { Entry = entry; Name = entry.Name; Score = entry.Score; Status = ReferenceEquals(entry, current) ? "Acting" : entry.Score <= 0 ? "No actions" : entry.Delayed ? "Delayed" : entry.Acted ? "Acted" : "Ready"; }
    }
    public sealed partial class InitiativeViewModel : ViewModelBase
    {
        private readonly Func<CharacterViewModel> _runner;
        public InitiativeViewModel(Func<CharacterViewModel> runner) { _runner = runner; }
        public InitiativeSession Session { get; } = new();
        public ObservableCollection<InitiativeRow> Combatants { get; } = new();
        [ObservableProperty] private InitiativeRow _selected;
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private decimal _basis;
        [ObservableProperty] private decimal _dice = 1;
        [ObservableProperty] private decimal _manualTotal = 1;
        [ObservableProperty] private bool _automatic = true;
        [ObservableProperty] private decimal _score;
        [ObservableProperty] private string _message = "";
        [ObservableProperty] private int _runnerMode;
        public string[] RunnerModes { get; } = new[] { "Physical", "Astral", "Cold-sim Matrix", "Hot-sim Matrix" };
        public string RoundLabel => $"Combat turn {Session.CombatTurn} · Pass {Session.Pass}";
        public string ActingLabel => Session.Current != null ? Session.Current.Name + " is acting" : Session.Finished ? "Combat turn finished" : "Waiting for delayed combatants";
        partial void OnSelectedChanged(InitiativeRow value) { if (value != null) Score = value.Score; }
        private void Refresh(InitiativeEntry preferred = null)
        {
            var selection = preferred ?? Selected?.Entry;
            Combatants.Clear(); foreach (var entry in Session.Entries) Combatants.Add(new InitiativeRow(entry, Session.Current));
            Selected = Combatants.FirstOrDefault(x => ReferenceEquals(x.Entry, selection)) ?? Combatants.FirstOrDefault(x => ReferenceEquals(x.Entry, Session.Current));
            OnPropertyChanged(nameof(RoundLabel)); OnPropertyChanged(nameof(ActingLabel));
        }
        private void Run(Action action) { try { action(); Message = ""; Refresh(); } catch (Exception ex) { Message = ex.Message; } }
        [RelayCommand] private void UseRunner()
        {
            var runner = _runner(); if (runner == null) { Message = "Open and select a runner first, or enter a combatant manually."; return; }
            var c = runner.Character; Name = runner.Name;
            (Basis, Dice) = RunnerMode switch { 1 => (c.AstralInitiativeValue, c.AstralInitiativeDice),
                2 => (c.MatrixInitiativeColdValue, c.MatrixInitiativeColdDice), 3 => (c.MatrixInitiativeHotValue, c.MatrixInitiativeHotDice),
                _ => (c.InitiativeValue, c.InitiativeDice) };
            ManualTotal = Dice; Message = "";
        }
        [RelayCommand] private void Add() => Run(() =>
        {
            int total = (int)ManualTotal;
            if (Automatic) { total = 0; for (int i = 0; i < (int)Dice; i++) total += Utils.GlobalRandom.NextD6ModuloBiasRemoved(); }
            else if (total < Dice || total > 6 * Dice) throw new ArgumentException("The dice total must be between the dice count and six times that count.");
            var entry = Session.Add(Name, (int)Basis, (int)Dice, checked((int)Basis + total), Automatic); Refresh(entry);
        });
        [RelayCommand] private void Remove() { if (Selected != null) Run(() => Session.Remove(Selected.Entry)); }
        [RelayCommand] private void Next() => Run(Session.Next);
        [RelayCommand] private void NextPass() => Run(Session.NextPass);
        [RelayCommand] private async Task NewTurnAsync()
        {
            var totals = new Dictionary<Guid, int>();
            foreach (var entry in Session.Entries.Where(x => !x.Automatic))
            {
                decimal? total = await PromptWindow.AskNumberAsync(null, entry.Name + " · " + entry.Dice + "d6 total", entry.Dice, entry.Dice * 6, true);
                if (!total.HasValue) return;
                totals.Add(entry.Id, (int)total.Value);
            }
            Run(() => Session.NewTurn(manualTotal: entry => totals[entry.Id]));
        }
        [RelayCommand] private void Sort() => Run(Session.Sort);
        [RelayCommand] private void Delay() { if (Selected != null) Run(() => Session.Delay(Selected.Entry)); }
        [RelayCommand] private void Resume() { if (Selected != null) Run(() => Session.Resume(Selected.Entry)); }
        [RelayCommand] private void SetScore() { if (Selected != null) Run(() => Session.SetScore(Selected.Entry, (int)Score)); }
        [RelayCommand] private void Adjust(string delta) { if (Selected != null && int.TryParse(delta, out int value)) Run(() => Session.Adjust(Selected.Entry, value)); }
    }
}
