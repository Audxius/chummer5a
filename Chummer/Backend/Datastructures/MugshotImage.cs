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

namespace Chummer
{
    /// <summary>
    /// A picture (mugshot) held by the engine as encoded image file bytes (PNG, JPEG...). The engine never decodes
    /// pixels itself; hosts decode for display and re-encode through <see cref="IImageCodec"/>.
    /// </summary>
    public sealed class MugshotImage : IDisposable
    {
        private object _objHostImage;

        public MugshotImage(byte[] abytEncoded)
        {
            EncodedBytes = abytEncoded ?? throw new ArgumentNullException(nameof(abytEncoded));
        }

        public static MugshotImage FromBase64(string strBase64)
        {
            if (string.IsNullOrEmpty(strBase64))
                throw new ArgumentNullException(nameof(strBase64));
            return new MugshotImage(Convert.FromBase64String(strBase64));
        }

        /// <summary>Encoded image file contents.</summary>
        public byte[] EncodedBytes { get; }

        public string ToBase64() => Convert.ToBase64String(EncodedBytes);

        /// <summary>
        /// Decoded representation cached by the host (e.g. a WinForms Image or an Avalonia Bitmap); disposed with this object.
        /// </summary>
        public object HostImage
        {
            get => _objHostImage;
            set
            {
                object objOld = Interlocked.Exchange(ref _objHostImage, value);
                if (objOld != null && !ReferenceEquals(objOld, value))
                    (objOld as IDisposable)?.Dispose();
            }
        }

        public void Dispose()
        {
            (Interlocked.Exchange(ref _objHostImage, null) as IDisposable)?.Dispose();
        }
    }
}
