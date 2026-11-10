/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Chummer.Backend.Equipment;
using Chummer.Backend.Enums;

namespace Chummer.Rules
{
    public sealed class CyberwareSuiteQuote
    {
        public string Name { get; set; }
        public string Grade { get; set; }
        public decimal Cost { get; set; }
        public decimal Essence { get; set; }
        public List<string> Items { get; } = new List<string>();
    }

    public static class CyberwareSuiteRules
    {
        public static XmlDocument Append(XmlDocument existing, XmlDocument generated)
        {
            if (existing?.SelectSingleNode("/chummer/suites") == null || generated?.SelectSingleNode("/chummer/suites/suite") == null)
                throw new ArgumentException("Select a suites XML file.");
            var result = (XmlDocument)existing.CloneNode(true); var suites = result.SelectSingleNode("/chummer/suites");
            foreach (XmlNode suite in generated.SelectNodes("/chummer/suites/suite"))
            {
                if (suites.SelectSingleNode("suite[name=" + (suite["name"]?.InnerText ?? "").CleanXPath() + "]") != null)
                    throw new ArgumentException("A suite with this name already exists in the selected file.");
                suites.AppendChild(result.ImportNode(suite, true));
            }
            return result;
        }

        private static string Type(Improvement.ImprovementSource source)
        {
            if (source == Improvement.ImprovementSource.Cyberware) return "cyberware";
            if (source == Improvement.ImprovementSource.Bioware) return "bioware";
            throw new ArgumentException("Select cyberware or bioware.", nameof(source));
        }

        public static async Task<XmlDocument> ExportAsync(Character character, string name, Improvement.ImprovementSource source, CancellationToken token = default)
        {
            string type = Type(source);
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a suite name.", nameof(name));
            var roots = (await character.Cyberware.ToListAsync(token).ConfigureAwait(false)).Where(x => x.SourceType == source).ToList();
            if (roots.Count == 0) throw new ArgumentException("The character has no implants of this type.");
            string grade = (await roots[0].GetGradeAsync(token).ConfigureAwait(false)).Name;
            var document = new XmlDocument { XmlResolver = null };
            var root = document.CreateElement("chummer"); document.AppendChild(root);
            var suites = document.CreateElement("suites"); root.AppendChild(suites);
            var suite = document.CreateElement("suite"); suites.AppendChild(suite);
            void Field(XmlNode parent, string key, string value) { var field = document.CreateElement(key); field.InnerText = value; parent.AppendChild(field); }
            Field(suite, "id", Guid.NewGuid().ToString()); Field(suite, "name", name.Trim()); Field(suite, "grade", grade);
            async Task WriteAsync(XmlNode parent, IEnumerable<Cyberware> items)
            {
                var collection = document.CreateElement(type + "s"); parent.AppendChild(collection);
                foreach (var item in items)
                {
                    token.ThrowIfCancellationRequested();
                    if (item.Capacity == "[*]") continue;
                    if ((await item.GetGradeAsync(token).ConfigureAwait(false)).Name != grade) throw new ArgumentException("All implants in a suite must use the same grade.");
                    var node = document.CreateElement(type); collection.AppendChild(node); Field(node, "name", item.Name);
                    if (!string.IsNullOrEmpty(item.Extra)) ((XmlElement)node["name"]).SetAttribute("select", item.Extra);
                    int rating = await item.GetRatingAsync(token).ConfigureAwait(false);
                    if (rating > 0) Field(node, "rating", rating.ToString(CultureInfo.InvariantCulture));
                    var children = await (await item.GetChildrenAsync(token).ConfigureAwait(false)).ToListAsync(token).ConfigureAwait(false);
                    if (children.Count > 0) await WriteAsync(node, children).ConfigureAwait(false);
                }
            }
            await WriteAsync(suite, roots).ConfigureAwait(false); return document;
        }

        private static async Task<Grade> GradeAsync(Character character, XmlNode suite, Improvement.ImprovementSource source, CancellationToken token)
        {
            if (character.IsAI || (source == Improvement.ImprovementSource.Cyberware ? !character.AddCyberwareEnabled : !character.AddBiowareEnabled)) return null;
            string name = suite?["grade"]?.InnerText ?? "Standard";
            var grades = await character.GetGradesListAsync(source, token: token).ConfigureAwait(false);
            var grade = grades.FirstOrDefault(x => x.Name == name);
            if (grade == null) return null;
            var disabled = await ImprovementManager.GetCachedImprovementListForValueOfAsync(character,
                source == Improvement.ImprovementSource.Cyberware ? Improvement.ImprovementType.DisableCyberwareGrade : Improvement.ImprovementType.DisableBiowareGrade, token: token).ConfigureAwait(false);
            return name.ContainsAny(disabled.Select(x => x.ImprovedName)) ? null : grade;
        }

        private static async Task<Cyberware> CreateAsync(Character character, XmlDocument data, XmlNode item, Grade grade,
            Improvement.ImprovementSource source, Cyberware parent, bool preview, List<Weapon> weapons, List<Vehicle> vehicles, CancellationToken token)
        {
            string type = Type(source);
            var definition = data.TryGetNodeByNameOrId("/chummer/" + type + "s/" + type, item["name"]?.InnerText ?? "");
            if (definition == null) throw new ArgumentException("An implant in this suite is missing from the game data: " + item["name"]?.InnerText);
            int.TryParse(item["rating"]?.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rating);
            var implant = new Cyberware(character);
            try
            {
                await implant.CreateAsync(definition, grade, source, rating, weapons, vehicles, !preview, true,
                    item.SelectSingleNode("name/@select")?.Value ?? "", parent, blnSkipSelectForms: preview, token: token).ConfigureAwait(false);
                if (implant.InternalId.IsEmptyGuid()) throw new OperationCanceledException(token);
                implant.Suite = true;
                foreach (XmlNode child in item.SelectNodes(type + "s/" + type))
                    await implant.Children.AddAsync(await CreateAsync(character, data, child, grade, source, implant, preview, weapons, vehicles, token).ConfigureAwait(false), token).ConfigureAwait(false);
                return implant;
            }
            catch { if (preview) await implant.DisposeAsync().ConfigureAwait(false); else await implant.DeleteCyberwareAsync(blnIncreaseEssenceHole: false, token: CancellationToken.None).ConfigureAwait(false); throw; }
        }

        public static async Task<CyberwareSuiteQuote> QuoteAsync(Character character, XmlNode suite, Improvement.ImprovementSource source, CancellationToken token = default)
        {
            string type = Type(source); var grade = await GradeAsync(character, suite, source, token).ConfigureAwait(false);
            if (grade == null || suite == null) return null;
            var data = await character.LoadDataAsync(type + ".xml", token: token).ConfigureAwait(false);
            var roots = new List<Cyberware>(); var weapons = new List<Weapon>(); var vehicles = new List<Vehicle>();
            try
            {
                var quote = new CyberwareSuiteQuote { Name = suite["name"]?.InnerText, Grade = grade.Name };
                async Task DescribeAsync(Cyberware implant, int depth)
                {
                    quote.Items.Add(new string(' ', depth * 2) + await implant.GetCurrentDisplayNameAsync(token).ConfigureAwait(false));
                    foreach (var child in await implant.Children.ToListAsync(token).ConfigureAwait(false)) await DescribeAsync(child, depth + 1).ConfigureAwait(false);
                }
                foreach (XmlNode item in suite.SelectNodes(type + "s/" + type)) roots.Add(await CreateAsync(character, data, item, grade, source, null, true, weapons, vehicles, token).ConfigureAwait(false));
                if (roots.Count == 0) return null;
                foreach (var implant in roots)
                {
                    quote.Cost += await implant.GetTotalCostAsync(token).ConfigureAwait(false);
                    quote.Essence += await implant.GetCalculatedESSAsync(token).ConfigureAwait(false);
                    await DescribeAsync(implant, 0).ConfigureAwait(false);
                }
                return quote;
            }
            finally
            {
                foreach (var implant in roots) await implant.DisposeAsync().ConfigureAwait(false);
                foreach (var weapon in weapons) await weapon.DisposeAsync().ConfigureAwait(false);
                foreach (var vehicle in vehicles) await vehicle.DisposeAsync().ConfigureAwait(false);
            }
        }

        public static async Task<RuleResult> PurchaseAsync(Character character, XmlNode suite, Improvement.ImprovementSource source, CancellationToken token = default)
        {
            string type = Type(source); var grade = await GradeAsync(character, suite, source, token).ConfigureAwait(false);
            if (suite == null || grade == null) return RuleResult.NotAllowed;
            var data = await character.LoadDataAsync(type + ".xml", token: token).ConfigureAwait(false);
            var roots = new List<Cyberware>(); var weapons = new List<Weapon>(); var vehicles = new List<Vehicle>(); bool committed = false;
            try
            {
                foreach (XmlNode item in suite.SelectNodes(type + "s/" + type)) roots.Add(await CreateAsync(character, data, item, grade, source, null, false, weapons, vehicles, token).ConfigureAwait(false));
                if (roots.Count == 0) return RuleResult.NotAllowed;
                decimal cost = 0; foreach (var implant in roots) cost += await implant.GetTotalCostAsync(token).ConfigureAwait(false);
                bool career = await character.GetCreatedAsync(token).ConfigureAwait(false);
                if (career && !await PurchaseRules.CheckNuyenAsync(character, cost, token).ConfigureAwait(false)) return RuleResult.NotEnoughNuyen;
                foreach (var implant in roots) await character.Cyberware.AddAsync(implant, token).ConfigureAwait(false);
                foreach (var weapon in weapons) await character.Weapons.AddAsync(weapon, token).ConfigureAwait(false);
                foreach (var vehicle in vehicles) await character.Vehicles.AddAsync(vehicle, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (career)
                {
                    var expense = new ExpenseLogEntry(character);
                    expense.Create(-cost, await LanguageManager.GetStringAsync("String_ExpensePurchaseCyberwareSuite", token: token).ConfigureAwait(false) + " " + suite["name"]?.InnerText, ExpenseType.Nuyen, DateTime.Now);
                    await character.ExpenseEntries.AddWithSortAsync(expense, token: CancellationToken.None).ConfigureAwait(false);
                    await character.ModifyNuyenAsync(-cost, CancellationToken.None).ConfigureAwait(false);
                }
                committed = true; return RuleResult.Changed;
            }
            finally
            {
                if (!committed)
                {
                    foreach (var implant in roots) await implant.DeleteCyberwareAsync(blnIncreaseEssenceHole: false, token: CancellationToken.None).ConfigureAwait(false);
                    foreach (var weapon in weapons) await weapon.DeleteWeaponAsync(token: CancellationToken.None).ConfigureAwait(false);
                    foreach (var vehicle in vehicles) await vehicle.DeleteVehicleAsync(token: CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
    }
}
