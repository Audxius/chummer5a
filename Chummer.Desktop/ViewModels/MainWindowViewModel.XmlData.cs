using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Chummer.Desktop.Views;
using Chummer.Desktop.Views.Dialogs;

namespace Chummer.Desktop.ViewModels
{
    public sealed partial class MainWindowViewModel
    {
        [RelayCommand]
        private async Task EditXmlData()
        {
            var window = new XmlDataWindow();
            await DialogHost.ShowModalAsync(window, null);
        }
    }
}
