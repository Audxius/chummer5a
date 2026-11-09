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
using SkiaSharp;

namespace Chummer.Desktop.Services
{
    /// <summary>
    /// SkiaSharp (cross-platform) implementation of <see cref="IImageCodec"/>.
    /// </summary>
    public sealed class SkiaImageCodec : IImageCodec
    {
        private const int DefaultJpegQuality = 90;

        public string ToStorageBase64(MugshotImage objImage, int intQuality, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (objImage == null)
                return string.Empty;
            return intQuality == int.MaxValue
                ? Encode(objImage, SKEncodedImageFormat.Png, 100)
                : Encode(objImage, SKEncodedImageFormat.Jpeg, intQuality < 0 ? DefaultJpegQuality : intQuality);
        }

        public Task<string> ToStorageBase64Async(MugshotImage objImage, int intQuality, CancellationToken token = default)
        {
            if (token.IsCancellationRequested)
                return Task.FromCanceled<string>(token);
            return Task.Run(() => ToStorageBase64(objImage, intQuality, token), token);
        }

        public Task<string> ToJpegBase64Async(MugshotImage objImage, int intQuality = -1, CancellationToken token = default)
        {
            if (token.IsCancellationRequested)
                return Task.FromCanceled<string>(token);
            return Task.Run(() => objImage == null
                                ? string.Empty
                                : Encode(objImage, SKEncodedImageFormat.Jpeg, intQuality < 0 ? DefaultJpegQuality : intQuality), token);
        }

        private static string Encode(MugshotImage objImage, SKEncodedImageFormat eFormat, int intQuality)
        {
            using (SKBitmap objBitmap = SKBitmap.Decode(objImage.EncodedBytes))
            {
                if (objBitmap == null)
                    return objImage.ToBase64(); // unknown format: keep the original bytes
                using (SKImage objSkImage = SKImage.FromBitmap(objBitmap))
                using (SKData objData = objSkImage.Encode(eFormat, intQuality))
                    return Convert.ToBase64String(objData.ToArray());
            }
        }
    }
}
