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
        private async Task Print() { await FlushInfoEditsAsync(); await SheetWindow.ShowAsync(new[] { Character }); }

        [RelayCommand]
        private async Task ExportCharacter() { await FlushInfoEditsAsync(); await DialogHost.ShowModalAsync(new CharacterExportWindow(Character), null); }

        [RelayCommand]
        private async Task ExportPacksKit() { await FlushInfoEditsAsync(); await DialogHost.ShowModalAsync(new PacksExportWindow(Character), null); }

        [RelayCommand]
        private Task EditLimitModifiers() => DialogHost.ShowModalAsync(new LimitModifiersWindow(this), null);

        [RelayCommand]
        private async Task PrintAllOpen()
        {
            var runners = MainWindowViewModel.Current.OpenCharacters.ToArray();
            foreach (var runner in runners) await runner.FlushInfoEditsAsync();
            await SheetWindow.ShowAsync(runners.Select(x => x.Character).ToArray());
        }
    }
}
