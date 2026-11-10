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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Xsl;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Chummer.Tests.Parity
{
    /// <summary>
    /// Golden-master tests that pin down everything the engine produces for the sample characters:
    /// saved files (sync and async writers), print/export XML in every language, rendered character sheets,
    /// the JSON and Squad Manager exports, and multi-character print XML.
    /// <para/>
    /// CHUMMER_PARITY_MODE=capture writes normalized output to the golden folder (run on the original engine).
    /// CHUMMER_PARITY_MODE=verify (default) compares against it and writes mismatches next to it under "Actual".
    /// CHUMMER_PARITY_GOLDEN_DIR overrides the golden folder (default: &lt;repo&gt;/Chummer.Parity/Golden).
    /// </summary>
    [TestClass]
    public class ParityGoldenTests
    {
        public TestContext TestContext { get; set; }

        private static readonly string[] s_astrLanguages = { "en-us", "de-de", "fr-fr", "ja-jp", "pt-br", "zh-cn" };

        /// <summary>
        /// Characters rendered with every sheet in every language. Kept small because sheet output is large;
        /// all other characters are rendered with the default sheet only. Chosen to span technomancer creation,
        /// magician career and priority career builds.
        /// </summary>
        private static readonly string[] s_astrFullSheetCharacters = { "Bastion", "Soma (Career)", "Wesson" };

        private static readonly string[] s_astrMultiPrintCharacters = { "Barrett", "Draught", "Miko" };

        private static bool IsCaptureMode =>
            string.Equals(Environment.GetEnvironmentVariable("CHUMMER_PARITY_MODE"), "capture", StringComparison.OrdinalIgnoreCase);

        private static string GoldenDirectory
        {
            get
            {
                string strOverride = Environment.GetEnvironmentVariable("CHUMMER_PARITY_GOLDEN_DIR");
                if (!string.IsNullOrEmpty(strOverride))
                    return strOverride;
                DirectoryInfo objDirectory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                while (objDirectory != null && !File.Exists(Path.Combine(objDirectory.FullName, "Chummer.sln")))
                    objDirectory = objDirectory.Parent;
                if (objDirectory == null)
                    throw new InvalidOperationException("Could not find the repository root; set CHUMMER_PARITY_GOLDEN_DIR.");
                return Path.Combine(objDirectory.FullName, "Chummer.Parity", "Golden");
            }
        }

        private static string ActualDirectory => Path.Combine(Path.GetDirectoryName(GoldenDirectory.TrimEnd('/', '\\')) ?? string.Empty, "Actual");

        private static readonly string s_strScratchDirectory = Path.Combine(Path.GetTempPath(), "chummer-parity");

        private readonly List<string> _lstFailures = new List<string>();

        private ParityNormalizer _objNormalizer;

        [TestInitialize]
        public void Setup()
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Directory.CreateDirectory(s_strScratchDirectory);
            _lstFailures.Clear();
            _objNormalizer = new ParityNormalizer();
            _objNormalizer.PathReplacements.Add(new KeyValuePair<string, string>(TestFilesDirectory, "<TESTFILES>"));
            _objNormalizer.PathReplacements.Add(new KeyValuePair<string, string>(s_strScratchDirectory, "<SCRATCH>"));
            _objNormalizer.PathReplacements.Add(new KeyValuePair<string, string>(Utils.GetStartupPath, "<APP>"));
        }

        private static string TestFilesDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestFiles");

        private static IEnumerable<FileInfo> TestFiles =>
            new DirectoryInfo(TestFilesDirectory).GetFiles("*.chum5").OrderBy(x => x.Name, StringComparer.Ordinal);

        [TestMethod, Timeout(30 * 60 * 1000, CooperativeCancellation = true)]
        public void Saves()
        {
            CancellationToken token = TestContext.CancellationToken;
            foreach (FileInfo objFile in TestFiles)
            {
                string strName = Path.GetFileNameWithoutExtension(objFile.Name);
                using (Character objCharacter = UnitTestUtils.LoadCharacter(objFile, token: token))
                {
                    string strSyncPath = Path.Combine(s_strScratchDirectory, strName + ".sync.chum5");
                    Assert.IsTrue(objCharacter.Save(strSyncPath, false, false, LzmaHelper.ChummerCompressionPreset.None, token),
                                  "Sync save failed for " + strName);
                    CheckXml(Path.Combine("saves", strName + ".sync.xml"), File.ReadAllText(strSyncPath, Encoding.UTF8));

                    string strAsyncPath = Path.Combine(s_strScratchDirectory, strName + ".async.chum5");
                    Assert.IsTrue(Utils.SafelyRunSynchronously(
                                      t => objCharacter.SaveAsync(strAsyncPath, false, false, LzmaHelper.ChummerCompressionPreset.None, t),
                                      token), "Async save failed for " + strName);
                    CheckXml(Path.Combine("saves", strName + ".async.xml"), File.ReadAllText(strAsyncPath, Encoding.UTF8));
                }
            }
            ReportFailures();
        }

        /// <summary>
        /// English print XML for every character, plus the JSON and Squad Manager exports and the default sheet.
        /// </summary>
        [TestMethod, Timeout(120 * 60 * 1000, CooperativeCancellation = true)]
        public void ExportsEnglish()
        {
            CancellationToken token = TestContext.CancellationToken;
            string strDefaultSheet = GlobalSettings.DefaultCharacterSheetDefaultValue;
            foreach (FileInfo objFile in TestFiles)
            {
                string strName = Path.GetFileNameWithoutExtension(objFile.Name);
                using (Character objCharacter = UnitTestUtils.LoadCharacter(objFile, token: token))
                {
                    XmlDocument xmlExport = GenerateExport(objCharacter, "en-us", token);
                    CheckXml(Path.Combine("exports", "en-us", strName + ".xml"), xmlExport.OuterXml);
                    CheckText(Path.Combine("exports", "json", strName + ".json"),
                              JsonConvert.SerializeXmlNode(_objNormalizer.CanonicalizeOrder(xmlExport), Newtonsoft.Json.Formatting.Indented));
                    CheckText(Path.Combine("exports", "squad-manager", strName + ".txt"),
                              Transform(Path.Combine(Utils.GetStartupPath, "export", "Squad Manager.xsl"), _objNormalizer.CanonicalizeOrder(xmlExport), token));
                    CheckText(Path.Combine("sheets", "en-us", strDefaultSheet, strName + ".html"),
                              Transform(Path.Combine(Utils.GetStartupPath, "sheets", strDefaultSheet + ".xsl"), _objNormalizer.CanonicalizeOrder(xmlExport), token));
                }
            }
            ReportFailures();
        }

        /// <summary>
        /// Print XML for every character in every non-English language.
        /// </summary>
        [TestMethod, Timeout(120 * 60 * 1000, CooperativeCancellation = true)]
        public void ExportsOtherLanguages()
        {
            CancellationToken token = TestContext.CancellationToken;
            foreach (FileInfo objFile in TestFiles)
            {
                string strName = Path.GetFileNameWithoutExtension(objFile.Name);
                using (Character objCharacter = UnitTestUtils.LoadCharacter(objFile, token: token))
                {
                    foreach (string strLanguage in s_astrLanguages.Where(x => x != "en-us"))
                    {
                        XmlDocument xmlExport = GenerateExport(objCharacter, strLanguage, token);
                        CheckXml(Path.Combine("exports", strLanguage, strName + ".xml"), xmlExport.OuterXml);
                    }
                }
            }
            ReportFailures();
        }

        /// <summary>
        /// Every sheet in every language for a few representative characters.
        /// </summary>
        [TestMethod, Timeout(120 * 60 * 1000, CooperativeCancellation = true)]
        public void SheetsAllLanguages()
        {
            CancellationToken token = TestContext.CancellationToken;
            foreach (string strName in s_astrFullSheetCharacters)
            {
                using (Character objCharacter = UnitTestUtils.LoadCharacter(new FileInfo(Path.Combine(TestFilesDirectory, strName + ".chum5")), token: token))
                {
                    foreach (string strLanguage in s_astrLanguages)
                    {
                        XmlDocument xmlExport = GenerateExport(objCharacter, strLanguage, token);
                        XmlDocument xmlCanonical = _objNormalizer.CanonicalizeOrder(xmlExport);
                        string strSheetDirectory = strLanguage == "en-us"
                            ? Path.Combine(Utils.GetStartupPath, "sheets")
                            : Path.Combine(Utils.GetStartupPath, "sheets", strLanguage);
                        foreach (string strSheet in Directory.GetFiles(strSheetDirectory, "*.xsl").Select(Path.GetFileNameWithoutExtension)
                                                             .OrderBy(x => x, StringComparer.Ordinal))
                        {
                            CheckText(Path.Combine("sheets", strLanguage, strSheet, strName + ".html"),
                                      Transform(Path.Combine(strSheetDirectory, strSheet + ".xsl"), xmlCanonical, token));
                        }
                    }
                }
            }
            ReportFailures();
        }

        [TestMethod, Timeout(10 * 60 * 1000, CooperativeCancellation = true)]
        public void MultiCharacterPrint()
        {
            CancellationToken token = TestContext.CancellationToken;
            List<Character> lstCharacters = new List<Character>(s_astrMultiPrintCharacters.Length);
            try
            {
                foreach (string strName in s_astrMultiPrintCharacters)
                    lstCharacters.Add(UnitTestUtils.LoadCharacter(new FileInfo(Path.Combine(TestFilesDirectory, strName + ".chum5")), token: token));
                CultureInfo objCulture = CultureInfo.GetCultureInfo("en-US");
                XmlDocument xmlExport = Utils.SafelyRunSynchronously(
                    t => CommonFunctions.GenerateCharactersExportXml(objCulture, "en-us", t, lstCharacters.ToArray()), token);
                CheckXml(Path.Combine("multi", string.Join("+", s_astrMultiPrintCharacters) + ".xml"), xmlExport.OuterXml);
            }
            finally
            {
                foreach (Character objCharacter in lstCharacters)
                    objCharacter.Dispose();
            }
            ReportFailures();
        }

        private static XmlDocument GenerateExport(Character objCharacter, string strLanguage, CancellationToken token)
        {
            CultureInfo objCulture;
            try
            {
                objCulture = CultureInfo.GetCultureInfo(strLanguage);
            }
            catch (CultureNotFoundException)
            {
                objCulture = CultureInfo.InvariantCulture;
            }
            return Utils.SafelyRunSynchronously(t => objCharacter.GenerateExportXml(objCulture, strLanguage, t), token);
        }

        /// <summary>Same transform settings as CharacterSheetViewer / ExportCharacter, minus the browser.</summary>
        private static string Transform(string strXslPath, XmlDocument xmlInput, CancellationToken token)
        {
            XslCompiledTransform objTransform = XslManager.GetTransformForFile(strXslPath, token);
            XmlWriterSettings objSettings = objTransform.OutputSettings?.Clone();
            if (objSettings != null)
            {
                objSettings.CheckCharacters = false;
                objSettings.ConformanceLevel = ConformanceLevel.Fragment;
            }
            using (MemoryStream objStream = new MemoryStream())
            {
                using (XmlWriter objWriter = objSettings != null
                           ? XmlWriter.Create(objStream, objSettings)
                           : Utils.GetXslTransformXmlWriter(objStream))
                    objTransform.Transform(xmlInput, objWriter);
                return Encoding.UTF8.GetString(objStream.ToArray());
            }
        }

        private void CheckXml(string strRelativePath, string strXml)
        {
            string strNormalized;
            try
            {
                strNormalized = _objNormalizer.NormalizeXml(strXml);
            }
            catch (XmlException ex)
            {
                _lstFailures.Add(strRelativePath + ": output is not well-formed XML (" + ex.Message + ")");
                return;
            }
            Check(strRelativePath, strNormalized);
        }

        private void CheckText(string strRelativePath, string strText)
        {
            Check(strRelativePath, _objNormalizer.NormalizeText(strText));
        }

        private void Check(string strRelativePath, string strNormalized)
        {
            string strGoldenPath = Path.Combine(GoldenDirectory, strRelativePath);
            if (IsCaptureMode)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(strGoldenPath) ?? GoldenDirectory);
                File.WriteAllText(strGoldenPath, strNormalized, new UTF8Encoding(false));
                return;
            }

            if (!File.Exists(strGoldenPath))
            {
                _lstFailures.Add(strRelativePath + ": no golden file (run with CHUMMER_PARITY_MODE=capture on the reference engine)");
                return;
            }

            // A transform can emit U+FEFF as part of its captured string. StreamReader
            // would silently consume those bytes as a file preamble on verification.
            string strExpected = Encoding.UTF8.GetString(File.ReadAllBytes(strGoldenPath));
            if (string.Equals(strExpected, strNormalized, StringComparison.Ordinal))
                return;

            string strActualPath = Path.Combine(ActualDirectory, strRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(strActualPath) ?? ActualDirectory);
            File.WriteAllText(strActualPath, strNormalized, new UTF8Encoding(false));
            _lstFailures.Add(strRelativePath + ": differs at line " + FirstDifferingLine(strExpected, strNormalized).ToString(CultureInfo.InvariantCulture));
        }

        private static int FirstDifferingLine(string strExpected, string strActual)
        {
            string[] astrExpected = strExpected.Split('\n');
            string[] astrActual = strActual.Split('\n');
            int intCount = Math.Min(astrExpected.Length, astrActual.Length);
            for (int i = 0; i < intCount; ++i)
            {
                if (!string.Equals(astrExpected[i], astrActual[i], StringComparison.Ordinal))
                    return i + 1;
            }
            return intCount + 1;
        }

        private void ReportFailures()
        {
            if (_lstFailures.Count == 0)
                return;
            const int intShown = 50;
            string strMessage = _lstFailures.Count.ToString(CultureInfo.InvariantCulture) + " parity mismatch(es); actual output written to "
                                + ActualDirectory + Environment.NewLine
                                + string.Join(Environment.NewLine, _lstFailures.Take(intShown));
            if (_lstFailures.Count > intShown)
                strMessage += Environment.NewLine + "...";
            Assert.Fail(strMessage);
        }
    }
}
