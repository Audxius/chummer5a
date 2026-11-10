using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Globalization;
using Newtonsoft.Json;

namespace Chummer.Printing
{
    public static class CharacterExporter
    {
        public static IReadOnlyList<ListItem> Languages()
        {
            string directory = Path.Combine(Utils.GetStartupPath, "lang");
            var codes = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.xml").Select(Path.GetFileNameWithoutExtension)
                .Where(x => x.Length == 5 && x[2] == '-').ToList() : new List<string>();
            if (!codes.Contains(GlobalSettings.Language)) codes.Add(GlobalSettings.Language);
            return codes.Select(code =>
            {
                string name;
                try { name = CultureInfo.GetCultureInfo(code).NativeName; } catch (CultureNotFoundException) { name = code; }
                return new ListItem(code, name);
            }).OrderBy(x => x.Name).ToList();
        }

        public static IReadOnlyList<string> Formats()
        {
            string directory = Path.Combine(Utils.GetStartupPath, "export");
            return new[] { "JSON", "XML" }.Concat(Directory.Exists(directory)
                ? Directory.GetFiles(directory, "*.xsl").Select(Path.GetFileNameWithoutExtension).OrderBy(x => x)
                : Enumerable.Empty<string>()).ToList();
        }

        /// <summary>The legacy ExportCharacter pipeline: character print XML, then JSON or an export XSLT.</summary>
        public static async Task<string> RenderAsync(Character character, string format, string language = null, CancellationToken token = default)
        {
            if (!Formats().Contains(format)) throw new ArgumentException("Unknown export format.", nameof(format));
            language = string.IsNullOrEmpty(language) ? GlobalSettings.Language : language;
            XmlDocument data = await CommonFunctions.GenerateCharactersExportXml(GlobalSettings.CultureInfo, language, token, character).ConfigureAwait(false);
            if (format == "JSON") return JsonConvert.SerializeXmlNode(data, Newtonsoft.Json.Formatting.Indented);
            if (format == "XML") return data.OuterXml;
            var transform = await XslManager.GetTransformForFileAsync(Path.Combine(Utils.GetStartupPath, "export", format + ".xsl"), token).ConfigureAwait(false);
            XmlWriterSettings settings = transform.OutputSettings.Clone();
            settings.CheckCharacters = false;
            using (MemoryStream stream = new MemoryStream())
            {
                using (XmlWriter writer = XmlWriter.Create(stream, settings)) transform.Transform(data, writer);
                stream.Position = 0;
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                    return await reader.ReadToEndAsync().ConfigureAwait(false);
            }
        }
    }
}
