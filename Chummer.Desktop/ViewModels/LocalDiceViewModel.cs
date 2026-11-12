/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Chummer.Rules;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;

namespace Chummer.Desktop.ViewModels
{
    public sealed class DieFaceViewModel
    {
        public string Label { get; }
        public IBrush Color { get; }
        public DieFaceViewModel(RolledDie die, DiceRollOptions options)
        {
            int target = options.Method == DiceRollMethod.ReallyLarge ? 1 : options.Method == DiceRollMethod.Large ? 3 : options.Cinematic ? 4 : 5;
            int glitch = options.Method == DiceRollMethod.ReallyLarge ? 7 : options.RushedJob ? 2 : 1;
            Label = die.Value + (die.IsBubble ? " · bubble" : "");
            Color = Brush.Parse(die.Value <= glitch ? "#FF4081" : !die.IsBubble && die.Value >= target ? "#00E5FF" : "#DDE8F2");
        }
        public override string ToString() => Label;
    }
    public sealed partial class LocalDiceViewModel : ViewModelBase
    {
        [ObservableProperty] private decimal _dice = 1;
        [ObservableProperty] private decimal _threshold;
        [ObservableProperty] private decimal _gremlins;
        [ObservableProperty] private int _method;
        [ObservableProperty] private bool _ruleOfSix;
        [ObservableProperty] private bool _rushedJob;
        [ObservableProperty] private bool _cinematic;
        [ObservableProperty] private bool _variableGlitch;
        [ObservableProperty] private bool _bubbleDie;
        [ObservableProperty] private string _summary = "Choose a pool and roll.";
        [ObservableProperty] private string _detail = "";
        [ObservableProperty] private bool _hasResult;
        private DiceRollResult _result;
        public string[] Methods { get; } = new[] { "Standard", "Large dice pool", "Really large dice pool" };
        public ObservableCollection<DieFaceViewModel> Faces { get; } = new();
        public ObservableCollection<string> History { get; } = new();
        public bool CanExplode => Method == 0;
        partial void OnMethodChanged(int value) { if (value != 0) RuleOfSix = false; OnPropertyChanged(nameof(CanExplode)); }
        private DiceRollOptions Options() => new() { Dice = (int)Dice, Threshold = (int)Threshold, Gremlins = (int)Gremlins,
            Method = (DiceRollMethod)Method, RuleOfSix = RuleOfSix, RushedJob = RushedJob, Cinematic = Cinematic, VariableGlitch = VariableGlitch, BubbleDie = BubbleDie };
        [RelayCommand] private void Roll() => Execute(false);
        [RelayCommand] private void Reroll() => Execute(true);
        private void Execute(bool reroll)
        {
            try
            {
                if (reroll && _result == null) return;
                var options = Options();
                var next = reroll ? DiceRollerRules.RerollMisses(_result, options) : DiceRollerRules.Roll(options);
                if (reroll && ReferenceEquals(next, _result)) { Detail = "No dice left to reroll."; return; }
                _result = next; HasResult = true;
                Summary = next.IsCriticalGlitch ? "Critical glitch" : next.Hits + (next.Hits == 1 ? " hit" : " hits") + (next.IsGlitch ? " · Glitch" : "");
                if (next.Threshold > 0 && !next.IsCriticalGlitch) Summary = (next.Success ? "Success · " : "Failure · ") + Summary;
                Detail = $"Sum {next.Sum} · Glitch dice {next.GlitchDice} / {next.GlitchThreshold}";
                Faces.Clear(); foreach (var die in next.Dice) Faces.Add(new DieFaceViewModel(die, options));
                History.Insert(0, (reroll ? "Reroll · " : "") + Summary + " · " + string.Join(", ", Faces));
                while (History.Count > 20) History.RemoveAt(History.Count - 1);
            }
            catch (Exception ex) { Detail = ex.Message; }
        }
    }
}
