using System.Globalization;
using System.Text;
using System.Xml;
using Chummer;
using Chummer.Tests.Parity;
using Newtonsoft.Json;
using Chummer.ParityVerification;

// Incremental verification against a Windows capture. Never writes to the golden directory.
// Usage: ParityVerify --golden DIR --output DIR [--mode saves|exports|sheets|all]
//                    [--characters 'Bastion|Wesson'] [--languages en-us,de-de] [--multi]
internal static class Program
{
    private static readonly string[] Languages = { "en-us", "de-de", "fr-fr", "ja-jp", "pt-br", "zh-cn" };
    private static readonly List<object> Failures = new();
    private static readonly ParityNormalizer Normalizer = new();
    private static string _golden, _output, _scratch;
    private static int _checked;
    private static readonly HashSet<string> CheckedPaths = new(StringComparer.Ordinal);
    private static readonly List<string> EquivalentSaves = new();
    private static readonly List<string> EquivalentExports = new();

    private static async Task<int> Main(string[] args)
    {
        string Option(string key, string fallback = null)
        {
            int index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }
        _golden = Option("--golden"); _output = Option("--output");
        if (_golden == null || _output == null)
        { Console.Error.WriteLine("Required: --golden DIR --output DIR; optional --mode saves|exports|sheets|all, --characters names joined with |, --languages codes joined with comma, --multi"); return 2; }
        _golden = Path.GetFullPath(_golden); _output = Path.GetFullPath(_output);
        if (_golden == _output || _output.StartsWith(_golden + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Actual output must be outside the Windows golden directory.");
        string mode = Option("--mode", "saves");
        if (!new[] { "saves", "exports", "sheets", "all" }.Contains(mode)) throw new ArgumentException("Unknown verification mode.");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        Utils.IsUnitTest = true; Utils.IsUnitTestForUI = false; Utils.CreateSynchronizationContext();
        string fixtures = Path.Combine(AppContext.BaseDirectory, "TestFiles");
        _scratch = Path.Combine(_output, "scratch"); Directory.CreateDirectory(_scratch);
        Normalizer.PathReplacements.Add(new KeyValuePair<string, string>(fixtures, "<TESTFILES>"));
        Normalizer.PathReplacements.Add(new KeyValuePair<string, string>(_scratch, "<SCRATCH>"));
        Normalizer.PathReplacements.Add(new KeyValuePair<string, string>(Utils.GetStartupPath, "<APP>"));
        if (args.Contains("--compare-existing"))
        {
            string actualDirectory = Path.Combine(_output, "Actual");
            foreach (string file in Directory.EnumerateFiles(actualDirectory, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
                Check(Path.GetRelativePath(actualDirectory, file), Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file)));
            await WriteReportAsync(mode + ":existing-actual");
            Console.WriteLine("RESULT: " + _checked + " retained outputs compared; " + Failures.Count + " mismatches/errors.");
            return Failures.Count == 0 ? 0 : 1;
        }
        var selected = Option("--characters")?.Split('|').ToHashSet(StringComparer.Ordinal);
        var languages = Option("--languages")?.Split(',') ?? Languages;
        var files = Directory.GetFiles(fixtures, "*.chum5").OrderBy(x => Path.GetFileName(x), StringComparer.Ordinal)
            .Where(x => selected == null || selected.Contains(Path.GetFileNameWithoutExtension(x)))
            .Where(x => Option("--after") == null || StringComparer.Ordinal.Compare(Path.GetFileNameWithoutExtension(x), Option("--after")) > 0).ToList();
        if (files.Count == 0) throw new ArgumentException("No matching character fixtures.");
        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            int before = Failures.Count; int checkedBefore = _checked;
            Console.WriteLine("START " + name); Console.Out.Flush();
            try
            {
                if (mode == "saves" || mode == "all")
                {
                    using var character = Load(file);
                    string sync = Path.Combine(_scratch, name + ".sync.chum5");
                    if (!character.Save(sync, false, false, LzmaHelper.ChummerCompressionPreset.None)) throw new IOException("Synchronous save failed.");
                    CheckXml(Path.Combine("saves", name + ".sync.xml"), await File.ReadAllTextAsync(sync));
                    string asyncPath = Path.Combine(_scratch, name + ".async.chum5");
                    if (!await character.SaveAsync(asyncPath, false, false, LzmaHelper.ChummerCompressionPreset.None)) throw new IOException("Asynchronous save failed.");
                    CheckXml(Path.Combine("saves", name + ".async.xml"), await File.ReadAllTextAsync(asyncPath));
                }
                if (mode != "saves")
                {
                    using var character = Load(file);
                    foreach (string language in languages)
                    {
                        Console.WriteLine("EXPORT " + name + " " + language); Console.Out.Flush();
                        var xml = await character.GenerateExportXml(CultureInfo.GetCultureInfo(language), language);
                        var canonical = Normalizer.CanonicalizeOrder(xml);
                        if (mode == "exports" || mode == "all")
                        {
                            CheckXml(Path.Combine("exports", language, name + ".xml"), xml.OuterXml);
                            if (language == "en-us")
                            {
                                CheckText(Path.Combine("exports", "json", name + ".json"), JsonConvert.SerializeXmlNode(canonical, Newtonsoft.Json.Formatting.Indented));
                                CheckText(Path.Combine("exports", "squad-manager", name + ".txt"), await TransformAsync(Path.Combine(Utils.GetStartupPath, "export", "Squad Manager.xsl"), canonical));
                                string sheet = GlobalSettings.DefaultCharacterSheetDefaultValue;
                                CheckText(Path.Combine("sheets", "en-us", sheet, name + ".html"), await TransformAsync(Path.Combine(Utils.GetStartupPath, "sheets", sheet + ".xsl"), canonical));
                            }
                        }
                        if (mode == "sheets" || mode == "all")
                        {
                            string directory = Path.Combine(_golden, "sheets", language);
                            if (!Directory.Exists(directory)) continue;
                            foreach (string golden in Directory.EnumerateFiles(directory, name + ".html", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
                            {
                                string sheet = Path.GetFileName(Path.GetDirectoryName(golden));
                                string sheetDirectory = language == "en-us" ? Path.Combine(Utils.GetStartupPath, "sheets") : Path.Combine(Utils.GetStartupPath, "sheets", language);
                                CheckText(Path.GetRelativePath(_golden, golden), await TransformAsync(Path.Combine(sheetDirectory, sheet + ".xsl"), canonical));
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Failures.Add(new { file = name, error = ex.ToString() }); Console.WriteLine("ERROR " + name + ": " + ex.Message); }
            Console.WriteLine("DONE " + name + ": " + (_checked - checkedBefore) + " checked, " + (Failures.Count - before) + " mismatches/errors"); Console.Out.Flush();
            await WriteReportAsync(mode);
        }
        if (args.Contains("--multi"))
        {
            var characters = new[] { "Barrett", "Draught", "Miko" }.Select(name => Load(Path.Combine(fixtures, name + ".chum5"))).ToArray();
            try
            {
                var xml = await CommonFunctions.GenerateCharactersExportXml(CultureInfo.GetCultureInfo("en-US"), "en-us", default, characters);
                CheckXml(Path.Combine("multi", "Barrett+Draught+Miko.xml"), xml.OuterXml);
            }
            finally { foreach (var character in characters) character.Dispose(); }
        }
        await WriteReportAsync(mode);
        Console.WriteLine("RESULT: " + _checked + " outputs checked; " + Failures.Count + " mismatches/errors. Report: " + Path.Combine(_output, "report.json"));
        return Failures.Count == 0 ? 0 : 1;
    }
    private static Character Load(string file)
    {
        var character = new Character { FileName = file };
        try { if (!character.Load()) throw new IOException("Could not load fixture: " + file); return character; }
        catch { character.Dispose(); throw; }
    }
    private static async Task<string> TransformAsync(string path, XmlDocument input)
    {
        var transform = await XslManager.GetTransformForFileAsync(path);
        var settings = transform.OutputSettings?.Clone();
        if (settings != null) { settings.CheckCharacters = false; settings.ConformanceLevel = ConformanceLevel.Fragment; }
        using var stream = new MemoryStream();
        using (var writer = settings == null ? Utils.GetXslTransformXmlWriter(stream) : XmlWriter.Create(stream, settings)) transform.Transform(input, writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    private static void CheckXml(string path, string xml) => Check(path, Normalizer.NormalizeXml(xml));
    private static void CheckText(string path, string text) => Check(path, Normalizer.NormalizeText(text));
    private static void Check(string path, string actual)
    {
        ++_checked;
        CheckedPaths.Add(path.Replace('\\', '/'));
        string destination = Path.Combine(_output, "Actual", path); Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.WriteAllText(destination, actual, new UTF8Encoding(false));
        string golden = Path.Combine(_golden, path);
        if (!File.Exists(golden)) { Failures.Add(new { file = path, error = "Missing Windows reference" }); return; }
        // Captured transform strings may contain an explicit U+FEFF. Preserve it
        // rather than having StreamReader silently consume it as a byte preamble.
        string expected = Encoding.UTF8.GetString(File.ReadAllBytes(golden));
        if (expected == actual) return;
        if (path.StartsWith("saves" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            string canonicalExpected = SaveComparison.Canonicalize(expected), canonicalActual = SaveComparison.Canonicalize(actual);
            if (canonicalExpected == canonicalActual)
            { EquivalentSaves.Add(path); Console.WriteLine("EQUIVALENT " + path + " (attribute order / carried-weight decimal formatting; raw actual retained)"); return; }
            expected = canonicalExpected; actual = canonicalActual;
        }
        else if (path.StartsWith("exports" + Path.DirectorySeparatorChar, StringComparison.Ordinal) && (path.EndsWith(".xml", StringComparison.Ordinal) || path.EndsWith(".json", StringComparison.Ordinal)))
        {
            bool json = path.EndsWith(".json", StringComparison.Ordinal);
            string canonicalExpected = json ? ExportComparison.CanonicalizeJson(expected) : ExportComparison.CanonicalizeXml(expected);
            string canonicalActual = json ? ExportComparison.CanonicalizeJson(actual) : ExportComparison.CanonicalizeXml(actual);
            if (canonicalExpected == canonicalActual)
            { EquivalentExports.Add(path); Console.WriteLine("EQUIVALENT " + path + " (keyed attribute order / JSON portrait encoding; raw actual retained)"); return; }
            expected = canonicalExpected; actual = canonicalActual;
        }
        string[] oldLines = expected.Split('\n'), newLines = actual.Split('\n'); int line = 0;
        while (line < Math.Min(oldLines.Length, newLines.Length) && oldLines[line] == newLines[line]) ++line;
        string oldLine = line < oldLines.Length ? oldLines[line] : "<EOF>", newLine = line < newLines.Length ? newLines[line] : "<EOF>";
        int column = 0; while (column < Math.Min(oldLine.Length, newLine.Length) && oldLine[column] == newLine[column]) ++column;
        string Snippet(string value) { int start = Math.Max(0, Math.Min(value.Length, column) - 40); return value.Substring(start, Math.Min(180, value.Length - start)); }
        Failures.Add(new { file = path, line = line + 1, column = column + 1, expected = Snippet(oldLine), actual = Snippet(newLine), expectedLineLength = oldLine.Length, actualLineLength = newLine.Length });
        Console.WriteLine("DIFF " + path + ":" + (line + 1));
    }
    private static Task WriteReportAsync(string mode) => File.WriteAllTextAsync(Path.Combine(_output, "report.json"), JsonConvert.SerializeObject(new
    { mode, golden = _golden, engine = typeof(Character).Assembly.GetName().Version?.ToString(), checkedOutputs = _checked,
      distinctOutputs = CheckedPaths.Count, equivalentSaves = EquivalentSaves, equivalentExports = EquivalentExports,
      uncheckedReferenceOutputs = Directory.EnumerateFiles(_golden, "*", SearchOption.AllDirectories).Select(x => Path.GetRelativePath(_golden, x).Replace('\\', '/')).Where(x => !CheckedPaths.Contains(x)).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
      failureCount = Failures.Count, failures = Failures }, Newtonsoft.Json.Formatting.Indented));
}
