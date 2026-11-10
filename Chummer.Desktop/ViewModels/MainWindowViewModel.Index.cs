using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Chummer.Desktop.Views.Dialogs;

namespace Chummer.Desktop.ViewModels
{
    public sealed partial class MainWindowViewModel
    {
        [RelayCommand]
        private async Task MasterIndex()
        {
            var settings = (CurrentPage as CharacterViewModel)?.Character.Settings
                ?? (await SettingsManager.GetLoadedCharacterSettingsAsync()).Values.FirstOrDefault();
            if (settings == null) return;
            var window = new MasterIndexWindow(settings);
            window.Opened += async (_, _) => await window.LoadAsync();
            await DialogHost.ShowModalAsync(window, null);
        }
    }
}
