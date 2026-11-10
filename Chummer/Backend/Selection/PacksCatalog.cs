/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Chummer.Selection
{
    public sealed class PacksKitOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Contents { get; set; }
    }

    public static class PacksCatalog
    {
        public static async Task<IReadOnlyList<PacksKitOption>> LoadAsync(Character character, CancellationToken token = default)
        {
            var document = await character.LoadDataAsync("packs.xml", token: token).ConfigureAwait(false);
            var categories = new HashSet<string>(document.SelectNodes("/chummer/categories/category[not(hide)]").Cast<XmlNode>().Select(x => x.InnerText));
            var result = new List<PacksKitOption>();
            foreach (XmlNode pack in document.SelectNodes("/chummer/packs/pack[not(hide)]"))
            {
                token.ThrowIfCancellationRequested();
                string category = pack["category"]?.InnerText ?? "";
                if (!categories.Contains(category)) continue;
                var contents = new StringBuilder();
                foreach (XmlNode section in pack.ChildNodes)
                {
                    if (section.NodeType != XmlNodeType.Element || section.Name == "name" || section.Name == "category" || section.Name == "translate" || section.Name == "id") continue;
                    Describe(section, contents, 0);
                }
                result.Add(new PacksKitOption
                {
                    Id = pack["id"]?.InnerText ?? pack["name"]?.InnerText,
                    Name = pack["translate"]?.InnerText ?? pack["name"]?.InnerText ?? "",
                    Category = category,
                    Contents = contents.ToString().Trim()
                });
            }
            return result.OrderBy(x => x.Name, StringComparer.CurrentCulture).ToList();
        }

        private static void Describe(XmlNode node, StringBuilder output, int depth)
        {
            string title = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(node.Name);
            string name = node["translate"]?.InnerText ?? node["name"]?.InnerText;
            output.Append(' ', depth * 2);
            if (node.Name == "nuyenbp") { output.AppendLine("Karma allocated to starting funds: " + node.InnerText); return; }
            if (name != null)
            {
                output.Append(name);
                if (node["name"]?.Attributes?["select"] != null) output.Append(" (" + node["name"].Attributes["select"].InnerText + ")");
                foreach (string property in new[] { "rating", "qty", "grade", "months", "force", "services" })
                    if (node[property] != null) output.Append(" // " + property + " " + node[property].InnerText);
                output.AppendLine();
                foreach (XmlNode child in node.ChildNodes)
                    if (child.NodeType == XmlNodeType.Element && child.ChildNodes.Cast<XmlNode>().Any(x => x.NodeType == XmlNodeType.Element)) Describe(child, output, depth + 1);
            }
            else if (!node.ChildNodes.Cast<XmlNode>().Any(x => x.NodeType == XmlNodeType.Element)) output.AppendLine(title + ": " + node.InnerText);
            else
            {
                output.AppendLine(title);
                foreach (XmlNode child in node.ChildNodes) if (child.NodeType == XmlNodeType.Element) Describe(child, output, depth + 1);
            }
        }
    }
}
