using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace Chummer.Printing
{
    public static class DataArchiveExporter
    {
        /// <summary>Legacy DataExporter: the selected settings and every merged game-data XML file in a flat ZIP.</summary>
        public static async Task ExportAsync(CharacterSettings settings, Stream destination, string language = null,
            IProgress<int> progress = null, CancellationToken token = default)
        {
            language = string.IsNullOrEmpty(language) ? GlobalSettings.Language : language;
            using (ZipArchive archive = new ZipArchive(destination, ZipArchiveMode.Create, true))
            {
                using (Stream entry = archive.CreateEntry("_selectedsetting.xml", CompressionLevel.Optimal).Open())
                    if (!await settings.SaveAsync(entry, token: token).ConfigureAwait(false)) throw new IOException("Could not export settings.");
                int completed = 1;
                progress?.Report(completed);
                foreach (string file in Utils.BasicDataFileNames)
                {
                    token.ThrowIfCancellationRequested();
                    var data = await settings.LoadDataAsync(file, language, token: token).ConfigureAwait(false);
                    using (Stream entry = archive.CreateEntry(Path.GetFileName(file), CompressionLevel.Optimal).Open()) data.Save(entry);
                    progress?.Report(++completed);
                }
            }
        }
    }
}
