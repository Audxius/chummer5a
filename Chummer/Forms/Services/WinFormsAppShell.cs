/*  This file is part of Chummer5a.
 *
 *  Chummer5a is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  Chummer5a is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with Chummer5a.  If not, see <http://www.gnu.org/licenses/>.
 *
 *  You can obtain the full source code for Chummer5a at
 *  https://github.com/chummer5a/chummer5a
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Win32;

namespace Chummer
{
    /// <summary>
    /// WinForms implementation of <see cref="IAppShell"/> (code moved out of the engine's Contact, Spirit,
    /// CharacterCache and GlobalSettings).
    /// </summary>
    public sealed class WinFormsAppShell : IAppShell
    {
        public bool IsCharacterOpenInAnyWindow(Character objCharacter)
        {
            ChummerMainForm frmMain = Program.MainForm;
            return frmMain != null && frmMain.OpenFormsWithCharacters.Any(x => x.CharacterObjects.Contains(objCharacter));
        }

        public async Task<bool> IsCharacterOpenInAnyWindowAsync(Character objCharacter, CancellationToken token = default)
        {
            ChummerMainForm frmMain = Program.MainForm;
            return frmMain != null && await frmMain.AnyOpenFormContainsCharacter(objCharacter, token: token).ConfigureAwait(false);
        }

        public CharacterWindowKinds GetOpenWindowKinds(string strFilePath)
        {
            ChummerMainForm frmMain = Program.MainForm;
            if (frmMain == null)
                return CharacterWindowKinds.None;
            CharacterWindowKinds eReturn = CharacterWindowKinds.None;
            if (frmMain.OpenCharacterEditorForms?.Any(
                    x => !x.CharacterObject.IsDisposed && string.Equals(x.CharacterObject.FileName, strFilePath,
                                                                        Utils.PathStringComparison)) == true)
                eReturn |= CharacterWindowKinds.Editor;
            if (frmMain.OpenCharacterSheetViewers?.Any(
                    x => x.CharacterObjects.Any(y => !y.IsDisposed && string.Equals(y.FileName, strFilePath,
                                                                                     Utils.PathStringComparison))) == true)
                eReturn |= CharacterWindowKinds.SheetViewer;
            if (frmMain.OpenCharacterExportForms?.Any(
                    x => !x.CharacterObject.IsDisposed && string.Equals(x.CharacterObject.FileName, strFilePath,
                                                                        Utils.PathStringComparison)) == true)
                eReturn |= CharacterWindowKinds.Export;
            return eReturn;
        }

        public async Task<CharacterWindowKinds> GetOpenWindowKindsAsync(string strFilePath, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            ChummerMainForm frmMain = Program.MainForm;
            if (frmMain == null)
                return CharacterWindowKinds.None;
            CharacterWindowKinds eReturn = CharacterWindowKinds.None;
            ThreadSafeObservableCollection<CharacterShared> lstToProcess1 = frmMain.OpenCharacterEditorForms;
            if (lstToProcess1 != null && await lstToProcess1.GetCountAsync(token).ConfigureAwait(false) > 0
                && await lstToProcess1.AnyAsync(
                    async (x, t) => !x.CharacterObject.IsDisposed &&
                                    string.Equals(await x.CharacterObject.GetFileNameAsync(t).ConfigureAwait(false),
                                                  strFilePath, Utils.PathStringComparison), token).ConfigureAwait(false))
                eReturn |= CharacterWindowKinds.Editor;
            ThreadSafeObservableCollection<CharacterSheetViewer> lstToProcess2 = frmMain.OpenCharacterSheetViewers;
            if (lstToProcess2 != null && await lstToProcess2.GetCountAsync(token).ConfigureAwait(false) > 0
                && await lstToProcess2.AnyAsync(
                    (x, t1) => x.CharacterObjects.AnyAsync(
                        async (y, t2) => !y.IsDisposed && string.Equals(
                            await y.GetFileNameAsync(t2).ConfigureAwait(false), strFilePath,
                            Utils.PathStringComparison), t1), token).ConfigureAwait(false))
                eReturn |= CharacterWindowKinds.SheetViewer;
            ThreadSafeObservableCollection<ExportCharacter> lstToProcess3 = frmMain.OpenCharacterExportForms;
            if (lstToProcess3 != null && await lstToProcess3.GetCountAsync(token).ConfigureAwait(false) > 0
                && await lstToProcess3.AnyAsync(
                    async (x, t) => !x.CharacterObject.IsDisposed &&
                                    string.Equals(await x.CharacterObject.GetFileNameAsync(t).ConfigureAwait(false),
                                                  strFilePath, Utils.PathStringComparison), token).ConfigureAwait(false))
                eReturn |= CharacterWindowKinds.Export;
            return eReturn;
        }

        public Character LoadCharacter(string strFileName, bool blnShowErrors = true, CancellationToken token = default)
        {
            using (ThreadSafeForm<LoadingBar> frmLoadingBar = Program.CreateAndShowProgressBar(strFileName, Character.NumLoadingSections))
                return Program.LoadCharacter(strFileName, string.Empty, false, blnShowErrors, frmLoadingBar.MyForm, token);
        }

        public async Task<Character> LoadCharacterAsync(string strFileName, bool blnShowErrors = true, CancellationToken token = default)
        {
            using (ThreadSafeForm<LoadingBar> frmLoadingBar = await Program
                       .CreateAndShowProgressBarAsync(strFileName, Character.NumLoadingSections, token)
                       .ConfigureAwait(false))
                return await Program.LoadCharacterAsync(strFileName, string.Empty, false, blnShowErrors, frmLoadingBar.MyForm, token)
                                    .ConfigureAwait(false);
        }

        public Task<bool> SwitchToOpenCharacterAsync(Character objCharacter, CancellationToken token = default)
            => Program.SwitchToOpenCharacter(objCharacter, token);

        public Task OpenCharacterAsync(Character objCharacter, CancellationToken token = default)
            => Program.OpenCharacter(objCharacter, token: token);

        public void OnLanguageChanged()
        {
            ChummerMainForm frmMain = Program.MainForm;
            if (frmMain == null)
                return;
            try
            {
                frmMain.TranslateWinForm();
                IReadOnlyCollection<Form> lstToProcess = frmMain.OpenCharacterEditorForms;
                if (lstToProcess != null && lstToProcess.Count > 0)
                {
                    foreach (Form frmLoop in lstToProcess)
                        frmLoop.TranslateWinForm();
                }

                lstToProcess = frmMain.OpenCharacterSheetViewers;
                if (lstToProcess != null && lstToProcess.Count > 0)
                {
                    foreach (Form frmLoop in lstToProcess)
                        frmLoop.TranslateWinForm();
                }

                lstToProcess = frmMain.OpenCharacterExportForms;
                if (lstToProcess != null && lstToProcess.Count > 0)
                {
                    foreach (Form frmLoop in lstToProcess)
                        frmLoop.TranslateWinForm();
                }

                frmMain.PrintMultipleCharactersForm?.TranslateWinForm();
                frmMain.CharacterRoster?.TranslateWinForm();
                frmMain.MasterIndex?.TranslateWinForm();
                frmMain.RefreshAllTabTitles();
            }
            catch (ObjectDisposedException)
            {
                //swallow this
            }
            catch (OperationCanceledException)
            {
                //swallow this
            }
        }

        public async Task OnLanguageChangedAsync(CancellationToken token = default)
        {
            ChummerMainForm frmMain = Program.MainForm;
            if (frmMain == null)
                return;
            await frmMain.TranslateWinFormAsync(token: token).ConfigureAwait(false);
            ThreadSafeObservableCollection<CharacterShared> lstToProcess1 = frmMain.OpenCharacterEditorForms;
            if (lstToProcess1 != null && await lstToProcess1.GetCountAsync(token).ConfigureAwait(false) > 0)
                await lstToProcess1.ForEachAsync((x, t) => x.TranslateWinFormAsync(token: t), token).ConfigureAwait(false);
            ThreadSafeObservableCollection<CharacterSheetViewer> lstToProcess2 = frmMain.OpenCharacterSheetViewers;
            if (lstToProcess2 != null && await lstToProcess2.GetCountAsync(token).ConfigureAwait(false) > 0)
                await lstToProcess2.ForEachAsync((x, t) => x.TranslateWinFormAsync(token: t), token).ConfigureAwait(false);
            ThreadSafeObservableCollection<ExportCharacter> lstToProcess3 = frmMain.OpenCharacterExportForms;
            if (lstToProcess3 != null && await lstToProcess3.GetCountAsync(token).ConfigureAwait(false) > 0)
                await lstToProcess3.ForEachAsync((x, t) => x.TranslateWinFormAsync(token: t), token).ConfigureAwait(false);
            Form frmToProcess = frmMain.PrintMultipleCharactersForm;
            if (frmToProcess != null)
                await frmToProcess.TranslateWinFormAsync(token: token).ConfigureAwait(false);
            frmToProcess = frmMain.CharacterRoster;
            if (frmToProcess != null)
                await frmToProcess.TranslateWinFormAsync(token: token).ConfigureAwait(false);
            frmToProcess = frmMain.MasterIndex;
            if (frmToProcess != null)
                await frmToProcess.TranslateWinFormAsync(token: token).ConfigureAwait(false);
            await frmMain.RefreshAllTabTitlesAsync(token).ConfigureAwait(false);
        }

        public void OnTelemetryDisabledChanged(bool blnDisabled)
        {
            if (!Program.ChummerTelemetryClient.IsValueCreated)
                return;
            // Sets up logging if the option is changed during runtime
            TelemetryConfiguration objConfiguration = Program.ActiveTelemetryConfiguration;
            if (objConfiguration != null)
                objConfiguration.DisableTelemetry = blnDisabled;
        }

        /// <summary>
        /// IE emulation and DPI feature keys for the WinForms WebBrowser control (moved from Utils).
        /// </summary>
        public void OnEmulatedBrowserVersionChanged(int intVersion)
        {
            int intInternetExplorerVersionKey = intVersion * 1000;
            string strChummerExeName = AppDomain.CurrentDomain.FriendlyName;
            try
            {
                using (RegistryKey objRegistry = Registry.CurrentUser.CreateSubKey(
                           "Software\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_BROWSER_EMULATION", true))
                    objRegistry?.SetValue(strChummerExeName, intInternetExplorerVersionKey, RegistryValueKind.DWord);

                using (RegistryKey objRegistry = Registry.CurrentUser.CreateSubKey(
                           "Software\\WOW6432Node\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_BROWSER_EMULATION", true))
                    objRegistry?.SetValue(strChummerExeName, intInternetExplorerVersionKey, RegistryValueKind.DWord);

                // These two needed to have WebBrowser control obey DPI settings for Chummer
                using (RegistryKey objRegistry = Registry.CurrentUser.CreateSubKey(
                           "Software\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_96DPI_PIXEL", true))
                    objRegistry?.SetValue(strChummerExeName, 1, RegistryValueKind.DWord);

                using (RegistryKey objRegistry = Registry.CurrentUser.CreateSubKey(
                           "Software\\WOW6432Node\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_96DPI_PIXEL", true))
                    objRegistry?.SetValue(strChummerExeName, 1, RegistryValueKind.DWord);
            }
            catch (UnauthorizedAccessException)
            {
                // Swallow this
            }
            catch (System.IO.IOException)
            {
                // Swallow this
            }
        }
    }
}
