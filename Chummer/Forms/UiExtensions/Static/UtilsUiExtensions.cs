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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Extensions.ObjectPool;
using Microsoft.VisualStudio.Threading;
using Microsoft.Win32;
using NLog;
using Microsoft.IO;
using System.Xml.XPath;
using Xoshiro.PRNG64;

namespace Chummer
{
    /// <summary>
    /// UI (WinForms) members of <see cref="Chummer.Utils"/>, moved out of the engine for the native port.
    /// </summary>
    public static class UtilsUiExtensions
    {
        /// <summary>
        /// Restarts Chummer5a.
        /// </summary>
        /// <param name="strLanguage">Language in which to display any prompts or warnings. If empty, use the language of <paramref name="objCulture"/> (Chummer's current language if that is null).</param>
        /// <param name="strText">Text to display in the prompt to restart. If empty, no prompt is displayed.</param>
        /// <param name="token">Cancellation token to listen to.</param>
        public static async ValueTask RestartApplication(string strLanguage = "", string strText = "", CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(strLanguage))
                strLanguage = GlobalSettings.Language;
            if (!string.IsNullOrEmpty(strText))
            {
                string text = await LanguageManager.GetStringAsync(strText, strLanguage, token: token).ConfigureAwait(false);
                string caption
                    = await LanguageManager.GetStringAsync("MessageTitle_Options_CloseForms", strLanguage, token: token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (await MessageBoxes.ShowAsync(text, caption, MessageButtons.YesNo, MessageIcon.Question, token: token).ConfigureAwait(false)
                    != MessageResult.Yes)
                    return;
            }

            // Need to do this here in case file names are changed while closing forms (because a character who previously did not have a filename was saved when prompted)
            // Cannot use foreach because saving a character as created removes the current form and adds a new one
            ThreadSafeObservableCollection<CharacterShared> lstToProcess = Program.MainForm.OpenCharacterEditorForms;
            if (lstToProcess != null)
            {
                for (int i = await lstToProcess.GetCountAsync(token).ConfigureAwait(false); i >= 0; --i)
                {
                    token.ThrowIfCancellationRequested();
                    CharacterShared objOpenCharacterForm;
                    try
                    {
                        objOpenCharacterForm = await lstToProcess.GetValueAtAsync(i, token).ConfigureAwait(false);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // Swallow this, we've closed the form in between the loop and us trying to get the form at the index
                        continue;
                    }
                    catch (IndexOutOfRangeException)
                    {
                        // Swallow this, we've closed the form in between the loop and us trying to get the form at the index
                        continue;
                    }

                    if (objOpenCharacterForm?.IsDirty == true)
                    {
                        string strCharacterName = await objOpenCharacterForm.CharacterObject
                                                                            .GetCharacterNameAsync(token)
                                                                            .ConfigureAwait(false);
                        if (await MessageBoxes.ShowAsync(
                                StringExtensions.FastFormat(
                                    await LanguageManager.GetStringAsync(
                                            "Message_UnsavedChanges", strLanguage,
                                            token: token)
                                        .ConfigureAwait(false), strCharacterName),
                                await LanguageManager
                                    .GetStringAsync("MessageTitle_UnsavedChanges", strLanguage, token: token)
                                    .ConfigureAwait(false),
                                MessageButtons.YesNoCancel, MessageIcon.Question, token: token).ConfigureAwait(false) != MessageResult.Yes)
                        {
                            return;
                        }

                        try
                        {
                            // Attempt to save the Character. If the user cancels the Save As dialogue that may open, cancel the closing event so that changes are not lost.
                            bool blnResult = await objOpenCharacterForm.SaveCharacter(token: token)
                                                                       .ConfigureAwait(false);
                            if (!blnResult)
                                return;
                        }
                        catch (OperationCanceledException) when (!token.IsCancellationRequested)
                        {
                            // Do not throw if cancellation is from the form's internal generic token (because the form is already closing)
                        }
                    }
                }
            }

            Utils.Log.Info("Restart Chummer");
            string strFileName;
            string strArguments = string.Empty;
            Application.UseWaitCursor = true;
            try
            {
                // Get the parameters/arguments passed to program if any
                if (lstToProcess != null)
                {
                    using (new FetchSafelyFromObjectPool<StringBuilder>(Utils.StringBuilderPool, out StringBuilder sbdArguments))
                    {
                        foreach (CharacterShared objOpenCharacterForm in lstToProcess)
                        {
                            Character objLoop = objOpenCharacterForm.CharacterObject;
                            string strLoopFileName = objLoop != null
                                ? await objLoop.GetFileNameAsync(token).ConfigureAwait(false)
                                : string.Empty;
                            if (!string.IsNullOrEmpty(strLoopFileName))
                                sbdArguments.Append('\"', strLoopFileName, "\" ");
                        }

                        if (sbdArguments.Length > 0)
                            --sbdArguments.Length;

                        strArguments = sbdArguments.ToString();
                    }
                }

                strFileName = Utils.GetStartupPath + Path.DirectorySeparatorChar + AppDomain.CurrentDomain.FriendlyName;
                // Restart current application, with same arguments/parameters
                foreach (Form objForm in await Program.MainForm.DoThreadSafeFuncAsync(x => x.MdiChildren, token).ConfigureAwait(false))
                {
                    await objForm.DoThreadSafeAsync(x => x.Close(), token: token).ConfigureAwait(false);
                }
            }
            catch
            {
                Application.UseWaitCursor = false;
                throw;
            }
            // Sending restart command asynchronously to MySynchronizationContext so that tasks can properly clean up before restart
#pragma warning disable VSTHRD001
            Utils.MySynchronizationContext.Post(x =>
            {
                (string strMyFileName, string strMyArguments) = (ValueTuple<string, string>) x;
                ProcessStartInfo objStartInfo = new ProcessStartInfo
                {
                    FileName = strMyFileName,
                    Arguments = strMyArguments
                };
                try
                {
                    Application.Exit();
                }
                finally
                {
                    objStartInfo.Start();
                }
            }, new ValueTuple<string, string>(strFileName, strArguments));
#pragma warning restore VSTHRD001
        }
    }
}
