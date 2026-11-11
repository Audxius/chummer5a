/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using CommunityToolkit.Mvvm.Input;

namespace Chummer.Desktop.ViewModels
{
    public sealed partial class CharacterViewModel
    {
        [RelayCommand]
        private void OpenRollPicker()
        {
            var main = MainWindowViewModel.Current;
            if (main == null) return;
            main.Dice.Activate(this);
            main.CurrentPage = main.Dice;
        }
    }
}
