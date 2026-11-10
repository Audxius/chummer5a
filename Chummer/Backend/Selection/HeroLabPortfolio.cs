using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.XPath;

namespace Chummer.Selection
{
    public sealed class HeroLabPortfolioEntry
    {
        public string CharacterId { get; set; }
        public string Name { get; set; }
        public string Player { get; set; }
        public string Metatype { get; set; }
        public string Description => Name + (string.IsNullOrEmpty(Metatype) ? "" : " // " + Metatype)
            + (string.IsNullOrEmpty(Player) ? "" : " // " + Player);
    }

    public static class HeroLabPortfolio
    {
        /// <summary>The statblock selection used by legacy HeroLabImporter.CacheCharacters.</summary>
        public static Task<IReadOnlyList<HeroLabPortfolioEntry>> ReadAsync(string path, CancellationToken token = default)
            => Task.Run<IReadOnlyList<HeroLabPortfolioEntry>>(() =>
            {
                // Ensure the platform encoding provider has been initialized before opening a legacy ZIP.
                _ = HostServices.AppVersion;
                var characters = new List<HeroLabPortfolioEntry>();
                using (ZipArchive portfolio = ZipFile.Open(path, ZipArchiveMode.Read, Encoding.GetEncoding(850)))
                {
                    foreach (ZipArchiveEntry entry in portfolio.Entries)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!entry.FullName.StartsWith("statblocks_xml", StringComparison.OrdinalIgnoreCase)
                            || !entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
                        using (Stream stream = entry.Open())
                        using (XmlReader reader = XmlReader.Create(stream, GlobalSettings.SafeXmlReaderSettings))
                        {
                            var document = new XPathDocument(reader);
                            foreach (XPathNavigator character in document.CreateNavigator().Select("/document/public/character"))
                            {
                                string id = character.GetAttribute("name", "");
                                if (string.IsNullOrEmpty(id)) continue;
                                characters.Add(new HeroLabPortfolioEntry { CharacterId = id, Name = id, Player = character.GetAttribute("playername", ""),
                                    Metatype = character.SelectSingleNode("race/@name")?.Value ?? "" });
                            }
                        }
                    }
                }
                return characters.GroupBy(x => x.CharacterId).Select(x => x.First()).OrderBy(x => x.Name, StringComparer.CurrentCulture).ToList();
            }, token);
    }
}
