/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;

namespace Chummer.Desktop.ViewModels
{
    /// <summary>Portrait (mugshot) management, like legacy Chummer's mugshot box on the Character Info tab.</summary>
    public sealed partial class CharacterViewModel
    {
        public bool HasPortrait => Portrait != null;

        partial void OnPortraitChanged(Bitmap value) => OnPropertyChanged(nameof(HasPortrait));

        /// <summary>Picks an image file and makes it the runner's main portrait.</summary>
        [RelayCommand]
        private async Task ChangePortrait()
        {
            Window objMain = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (objMain == null)
                return;
            IStorageFolder objStart = null;
            if (!string.IsNullOrWhiteSpace(GlobalSettings.RecentImageFolder) && Directory.Exists(GlobalSettings.RecentImageFolder))
                objStart = await objMain.StorageProvider.TryGetFolderFromPathAsync(GlobalSettings.RecentImageFolder).ConfigureAwait(true);
            IReadOnlyList<IStorageFile> lstFiles = await objMain.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose a portrait",
                AllowMultiple = false,
                SuggestedStartLocation = objStart,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp" } },
                    FilePickerFileTypes.All,
                }
            }).ConfigureAwait(true);
            string strPath = lstFiles.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrEmpty(strPath))
                return;
            byte[] abytEncoded;
            try
            {
                abytEncoded = await Task.Run(() => EncodePortrait(strPath)).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is NotSupportedException)
            {
                await MessageBoxes.ShowAsync("That file could not be read as an image:\n" + ex.Message, "Portrait", MessageButtons.OK, MessageIcon.Warning).ConfigureAwait(true);
                return;
            }
            GlobalSettings.RecentImageFolder = Path.GetDirectoryName(strPath);
            await Task.Run(async () =>
            {
                MugshotImage objMugshot = new MugshotImage(abytEncoded);
                ThreadSafeList<MugshotImage> lstMugshots = await Character.GetMugshotsAsync().ConfigureAwait(false);
                await lstMugshots.AddAsync(objMugshot).ConfigureAwait(false);
                await Character.SetMainMugshotIndexAsync(await lstMugshots.IndexOfAsync(objMugshot).ConfigureAwait(false)).ConfigureAwait(false);
            }).ConfigureAwait(true);
            MarkDirty();
            _ = Task.Run(RefreshSummary);
        }

        /// <summary>Removes the main portrait (legacy RemoveMugshot).</summary>
        [RelayCommand]
        private async Task RemovePortrait()
        {
            await Task.Run(async () =>
            {
                int intIndex = await Character.GetMainMugshotIndexAsync().ConfigureAwait(false);
                if (intIndex < 0)
                    return;
                ThreadSafeList<MugshotImage> lstMugshots = await Character.GetMugshotsAsync().ConfigureAwait(false);
                if (intIndex >= await lstMugshots.GetCountAsync().ConfigureAwait(false))
                    return;
                await lstMugshots.RemoveAtAsync(intIndex).ConfigureAwait(false);
                await Character.SetMainMugshotIndexAsync(-1).ConfigureAwait(false);
            }).ConfigureAwait(true);
            MarkDirty();
            _ = Task.Run(RefreshSummary);
        }

        /// <summary>Downscales big photos (saves stay small) and stores the portrait as PNG, which legacy Chummer reads too.</summary>
        private static byte[] EncodePortrait(string strPath)
        {
            using (FileStream objFile = File.OpenRead(strPath))
            using (Bitmap objBitmap = new Bitmap(objFile))
            {
                Bitmap objScaled = objBitmap;
                try
                {
                    if (objBitmap.PixelSize.Width > 768)
                    {
                        objFile.Position = 0;
                        objScaled = Bitmap.DecodeToWidth(objFile, 768);
                    }
                    using (MemoryStream objOut = new MemoryStream())
                    {
                        objScaled.Save(objOut);
                        return objOut.ToArray();
                    }
                }
                finally
                {
                    if (!ReferenceEquals(objScaled, objBitmap))
                        objScaled.Dispose();
                }
            }
        }
    }
}
