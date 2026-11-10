/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using System.Threading.Tasks;
using System.Linq;
using Chummer.Desktop.Views.Dialogs;
using CommunityToolkit.Mvvm.Input;

namespace Chummer.Desktop.ViewModels
{
    public sealed partial class CharacterViewModel
    {
        /// <summary>Print / export the character sheet (legacy File > Print Character).</summary>
        [RelayCommand]
        private Task Print() => SheetWindow.ShowAsync(new[] { Character });

        [RelayCommand]
        private Task ExportCharacter() => DialogHost.ShowModalAsync(new CharacterExportWindow(Character), null);

        [RelayCommand]
        private Task PrintAllOpen() => SheetWindow.ShowAsync(MainWindowViewModel.Current.OpenCharacters.Select(x => x.Character).ToArray());
    }
}
