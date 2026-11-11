using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

// Prepare a separate character copy, touching only contact portrait XML. Never overwrite the input.
if (args.Length != 3) throw new ArgumentException("Usage: ContactArt input.chum5 artwork-directory output.chum5");
string input = Path.GetFullPath(args[0]), directory = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
if (input == output || File.Exists(output)) throw new IOException("Choose a new output file; existing characters are never overwritten.");
byte[] originalBytes = File.ReadAllBytes(input);
string original = File.ReadAllText(input);
using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "prompts.json")));
var portraits = manifest.RootElement.EnumerateArray().ToDictionary(x => Guid.Parse(x.GetProperty("id").GetString()!), x => x.GetProperty("slug").GetString()!);
var applied = new HashSet<Guid>();
string result = Regex.Replace(original, @"<contact>.*?</contact>", match =>
{
    var contact = XElement.Parse(match.Value, LoadOptions.PreserveWhitespace);
    if (!Guid.TryParse(contact.Element("guid")?.Value, out var id) || !portraits.TryGetValue(id, out string? slug)) return match.Value;
    if (!applied.Add(id)) throw new InvalidDataException("Duplicate contact identifier: " + id);
    byte[] portrait = File.ReadAllBytes(Path.Combine(directory, slug + ".png"));
    if (!portrait.AsSpan().StartsWith(new byte[] {137,80,78,71,13,10,26,10})) throw new InvalidDataException("Not a PNG: " + slug);
    int oldCount = contact.Element("mugshots")?.Elements("mugshot").Count() ?? 0;
    string block = match.Value;
    string node = "<mugshot>" + Convert.ToBase64String(portrait) + "</mugshot>";
    if (Regex.IsMatch(block, @"<mugshots\s*/>")) block = Regex.Replace(block, @"<mugshots\s*/>", "<mugshots>" + node + "</mugshots>");
    else if (block.Contains("</mugshots>", StringComparison.Ordinal)) block = block.Replace("</mugshots>", node + "</mugshots>", StringComparison.Ordinal);
    else block = block.Replace("</contact>", "<mugshots>" + node + "</mugshots></contact>", StringComparison.Ordinal);
    if (Regex.IsMatch(block, @"<mainmugshotindex\b")) block = Regex.Replace(block, @"<mainmugshotindex(?:\s*/>|>.*?</mainmugshotindex>)", "<mainmugshotindex>" + oldCount + "</mainmugshotindex>");
    else block = block.Replace("</contact>", "<mainmugshotindex>" + oldCount + "</mainmugshotindex></contact>", StringComparison.Ordinal);
    Console.WriteLine(contact.Element("name")?.Value + " → " + slug);
    return block;
}, RegexOptions.Singleline);
if (applied.Count != portraits.Count) throw new InvalidDataException("Not all portrait contact identifiers were found.");
var before = XDocument.Parse(original, LoadOptions.PreserveWhitespace);
var after = XDocument.Parse(result, LoadOptions.PreserveWhitespace);
foreach (var contact in after.Descendants("contact"))
{
    if (!Guid.TryParse(contact.Element("guid")?.Value, out var id) || !applied.Contains(id)) continue;
    var source = before.Descendants("contact").Single(c => c.Element("guid")?.Value == contact.Element("guid")?.Value);
    foreach (string name in new[] { "mugshots", "mainmugshotindex" })
    {
        contact.Element(name)?.Remove();
        source.Element(name)?.Remove();
    }
}
if (!XNode.DeepEquals(before, after)) throw new InvalidDataException("Unexpected non-portrait character changes; output was not written.");
File.WriteAllText(output, result, new UTF8Encoding(originalBytes.AsSpan().StartsWith(new byte[] {239,187,191})));
File.WriteAllText(output + ".source-sha256", Convert.ToHexString(SHA256.HashData(originalBytes)));
Console.WriteLine("Verified " + applied.Count + " contact portraits; all other XML unchanged. Prepared: " + output);
