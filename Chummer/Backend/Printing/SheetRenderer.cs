/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Xsl;

namespace Chummer.Printing
{
    /// <summary>
    /// Renders characters through the XSLT character sheets (legacy CharacterSheetViewer: GenerateCharactersExportXml then
    /// the sheet's compiled transform), producing HTML that any browser can show, print or turn into PDF.
    /// </summary>
    public static class SheetRenderer
    {
        /// <summary>Sheets available for a language (value = path below "sheets" without extension, as legacy lists them).</summary>
        public static Task<List<ListItem>> ListSheetsAsync(string strLanguage, IEnumerable<Character> lstCharacters, CancellationToken token = default)
            => XmlManager.GetXslFilesFromLocalDirectoryAsync(string.IsNullOrEmpty(strLanguage) ? GlobalSettings.Language : strLanguage, lstCharacters, false, token);

        public static string SheetPath(string strSheet)
            => Path.Combine(Utils.GetStartupPath, "sheets", strSheet.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar) + ".xsl");

        public static async Task<string> RenderHtmlAsync(IReadOnlyList<Character> lstCharacters, string strSheet, string strLanguage = null, CultureInfo objCulture = null, CancellationToken token = default)
        {
            if (lstCharacters == null || lstCharacters.Count == 0)
                throw new ArgumentException("No characters to print.", nameof(lstCharacters));
            strLanguage = string.IsNullOrEmpty(strLanguage) ? GlobalSettings.Language : strLanguage;
            objCulture = objCulture ?? GlobalSettings.CultureInfo;
            string strXslPath = SheetPath(strSheet);
            if (!File.Exists(strXslPath))
                throw new FileNotFoundException("Character sheet not found: " + strSheet, strXslPath);
            Character[] aobjCharacters = new Character[lstCharacters.Count];
            for (int i = 0; i < aobjCharacters.Length; ++i)
                aobjCharacters[i] = lstCharacters[i];
            XmlDocument xmlCharacters = await CommonFunctions.GenerateCharactersExportXml(objCulture, strLanguage, token, aobjCharacters).ConfigureAwait(false);
            XslCompiledTransform objTransform = await XslManager.GetTransformForFileAsync(strXslPath, token).ConfigureAwait(false);
            XmlWriterSettings objSettings = objTransform.OutputSettings?.Clone();
            if (objSettings != null)
            {
                objSettings.CheckCharacters = false;
                objSettings.ConformanceLevel = ConformanceLevel.Fragment;
            }
            using (MemoryStream objStream = new MemoryStream())
            {
                using (XmlWriter objWriter = objSettings != null ? XmlWriter.Create(objStream, objSettings) : Utils.GetXslTransformXmlWriter(objStream))
                    objTransform.Transform(xmlCharacters, objWriter);
                objStream.Position = 0;
                StringBuilder sbdReturn = new StringBuilder();
                using (StreamReader objReader = new StreamReader(objStream, Encoding.UTF8, true))
                {
                    for (string strLine = await objReader.ReadLineAsync().ConfigureAwait(false); strLine != null; strLine = await objReader.ReadLineAsync().ConfigureAwait(false))
                    {
                        if (!string.IsNullOrEmpty(strLine))
                            sbdReturn.AppendLine(strLine);
                    }
                }
                return sbdReturn.ToString();
            }
        }
    }
}
