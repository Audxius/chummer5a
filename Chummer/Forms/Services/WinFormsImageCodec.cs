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
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Chummer
{
    /// <summary>
    /// WinForms (System.Drawing) implementation of <see cref="IImageCodec"/>. Decodes exactly as character loading
    /// always did (32bpp premultiplied ARGB) so re-encoded output is unchanged.
    /// </summary>
    public sealed class WinFormsImageCodec : IImageCodec
    {
        public string ToStorageBase64(MugshotImage objImage, int intQuality, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            Image imgDecoded = objImage.GetImage(token);
            return intQuality == int.MaxValue
                ? imgDecoded.ToBase64String(token: token)
                : imgDecoded.ToBase64StringAsJpeg(intQuality, token);
        }

        public Task<string> ToStorageBase64Async(MugshotImage objImage, int intQuality, CancellationToken token = default)
        {
            if (token.IsCancellationRequested)
                return Task.FromCanceled<string>(token);
            Image imgDecoded = objImage.GetImage(token);
            return intQuality == int.MaxValue
                ? imgDecoded.ToBase64StringAsync(token: token)
                : imgDecoded.ToBase64StringAsJpegAsync(intQuality, token: token);
        }

        public Task<string> ToJpegBase64Async(MugshotImage objImage, int intQuality = -1, CancellationToken token = default)
        {
            if (token.IsCancellationRequested)
                return Task.FromCanceled<string>(token);
            return objImage.GetImage(token).ToBase64StringAsJpegAsync(intQuality, token: token);
        }
    }

    /// <summary>
    /// WinForms access to <see cref="MugshotImage"/> pixels.
    /// </summary>
    public static class MugshotImageWinFormsExtensions
    {
        /// <summary>
        /// The decoded image (cached on the mugshot and disposed with it). Do not dispose the returned object.
        /// </summary>
        public static Image GetImage(this MugshotImage objMugshot, CancellationToken token = default)
        {
            if (objMugshot == null)
                return null;
            if (objMugshot.HostImage is Image imgCached)
                return imgCached;
            Image imgNew = objMugshot.ToBase64().ToImage(PixelFormat.Format32bppPArgb, token);
            objMugshot.HostImage = imgNew;
            return imgNew;
        }

        /// <summary>
        /// Wraps a WinForms image (e.g. one the user just picked) as a mugshot, encoded losslessly.
        /// </summary>
        public static MugshotImage ToMugshot(this Image imgSource)
        {
            if (imgSource == null)
                throw new ArgumentNullException(nameof(imgSource));
            using (MemoryStream objStream = new MemoryStream())
            {
                imgSource.Save(objStream, ImageFormat.Png);
                MugshotImage objReturn = new MugshotImage(objStream.ToArray());
                objReturn.HostImage = imgSource;
                return objReturn;
            }
        }
    }
}
