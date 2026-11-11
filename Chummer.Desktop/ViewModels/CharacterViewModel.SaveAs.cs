/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;

namespace Chummer.Desktop.ViewModels
{
    public sealed partial class CharacterViewModel
    {
        /// <summary>Legacy File > Save As: pick a new .chum5 (or compressed .chum5lz) path and save there.</summary>
        [RelayCommand]
        private async Task SaveAs()
        {
            await FlushInfoEditsAsync();
            Window objMain = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (objMain == null)
                return;
            string strCurrent = Character.FileName;
            IStorageFolder objStart = null;
            if (!string.IsNullOrEmpty(strCurrent) && Directory.Exists(Path.GetDirectoryName(strCurrent)))
                objStart = await objMain.StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(strCurrent)).ConfigureAwait(true);
            IStorageFile objFile = await objMain.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save runner as",
                SuggestedStartLocation = objStart,
                SuggestedFileName = string.IsNullOrEmpty(strCurrent) ? (Name + ".chum5") : Path.GetFileName(strCurrent),
                DefaultExtension = "chum5",
                FileTypeChoices = new List<FilePickerFileType>
                {
                    new FilePickerFileType("Chummer character") { Patterns = new[] { "*.chum5" } },
                    new FilePickerFileType("Compressed Chummer character") { Patterns = new[] { "*.chum5lz" } },
                },
            }).ConfigureAwait(true);
            string strPath = objFile?.TryGetLocalPath();
            if (string.IsNullOrEmpty(strPath))
                return;
            if (!strPath.EndsWith(".chum5", System.StringComparison.OrdinalIgnoreCase) && !strPath.EndsWith(".chum5lz", System.StringComparison.OrdinalIgnoreCase))
                strPath += ".chum5";
            // Like legacy SaveCharacterAs: switch the file name, save, and switch back if saving failed.
            string strOld = Character.FileName;
            await Character.SetFileNameAsync(strPath).ConfigureAwait(true);
            bool blnSaved = false;
            try
            {
                await FlushInfoEditsAsync();
                blnSaved = await Character.SaveAsync().ConfigureAwait(true);
            }
            finally
            {
                if (!blnSaved)
                    await Character.SetFileNameAsync(strOld).ConfigureAwait(true);
            }
            if (blnSaved)
            {
                IsDirty = false;
                OnPropertyChanged(nameof(FilePath));
            }
        }
    }
}
