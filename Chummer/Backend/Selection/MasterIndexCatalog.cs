using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.XPath;

namespace Chummer.Selection
{
    public sealed class MasterIndexEntry
    {
        public string Name { get; set; }
        public string File { get; set; }
        public string Book { get; set; }
        public string Page { get; set; }
        public string DisplayPage { get; set; }
        public string Notes { get; set; }
        public string Source => Book + " " + Page;
        public string DisplaySource => Book + " " + DisplayPage;
    }

    public static class MasterIndexCatalog
    {
        // Same source files and page/source selection as legacy MasterIndex.LoadContent.
        private static readonly string[] Files = { "actions.xml", "armor.xml", "bioware.xml", "complexforms.xml", "critters.xml", "critterpowers.xml",
            "cyberware.xml", "drugs.xml", "drugcomponents.xml", "echoes.xml", "gear.xml", "lifemodules.xml", "lifestyles.xml", "martialarts.xml",
            "mentors.xml", "metamagic.xml", "metatypes.xml", "paragons.xml", "powers.xml", "programs.xml", "qualities.xml", "references.xml",
            "skills.xml", "spells.xml", "spiritpowers.xml", "streams.xml", "traditions.xml", "vehicles.xml", "weapons.xml" };

        public static async Task<IReadOnlyList<MasterIndexEntry>> LoadAsync(CharacterSettings settings, CancellationToken token = default)
        {
            var result = new List<MasterIndexEntry>();
            var books = (await settings.GetBooksAsync(token).ConfigureAwait(false)).ToList();
            string filter = books.Count == 0 ? "source" : "(" + string.Join(" or ", books.Select(x => "source = " + x.CleanXPath())) + ")";
            foreach (string file in Files)
            {
                var document = await settings.LoadDataXPathAsync(file, token: token).ConfigureAwait(false);
                foreach (XPathNavigator node in document.Select("/chummer//*[page and " + filter + "]"))
                {
                    token.ThrowIfCancellationRequested();
                    result.Add(new MasterIndexEntry
                    {
                        Name = node.SelectSingleNode("translate")?.Value ?? node.SelectSingleNode("name")?.Value ?? node.SelectSingleNode("id")?.Value ?? "Unknown",
                        File = file,
                        Book = node.SelectSingleNode("source")?.Value,
                        Page = node.SelectSingleNode("page")?.Value,
                        DisplayPage = node.SelectSingleNode("altpage")?.Value ?? node.SelectSingleNode("page")?.Value,
                        Notes = node.SelectSingleNode("altnotes")?.Value ?? node.SelectSingleNode("notes")?.Value ?? string.Empty
                    });
                }
            }
            return result.OrderBy(x => x.Name, StringComparer.CurrentCulture).ThenBy(x => x.Book).ThenBy(x => x.Page).ToList();
        }
    }
}
