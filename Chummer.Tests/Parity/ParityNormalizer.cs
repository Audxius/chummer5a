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

// Deliberately free of any Chummer types: this file must compile unchanged against both the
// original net48 engine (to capture baselines) and the ported net8 engine (to verify them).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Chummer.Tests.Parity
{
    /// <summary>
    /// Turns engine output (saved characters, print/export XML, rendered sheets) into a canonical text form
    /// so that output from two engine builds can be compared with plain string equality and diffed in git.
    /// </summary>
    public sealed class ParityNormalizer
    {
        private static readonly Regex s_rgxGuid = new Regex(
            @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex s_rgxBase64Blob = new Regex(
            @"^[A-Za-z0-9+/\r\n]{200,}={0,2}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex s_rgxDataUri = new Regex(
            @"data:[a-zA-Z0-9.+/-]+;base64,[A-Za-z0-9+/=\r\n]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Elements that only identify the build or machine, removed wherever they appear.</summary>
        public ISet<string> DroppedElements { get; } = new HashSet<string>(StringComparer.Ordinal)
        {
            "appversion",
            "minimumappversion",
            "settingshashcode",
        };

        /// <summary>
        /// Containers whose child order is not meaningful (e.g. improvements regenerated in parallel on load).
        /// Their children are sorted by content, with GUIDs masked, before GUIDs are renumbered.
        /// </summary>
        public ISet<string> UnorderedContainers { get; } = new HashSet<string>(StringComparer.Ordinal)
        {
            "improvements",
            "otherarmors",
        };

        /// <summary>Machine-specific path prefixes (e.g. the test files folder) replaced by stable tokens.</summary>
        public IList<KeyValuePair<string, string>> PathReplacements { get; } = new List<KeyValuePair<string, string>>();

        /// <summary>
        /// Canonicalizes an XML document: drops build-identifying elements, blanks out base64 blobs
        /// (JPEG re-encoding differs between platforms), sorts unordered containers, renumbers GUIDs
        /// by first appearance and pretty-prints with stable formatting.
        /// </summary>
        public string NormalizeXml(string strXml)
        {
            if (strXml == null)
                throw new ArgumentNullException(nameof(strXml));
            XDocument xmlDocument = XDocument.Parse(ReplacePaths(strXml), LoadOptions.None);
            if (xmlDocument.Root == null)
                return string.Empty;

            foreach (XElement xmlDropped in xmlDocument.Descendants().Where(x => DroppedElements.Contains(x.Name.LocalName)).ToList())
                xmlDropped.Remove();

            foreach (XElement xmlElement in xmlDocument.Descendants().Where(x => !x.HasElements))
            {
                string strValue = xmlElement.Value;
                if (strValue.Length >= 200 && s_rgxBase64Blob.IsMatch(strValue))
                    xmlElement.Value = "[base64]";
            }

            // Deepest containers first so that a parent's sort key already reflects sorted children.
            foreach (XElement xmlContainer in xmlDocument.Descendants()
                                                         .Where(x => UnorderedContainers.Contains(x.Name.LocalName))
                                                         .OrderByDescending(x => x.Ancestors().Count())
                                                         .ToList())
            {
                List<XElement> lstChildren = xmlContainer.Elements()
                                                         .OrderBy(x => x.Name.LocalName, StringComparer.Ordinal)
                                                         .ThenBy(x => s_rgxGuid.Replace(x.ToString(SaveOptions.DisableFormatting), "G"), StringComparer.Ordinal)
                                                         .ToList();
                xmlContainer.RemoveNodes();
                xmlContainer.Add(lstChildren);
            }

            string strCanonical = WriteCanonical(xmlDocument);
            return RenumberGuids(strCanonical);
        }

        /// <summary>
        /// Canonicalizes rendered sheet output (HTML from the XSLT sheets), which is not necessarily well-formed XML.
        /// </summary>
        public string NormalizeText(string strText)
        {
            if (strText == null)
                throw new ArgumentNullException(nameof(strText));
            string strReturn = ReplacePaths(strText);
            strReturn = s_rgxDataUri.Replace(strReturn, "data:[base64]");
            StringBuilder sbdReturn = new StringBuilder(strReturn.Length);
            using (StringReader objReader = new StringReader(strReturn))
            {
                for (string strLine = objReader.ReadLine(); strLine != null; strLine = objReader.ReadLine())
                {
                    string strTrimmed = strLine.TrimEnd();
                    if (strTrimmed.Length > 0)
                        sbdReturn.Append(strTrimmed).Append('\n');
                }
            }
            return RenumberGuids(sbdReturn.ToString());
        }

        private string ReplacePaths(string strInput)
        {
            string strReturn = strInput;
            foreach (KeyValuePair<string, string> kvpReplacement in PathReplacements)
            {
                if (string.IsNullOrEmpty(kvpReplacement.Key))
                    continue;
                strReturn = strReturn.Replace(kvpReplacement.Key, kvpReplacement.Value);
                // Paths can also appear with the other directory separator or URI-escaped.
                strReturn = strReturn.Replace(kvpReplacement.Key.Replace('\\', '/'), kvpReplacement.Value);
            }
            return strReturn;
        }

        /// <summary>
        /// Replaces every GUID with a sequence token in order of first appearance, so freshly generated GUIDs
        /// compare equal while references between elements stay intact. Data-file GUIDs (sourceid) are
        /// renumbered too; their identity is still checked through the element they sit in.
        /// </summary>
        private static string RenumberGuids(string strInput)
        {
            Dictionary<string, string> dicSeen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return s_rgxGuid.Replace(strInput, objMatch =>
            {
                string strGuid = objMatch.Value;
                if (strGuid == "00000000-0000-0000-0000-000000000000")
                    return "GUID-EMPTY";
                string strToken;
                if (!dicSeen.TryGetValue(strGuid, out strToken))
                {
                    strToken = "GUID-" + (dicSeen.Count + 1).ToString("D4", CultureInfo.InvariantCulture);
                    dicSeen.Add(strGuid, strToken);
                }
                return strToken;
            });
        }

        private static string WriteCanonical(XDocument xmlDocument)
        {
            XmlWriterSettings objSettings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
                OmitXmlDeclaration = true,
                Encoding = new UTF8Encoding(false),
                CheckCharacters = false,
            };
            StringBuilder sbdReturn = new StringBuilder();
            using (XmlWriter objWriter = XmlWriter.Create(new StringWriter(sbdReturn, CultureInfo.InvariantCulture), objSettings))
                xmlDocument.Root.WriteTo(objWriter);
            return sbdReturn.Append('\n').ToString();
        }
    }
}
