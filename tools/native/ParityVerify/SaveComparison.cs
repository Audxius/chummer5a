using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Chummer.ParityVerification
{
    /// <summary>Only known non-semantic differences in normalized saved characters.
    /// Does not remove elements, alter GUID references, sort gameplay lists, or normalize arbitrary text.</summary>
    public static class SaveComparison
    {
        public static string Canonicalize(string normalized)
        {
            var document = XDocument.Parse(normalized, LoadOptions.None);
            var attributes = document.Root?.Element("attributes");
            if (attributes != null)
            {
                var rows = attributes.Elements().OrderBy(x => x.Element("name")?.Value, StringComparer.Ordinal)
                    .ThenBy(x => x.Element("metatypecategory")?.Value, StringComparer.Ordinal).ToList();
                attributes.ReplaceNodes(rows);
            }
            var weight = document.Root?.Element("totalcarriedweight");
            if (weight != null && decimal.TryParse(weight.Value, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out decimal value))
                weight.Value = value.ToString("G29", CultureInfo.InvariantCulture);
            return document.ToString();
        }
    }
}
