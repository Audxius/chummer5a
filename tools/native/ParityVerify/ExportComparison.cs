using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chummer.ParityVerification
{
    /// <summary>Export equivalences are limited to keyed attribute order and portrait re-encoding.
    /// Values, other arrays, notes, and every non-portrait string remain significant.</summary>
    public static class ExportComparison
    {
        private static readonly Regex Base64 = new Regex(@"^[A-Za-z0-9+/\r\n]+={0,2}$", RegexOptions.CultureInvariant);
        public static string CanonicalizeXml(string normalized)
        {
            var document = XDocument.Parse(normalized, LoadOptions.None);
            foreach (var group in document.Descendants("attributes"))
            {
                var rows = group.Elements("attribute").OrderBy(x => x.Element("name_english")?.Value ?? x.Element("name")?.Value, StringComparer.Ordinal)
                    .ThenBy(x => x.Element("metatypecategory")?.Value, StringComparer.Ordinal).ToList();
                int index = 0;
                var nodes = group.Nodes().Select(x => x is XElement element && element.Name == "attribute" ? rows[index++] : x).ToList();
                group.ReplaceNodes(nodes);
            }
            return document.ToString();
        }
        public static string CanonicalizeJson(string normalized)
        {
            var root = JToken.Parse(normalized);
            var properties = ((JContainer)root).Descendants().OfType<JProperty>().ToList();
            foreach (var property in properties.Where(x => x.Name == "attributes")) SortGroups(property.Value);
            foreach (var property in ((JContainer)root).Descendants().OfType<JProperty>().ToList())
            {
                bool portrait = property.Name == "mainmugshotbase64"
                    || property.Name == "stringbase64" && property.Ancestors().OfType<JProperty>().Any(x => x.Name == "othermugshots");
                if (portrait && property.Value.Type == JTokenType.String)
                {
                    string value = property.Value.Value<string>();
                    if (value.Length >= 200 && Base64.IsMatch(value)) property.Value = "[base64]";
                }
            }
            return root.ToString(Formatting.Indented);
        }
        private static void SortGroups(JToken token)
        {
            if (token is JArray groups) { foreach (var group in groups) SortGroups(group); return; }
            if (!(token is JObject obj) || !(obj["attribute"] is JArray rows)) return;
            if (rows.Any(x => !(x is JObject) || x["name_english"] == null && x["name"] == null)) return;
            obj["attribute"] = new JArray(rows.OrderBy(x => x["name_english"]?.Value<string>() ?? x["name"]?.Value<string>(), StringComparer.Ordinal)
                .ThenBy(x => x["metatypecategory"]?.Value<string>(), StringComparer.Ordinal));
        }
    }
}
