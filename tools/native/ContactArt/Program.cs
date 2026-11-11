using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

// Prepare a separate character copy, touching only contact portrait XML. Never overwrite the input.
if (args.Length < 3 || args.Length > 4) throw new ArgumentException("Usage: ContactArt input.chum5 artwork-directory output.chum5 [previous-artwork-directory | --replace-main]");
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
    var oldImages = contact.Element("mugshots")?.Elements("mugshot").ToArray() ?? Array.Empty<XElement>();
    string encoded = Convert.ToBase64String(portrait);
    int selected = Array.FindIndex(oldImages, image => image.Value == encoded);
    bool replaceMain = args.Length == 4 && args[3] == "--replace-main";
    string previousPath = args.Length == 4 && !replaceMain ? Path.Combine(Path.GetFullPath(args[3]), slug + ".png") : "";
    int replace = selected < 0 && File.Exists(previousPath)
        ? Array.FindIndex(oldImages, image => image.Value == Convert.ToBase64String(File.ReadAllBytes(previousPath))) : -1;
    if (selected < 0 && replaceMain && int.TryParse(contact.Element("mainmugshotindex")?.Value, out int main) && main >= 0 && main < oldImages.Length) replace = main;
    if (selected < 0) selected = replace >= 0 ? replace : oldImages.Length;
    string block = match.Value;
    string node = "<mugshot>" + encoded + "</mugshot>";
    if (replace >= 0) block = block.Replace(oldImages[replace].ToString(SaveOptions.DisableFormatting), node, StringComparison.Ordinal);
    else if (selected < oldImages.Length) { /* Already installed: retain the existing image. */ }
    else if (Regex.IsMatch(block, @"<mugshots\s*/>")) block = Regex.Replace(block, @"<mugshots\s*/>", "<mugshots>" + node + "</mugshots>");
    else if (block.Contains("</mugshots>", StringComparison.Ordinal)) block = block.Replace("</mugshots>", node + "</mugshots>", StringComparison.Ordinal);
    else block = block.Replace("</contact>", "<mugshots>" + node + "</mugshots></contact>", StringComparison.Ordinal);
    if (Regex.IsMatch(block, @"<mainmugshotindex\b")) block = Regex.Replace(block, @"<mainmugshotindex(?:\s*/>|>.*?</mainmugshotindex>)", "<mainmugshotindex>" + selected + "</mainmugshotindex>");
    else block = block.Replace("</contact>", "<mainmugshotindex>" + selected + "</mainmugshotindex></contact>", StringComparison.Ordinal);
    var updated = XElement.Parse(block);
    if (updated.Element("mugshots")?.Elements("mugshot").ElementAt(selected).Value != encoded)
        throw new InvalidDataException("Portrait replacement failed: " + slug);
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
