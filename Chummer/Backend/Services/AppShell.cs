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
using System.Threading;
using System.Threading.Tasks;

namespace Chummer
{
    /// <summary>
    /// Kinds of windows a character file can be open in (shown as markers in the character roster).
    /// </summary>
    [Flags]
    public enum CharacterWindowKinds
    {
        None = 0,
        Editor = 1,
        SheetViewer = 2,
        Export = 4
    }

    /// <summary>
    /// The application shell around the engine: which characters are open in which windows, opening/loading
    /// characters for the user, and reacting to global setting changes that affect all windows.
    /// </summary>
    public interface IAppShell
    {
        /// <summary>Whether any window currently shows the character.</summary>
        bool IsCharacterOpenInAnyWindow(Character objCharacter);

        /// <summary>Whether any window currently shows the character.</summary>
        Task<bool> IsCharacterOpenInAnyWindowAsync(Character objCharacter, CancellationToken token = default);

        /// <summary>Which kinds of windows show the character file at this path.</summary>
        CharacterWindowKinds GetOpenWindowKinds(string strFilePath);

        /// <summary>Which kinds of windows show the character file at this path.</summary>
        Task<CharacterWindowKinds> GetOpenWindowKindsAsync(string strFilePath, CancellationToken token = default);

        /// <summary>Load a character file (registering it as open), showing progress to the user.</summary>
        Character LoadCharacter(string strFileName, bool blnShowErrors = true, CancellationToken token = default);

        /// <summary>Load a character file (registering it as open), showing progress to the user.</summary>
        Task<Character> LoadCharacterAsync(string strFileName, bool blnShowErrors = true, CancellationToken token = default);

        /// <summary>Bring an already-open window for the character to the front.</summary>
        /// <returns>False if no window shows the character.</returns>
        Task<bool> SwitchToOpenCharacterAsync(Character objCharacter, CancellationToken token = default);

        /// <summary>Open an editor window for the character.</summary>
        Task OpenCharacterAsync(Character objCharacter, CancellationToken token = default);

        /// <summary>The UI language changed: retranslate open windows.</summary>
        void OnLanguageChanged();

        /// <summary>The UI language changed: retranslate open windows.</summary>
        Task OnLanguageChangedAsync(CancellationToken token = default);

        /// <summary>The telemetry setting changed while running.</summary>
        void OnTelemetryDisabledChanged(bool blnDisabled);

        /// <summary>The emulated browser version for embedded HTML views changed (or was loaded at startup).</summary>
        void OnEmulatedBrowserVersionChanged(int intVersion);
    }

    /// <summary>
    /// Used when no UI has registered itself: no windows exist; characters load without progress display.
    /// </summary>
    public sealed class HeadlessAppShell : IAppShell
    {
        public bool IsCharacterOpenInAnyWindow(Character objCharacter) => false;

        public Task<bool> IsCharacterOpenInAnyWindowAsync(Character objCharacter, CancellationToken token = default)
            => token.IsCancellationRequested ? Task.FromCanceled<bool>(token) : Task.FromResult(false);

        public CharacterWindowKinds GetOpenWindowKinds(string strFilePath) => CharacterWindowKinds.None;

        public Task<CharacterWindowKinds> GetOpenWindowKindsAsync(string strFilePath, CancellationToken token = default)
            => token.IsCancellationRequested ? Task.FromCanceled<CharacterWindowKinds>(token) : Task.FromResult(CharacterWindowKinds.None);

        public Character LoadCharacter(string strFileName, bool blnShowErrors = true, CancellationToken token = default)
        {
            return Utils.SafelyRunSynchronously(t => LoadCharacterAsync(strFileName, blnShowErrors, t), token);
        }

        public async Task<Character> LoadCharacterAsync(string strFileName, bool blnShowErrors = true, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            Character objCharacter = await CharacterRegistry.OpenCharacters.FirstOrDefaultAsync(
                async (x, t) => string.Equals(await x.GetFileNameAsync(t).ConfigureAwait(false), strFileName, Utils.PathStringComparison),
                token).ConfigureAwait(false);
            if (objCharacter != null)
                return objCharacter;
            objCharacter = new Character();
            try
            {
                await objCharacter.SetFileNameAsync(strFileName, token).ConfigureAwait(false);
                if (!await objCharacter.LoadAsync(showWarnings: blnShowErrors, token: token).ConfigureAwait(false))
                {
                    await objCharacter.DisposeAsync().ConfigureAwait(false);
                    return null;
                }
            }
            catch
            {
                await objCharacter.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            await CharacterRegistry.OpenCharacters.AddAsync(objCharacter, token).ConfigureAwait(false);
            return objCharacter;
        }

        public Task<bool> SwitchToOpenCharacterAsync(Character objCharacter, CancellationToken token = default)
            => token.IsCancellationRequested ? Task.FromCanceled<bool>(token) : Task.FromResult(false);

        public Task OpenCharacterAsync(Character objCharacter, CancellationToken token = default)
            => token.IsCancellationRequested ? Task.FromCanceled(token) : Task.CompletedTask;

        public void OnLanguageChanged()
        {
            // No windows to retranslate
        }

        public Task OnLanguageChangedAsync(CancellationToken token = default)
            => token.IsCancellationRequested ? Task.FromCanceled(token) : Task.CompletedTask;

        public void OnTelemetryDisabledChanged(bool blnDisabled)
        {
            // No telemetry pipeline
        }

        public void OnEmulatedBrowserVersionChanged(int intVersion)
        {
            // No embedded browser
        }
    }

    /// <summary>
    /// Characters currently loaded in the application (previously Program.OpenCharacters).
    /// </summary>
    public static class CharacterRegistry
    {
        public static ThreadSafeObservableCollection<Character> OpenCharacters { get; } = new ThreadSafeObservableCollection<Character>();
    }
}
