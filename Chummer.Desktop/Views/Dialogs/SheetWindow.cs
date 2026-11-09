/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Chummer.Desktop.Services;
using Chummer.Printing;

namespace Chummer.Desktop.Views.Dialogs
{
    /// <summary>
    /// Print / export character sheets (legacy CharacterSheetViewer). The sheet is rendered to HTML and shown in the system
    /// browser, which also handles printing; PDF goes through a headless Chromium when one is installed.
    /// </summary>
    public sealed class SheetWindow : HudWindow
    {
        private readonly IReadOnlyList<Character> _lstCharacters;
        private readonly ListBox _lstSheets = new ListBox { Height = 360 };
        private readonly TextBlock _lblStatus = new TextBlock { Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap };

        private SheetWindow(IReadOnlyList<Character> lstCharacters) : base("Character sheet", 560)
        {
            _lstCharacters = lstCharacters;
        }

        public static async Task ShowAsync(IReadOnlyList<Character> lstCharacters)
        {
            SheetWindow objWindow = new SheetWindow(lstCharacters);
            List<ListItem> lstSheets = await SheetRenderer.ListSheetsAsync(GlobalSettings.Language, lstCharacters).ConfigureAwait(true);
            objWindow._lstSheets.ItemsSource = lstSheets;
            objWindow._lstSheets.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(ListItem.Name));
            ListItem objDefault = lstSheets.FirstOrDefault(x => x.Value?.ToString() == GlobalSettings.DefaultCharacterSheet);
            objWindow._lstSheets.SelectedItem = objDefault.Value != null ? (object)objDefault : lstSheets.FirstOrDefault();

            Button cmdOpen = new Button { Content = "OPEN IN BROWSER", Classes = { "primary" }, IsDefault = true };
            Button cmdHtml = new Button { Content = "SAVE HTML…" };
            Button cmdPdf = new Button { Content = "SAVE PDF…", IsEnabled = FindChromium() != null };
            ToolTip.SetTip(cmdPdf, cmdPdf.IsEnabled ? "Rendered with headless Chromium" : "Install chromium (or google-chrome) to export PDF directly; or open in the browser and print to PDF.");
            ToolTip.SetShowOnDisabled(cmdPdf, true);
            Button cmdClose = new Button { Content = "CLOSE", IsCancel = true };
            cmdOpen.Click += async (sender, args) => await objWindow.OpenAsync().ConfigureAwait(true);
            cmdHtml.Click += async (sender, args) => await objWindow.SaveAsync(false).ConfigureAwait(true);
            cmdPdf.Click += async (sender, args) => await objWindow.SaveAsync(true).ConfigureAwait(true);
            cmdClose.Click += (sender, args) => objWindow.Close();
            objWindow.Content = Frame(new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    Heading(lstCharacters.Count == 1 ? "Print " + lstCharacters[0].CharacterName : "Print " + lstCharacters.Count + " runners"),
                    new TextBlock { Text = "SHEET", Classes = { "label" } },
                    objWindow._lstSheets,
                    objWindow._lblStatus,
                    ButtonRow(cmdOpen, cmdHtml, cmdPdf, cmdClose),
                }
            });
            Window objOwner = ModalHelper.ResolveOwner(null);
            if (objOwner != null && objOwner.IsVisible)
                await objWindow.ShowDialog(objOwner).ConfigureAwait(true);
            else
                objWindow.Show();
        }

        private string SelectedSheet => (_lstSheets.SelectedItem is ListItem objItem ? objItem.Value?.ToString() : null) ?? GlobalSettings.DefaultCharacterSheet;

        private async Task<string> RenderAsync()
        {
            _lblStatus.Text = "Generating sheet…";
            try
            {
                string strSheet = SelectedSheet;
                string strHtml = await Task.Run(() => SheetRenderer.RenderHtmlAsync(_lstCharacters, strSheet)).ConfigureAwait(true);
                _lblStatus.Text = string.Empty;
                return strHtml;
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Could not generate the sheet: " + ex.Message;
                return null;
            }
        }

        private static string OutputFolder()
        {
            string strCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (string.IsNullOrEmpty(strCache))
                strCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
            string strFolder = Path.Combine(strCache, "chummer5", "sheets");
            Directory.CreateDirectory(strFolder);
            return strFolder;
        }

        private string SafeName => string.Concat((_lstCharacters.Count == 1 ? _lstCharacters[0].CharacterName : "runners").Split(Path.GetInvalidFileNameChars()));

        private async Task OpenAsync()
        {
            string strHtml = await RenderAsync().ConfigureAwait(true);
            if (strHtml == null)
                return;
            string strPath = Path.Combine(OutputFolder(), SafeName + ".htm");
            await File.WriteAllTextAsync(strPath, strHtml).ConfigureAwait(true);
            Launch(strPath);
            _lblStatus.Text = "Opened " + strPath;
        }

        private async Task SaveAsync(bool blnPdf)
        {
            IStorageFile objFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = blnPdf ? "Save sheet as PDF" : "Save sheet as HTML",
                SuggestedFileName = SafeName + (blnPdf ? ".pdf" : ".htm"),
                DefaultExtension = blnPdf ? "pdf" : "htm",
                FileTypeChoices = new[] { blnPdf ? FilePickerFileTypes.Pdf : new FilePickerFileType("HTML") { Patterns = new[] { "*.htm", "*.html" } } },
            }).ConfigureAwait(true);
            string strTarget = objFile?.TryGetLocalPath();
            if (string.IsNullOrEmpty(strTarget))
                return;
            string strHtml = await RenderAsync().ConfigureAwait(true);
            if (strHtml == null)
                return;
            if (!blnPdf)
            {
                await File.WriteAllTextAsync(strTarget, strHtml).ConfigureAwait(true);
                _lblStatus.Text = "Saved " + strTarget;
                return;
            }
            string strTemp = Path.Combine(OutputFolder(), SafeName + ".htm");
            await File.WriteAllTextAsync(strTemp, strHtml).ConfigureAwait(true);
            _lblStatus.Text = "Rendering PDF…";
            string strChromium = FindChromium();
            int intExit = await Task.Run(() =>
            {
                using (Process objProcess = Process.Start(new ProcessStartInfo(strChromium)
                       {
                           ArgumentList = { "--headless", "--disable-gpu", "--no-pdf-header-footer", "--print-to-pdf=" + strTarget, new Uri(strTemp).AbsoluteUri },
                           UseShellExecute = false,
                           RedirectStandardError = true,
                           RedirectStandardOutput = true,
                       }))
                {
                    if (objProcess == null)
                        return -1;
                    objProcess.StandardError.ReadToEnd();
                    objProcess.WaitForExit(120000);
                    return objProcess.HasExited ? objProcess.ExitCode : -1;
                }
            }).ConfigureAwait(true);
            _lblStatus.Text = intExit == 0 && File.Exists(strTarget) ? "Saved " + strTarget : "PDF export failed (Chromium exit code " + intExit + ").";
        }

        private static string FindChromium()
        {
            string[] astrNames = { "chromium", "chromium-browser", "google-chrome-stable", "google-chrome", "brave", "microsoft-edge" };
            foreach (string strDir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                foreach (string strName in astrNames)
                {
                    string strPath = Path.Combine(strDir, strName);
                    if (File.Exists(strPath))
                        return strPath;
                }
            }
            return null;
        }

        private static void Launch(string strPath)
        {
            try
            {
                Process.Start(new ProcessStartInfo(OperatingSystem.IsWindows() ? strPath : OperatingSystem.IsMacOS() ? "open" : "xdg-open")
                {
                    ArgumentList = { },
                    UseShellExecute = OperatingSystem.IsWindows(),
                    Arguments = OperatingSystem.IsWindows() ? string.Empty : "\"" + strPath + "\"",
                })?.Dispose();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // No opener: the status line still shows where the file is.
            }
        }
    }
}
