/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Chummer.Backend.Equipment;
using Chummer.Backend.Enums;

namespace Chummer.Rules
{
    public sealed class CustomDrugIngredient
    {
        public string ComponentId { get; set; }
        public int Level { get; set; }
    }
    public sealed class CustomDrugDesign
    {
        public string Name { get; set; }
        public string GradeId { get; set; }
        public bool Free { get; set; }
        public decimal Markup { get; set; }
        public List<CustomDrugIngredient> Ingredients { get; } = new List<CustomDrugIngredient>();
    }
    public sealed class CustomDrugQuote
    {
        public decimal Cost { get; set; }
        public string Description { get; set; }
        public string Availability { get; set; }
    }
    public static class CustomDrugRules
    {
        public static async Task<RuleResult> BuyAdditionalDoseAsync(Character character, Drug drug, CancellationToken token = default)
        {
            if (drug == null || !ReferenceEquals(drug.CharacterObject, character) || !await character.Drugs.ContainsAsync(drug, token).ConfigureAwait(false)) return RuleResult.NotAllowed;
            bool career = await character.GetCreatedAsync(token).ConfigureAwait(false);
            decimal cost = 0;
            if (career)
            {
                cost = await PurchaseRules.StreetCostAsync(character, await drug.GetCostAsync(token).ConfigureAwait(false), 0,
                    (await drug.TotalAvailTupleAsync(token: token).ConfigureAwait(false)).Suffix, token).ConfigureAwait(false);
                if (!await PurchaseRules.CheckNuyenAsync(character, cost, token).ConfigureAwait(false)) return RuleResult.NotEnoughNuyen;
            }
            token.ThrowIfCancellationRequested();
            await drug.GenerateImprovementsAsync(CancellationToken.None).ConfigureAwait(false);
            if (career) await PurchaseRules.ChargeAsync(character, cost, "String_ExpensePurchaseDrug", await drug.GetCurrentDisplayNameShortAsync(CancellationToken.None).ConfigureAwait(false),
                NuyenExpenseType.AddDrug, drug.InternalId, 1, CancellationToken.None).ConfigureAwait(false);
            drug.Quantity += 1;
            return RuleResult.Changed;
        }

        public static async Task<IReadOnlyList<DrugComponent>> ComponentsAsync(Character character, CancellationToken token = default)
        {
            var document = await character.LoadDataAsync(DrugsData.ComponentsFileName, token: token).ConfigureAwait(false);
            var components = new List<DrugComponent>();
            foreach (XmlNode node in document.SelectNodes(DrugsData.ComponentXPath))
            {
                var component = new DrugComponent(character); await component.LoadAsync(node, token).ConfigureAwait(false);
                if (!string.Equals(component.Category, "BTLs", StringComparison.OrdinalIgnoreCase)) components.Add(component);
            }
            return components.OrderBy(x => x.Category).ThenBy(x => x.Name).ToList();
        }

        private static async Task<Drug> BuildAsync(Character character, CustomDrugDesign design, CancellationToken token)
        {
            if (design == null || string.IsNullOrWhiteSpace(design.Name) || design.Markup < -100) throw new ArgumentException("Enter a drug name and valid markup.");
            var catalog = await ComponentsAsync(character, token).ConfigureAwait(false);
            var selected = new List<DrugComponent>();
            foreach (var ingredient in design.Ingredients)
            {
                var definition = catalog.FirstOrDefault(x => x.SourceIDString == ingredient.ComponentId || x.Name == ingredient.ComponentId);
                if (definition == null || ingredient.Level < 0 || ingredient.Level >= definition.DrugEffects.Count) throw new ArgumentException("Select an available component and effect level.");
                if (definition.Limit > 0 && selected.Count(x => x.Name == definition.Name) >= definition.Limit) throw new ArgumentException("This component has reached its recipe limit: " + definition.Name);
                var component = definition.Clone(); component.Level = ingredient.Level; selected.Add(component);
            }
            var foundations = selected.Where(x => x.Category == "Foundation").ToList();
            if (foundations.Count != 1) throw new ArgumentException("A custom drug needs exactly one foundation.");
            foreach (var component in selected.Where(x => x.Level >= 2))
                foreach (var attribute in foundations[0].DrugEffects[0].Attributes)
                    if (attribute.Value < 0 && component.DrugEffects[component.Level].Attributes.TryGetValue(attribute.Key, out decimal value) && value > 0)
                        throw new ArgumentException("This foundation limits the " + attribute.Key + " block to level 2.");
            var grades = await character.GetGradesListAsync(Improvement.ImprovementSource.Drug, token: token).ConfigureAwait(false);
            var grade = string.IsNullOrEmpty(design.GradeId) ? grades.FirstOrDefault(x => x.Name != "None") : grades.FirstOrDefault(x => x.SourceIDString == design.GradeId || x.Name == design.GradeId);
            // Legacy custom recipes may omit a grade when no grade sourcebook is enabled.
            if (grade == null && !string.IsNullOrEmpty(design.GradeId)) throw new ArgumentException("Select an available drug grade.");
            if (design.Free && !await character.GetCreatedAsync(token).ConfigureAwait(false))
                foreach (var component in selected) component.Cost = "0";
            var drug = new Drug(character) { Category = "Custom Drugs", Grade = grade, Quantity = 1 };
            try
            {
                await drug.SetNameAsync(design.Name.Trim(), token).ConfigureAwait(false);
                foreach (var component in selected) await drug.Components.AddAsync(component, token).ConfigureAwait(false);
                return drug;
            }
            catch { await drug.DisposeAsync().ConfigureAwait(false); throw; }
        }

        public static async Task<CustomDrugQuote> QuoteAsync(Character character, CustomDrugDesign design, CancellationToken token = default)
        {
            var drug = await BuildAsync(character, design, token).ConfigureAwait(false);
            try
            {
                decimal cost = await drug.GetCostAsync(token).ConfigureAwait(false);
                var availability = await drug.TotalAvailTupleAsync(token: token).ConfigureAwait(false);
                if (await character.GetCreatedAsync(token).ConfigureAwait(false)) cost = await PurchaseRules.StreetCostAsync(character, cost, design.Markup, availability.Suffix, token).ConfigureAwait(false);
                return new CustomDrugQuote { Cost = design.Free ? 0 : cost, Availability = await drug.GetDisplayTotalAvailAsync(token).ConfigureAwait(false),
                    Description = await drug.GenerateDescriptionAsync(0, token: token).ConfigureAwait(false) };
            }
            finally { await drug.DisposeAsync().ConfigureAwait(false); }
        }

        public static async Task<PurchaseResult> PurchaseAsync(Character character, CustomDrugDesign design, CancellationToken token = default)
        {
            var drug = await BuildAsync(character, design, token).ConfigureAwait(false);
            return await PurchaseRules.PurchaseDrugInstanceAsync(character, drug, design.Markup, design.Free, token).ConfigureAwait(false);
        }
    }
}
