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

using System.Threading;
using System.Threading.Tasks;

namespace Chummer
{
    /// <summary>
    /// Image re-encoding the engine needs when writing files: storage format for character files and JPEG for
    /// printed sheets. Pixel work lives in the host (System.Drawing on WinForms, SkiaSharp in the native app).
    /// </summary>
    public interface IImageCodec
    {
        /// <summary>
        /// Base64 of the image as it should be stored in a character file.
        /// </summary>
        /// <param name="intQuality">JPEG quality, or int.MaxValue for lossless PNG (GlobalSettings.SavedImageQuality).</param>
        string ToStorageBase64(MugshotImage objImage, int intQuality, CancellationToken token = default);

        /// <inheritdoc cref="ToStorageBase64"/>
        Task<string> ToStorageBase64Async(MugshotImage objImage, int intQuality, CancellationToken token = default);

        /// <summary>
        /// Base64 JPEG for embedding in printed sheets.
        /// </summary>
        Task<string> ToJpegBase64Async(MugshotImage objImage, int intQuality = -1, CancellationToken token = default);
    }

    /// <summary>
    /// Used when no UI has registered itself: images are passed through unchanged (no pixel work).
    /// </summary>
    public sealed class PassThroughImageCodec : IImageCodec
    {
        public string ToStorageBase64(MugshotImage objImage, int intQuality, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return objImage?.ToBase64() ?? string.Empty;
        }

        public Task<string> ToStorageBase64Async(MugshotImage objImage, int intQuality, CancellationToken token = default)
        {
            return token.IsCancellationRequested
                ? Task.FromCanceled<string>(token)
                : Task.FromResult(objImage?.ToBase64() ?? string.Empty);
        }

        public Task<string> ToJpegBase64Async(MugshotImage objImage, int intQuality = -1, CancellationToken token = default)
        {
            return token.IsCancellationRequested
                ? Task.FromCanceled<string>(token)
                : Task.FromResult(objImage?.ToBase64() ?? string.Empty);
        }
    }
}
