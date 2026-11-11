/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Backend.Equipment;

namespace Chummer.Rules
{
    public sealed class ImplantUpgradeOptions
    {
        public int MinimumRating { get; set; }
        public int MaximumRating { get; set; }
        public int CurrentRating { get; set; }
        public string CurrentGrade { get; set; }
        public List<ListItem> Grades { get; } = new List<ListItem>();
    }
    public sealed class ImplantUpgradeQuote
    {
        public decimal Cost { get; set; }
        public decimal TradeIn { get; set; }
        public decimal OldValue { get; set; }
        public decimal NewValue { get; set; }
        public string OldGrade { get; set; }
        public string NewGrade { get; set; }
        public decimal OldEssence { get; set; }
        public decimal NewEssence { get; set; }
        public string OldCapacity { get; set; }
        public string NewCapacity { get; set; }
        public bool CanAfford { get; set; }
        public bool CanApply { get; set; }
        public string Message { get; set; }
    }
    public static class ImplantUpgradeRules
    {
        private static async Task<bool> OwnedAsync(Character character, Cyberware implant, CancellationToken token)
            => implant != null && ReferenceEquals(implant.CharacterObject, character) && await implant.GetParentVehicleAsync(token).ConfigureAwait(false) == null
               && implant.SourceID != Cyberware.EssenceHoleGUID && implant.SourceID != Cyberware.EssenceAntiHoleGUID
               && !(implant.Capacity == "[*]" && await implant.GetParentAsync(token).ConfigureAwait(false) != null)
               && ReferenceEquals(await character.Cyberware.DeepFindByIdAsync(implant.InternalId, token).ConfigureAwait(false), implant);

        private static async Task<List<Grade>> GradesAsync(Character character, Cyberware implant, CancellationToken token)
        {
            var source = await implant.GetSourceTypeAsync(token).ConfigureAwait(false);
            var candidates = await character.GetGradesListAsync(source, token: token).ConfigureAwait(false);
            var disabled = await ImprovementManager.GetCachedImprovementListForValueOfAsync(character,
                source == Improvement.ImprovementSource.Bioware ? Improvement.ImprovementType.DisableBiowareGrade : Improvement.ImprovementType.DisableCyberwareGrade, token: token).ConfigureAwait(false);
            string forced = implant.ForceGrade;
            var parent = await implant.GetParentAsync(token).ConfigureAwait(false);
            if (parent != null && string.IsNullOrEmpty(forced)) forced = (await parent.GetGradeAsync(token).ConfigureAwait(false)).Name;
            bool adapsin = source == Improvement.ImprovementSource.Cyberware && await character.GetAdapsinEnabledAsync(token).ConfigureAwait(false);
            bool burnout = await character.GetBurnoutsWayEnabledAsync(token).ConfigureAwait(false);
            var result = new List<Grade>();
            foreach (var grade in candidates)
            {
                if (!string.IsNullOrEmpty(forced))
                {
                    if (grade.Name == forced || grade.SourceIDString == forced) result.Add(grade);
                    continue;
                }
                if (grade.Name == "None" || grade.Name.ContainsAny(disabled.Select(x => x.ImprovedName))) continue;
                if (adapsin ? !grade.Adapsin && grade.Name.ContainsAny(candidates.Where(x => x.Adapsin).Select(x => x.Name)) : grade.Adapsin) continue;
                if (burnout ? !grade.Burnout && grade.Name.ContainsAny(candidates.Where(x => x.Burnout).Select(x => x.Name)) : grade.Burnout) continue;
                if (!await (await grade.GetNodeXPathAsync(token: token).ConfigureAwait(false)).RequirementsMetAsync(character, token: token).ConfigureAwait(false)) continue;
                result.Add(grade);
            }
            return result;
        }

        public static async Task<ImplantUpgradeOptions> OptionsAsync(Character character, Cyberware implant, CancellationToken token = default)
        {
            if (!await OwnedAsync(character, implant, token).ConfigureAwait(false)) return null;
            var result = new ImplantUpgradeOptions { MinimumRating = await implant.GetMinRatingAsync(token).ConfigureAwait(false),
                MaximumRating = await implant.GetMaxRatingAsync(token).ConfigureAwait(false), CurrentRating = await implant.GetRatingAsync(token).ConfigureAwait(false),
                CurrentGrade = (await implant.GetGradeAsync(token).ConfigureAwait(false)).SourceIDString };
            foreach (var grade in await GradesAsync(character, implant, token).ConfigureAwait(false))
                result.Grades.Add(new ListItem(grade.SourceIDString, await grade.GetCurrentDisplayNameAsync(token).ConfigureAwait(false)));
            return result;
        }
        private static decimal CapacityNumber(string text, bool consumed)
        {
            int split = text.IndexOf("/[", StringComparison.Ordinal);
            if (consumed && split >= 0) text = text.Substring(split + 2).TrimEnd(']');
            else if (!consumed && split >= 0) text = text.Substring(0, split);
            else if (!consumed && text.StartsWith("[", StringComparison.Ordinal)) return 0;
            text = text.Trim('[', ']');
            if (text == "*") return 0;
            if (!decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal value)) throw new ArgumentException("The implant capacity expression could not be evaluated.");
            return value;
        }

        public static async Task<ImplantUpgradeQuote> QuoteAsync(Character character, Cyberware implant, string gradeId, int rating, decimal refundFraction, bool free, CancellationToken token = default)
        {
            if (refundFraction < 0 || refundFraction > 1 || !await OwnedAsync(character, implant, token).ConfigureAwait(false)) return null;
            var grade = (await GradesAsync(character, implant, token).ConfigureAwait(false)).FirstOrDefault(x => x.SourceIDString == gradeId || x.Name == gradeId);
            if (grade == null || rating < await implant.GetMinRatingAsync(token).ConfigureAwait(false) || rating > await implant.GetMaxRatingAsync(token).ConfigureAwait(false)) return null;
            var currentGrade = await implant.GetGradeAsync(token).ConfigureAwait(false);
            bool changed = rating != await implant.GetRatingAsync(token).ConfigureAwait(false) || currentGrade.SourceIDString != grade.SourceIDString;
            bool career = await character.GetCreatedAsync(token).ConfigureAwait(false);
            decimal oldValue = await implant.GetTotalCostAsync(token).ConfigureAwait(false);
            decimal newValue = await implant.CalculatedTotalCostAsync(() => Task.FromResult(rating), grade, token).ConfigureAwait(false);
            decimal tradeIn = career && !free ? oldValue * refundFraction : 0;
            decimal cost = !career || free ? 0 : newValue - tradeIn;
            string oldCapacity = await implant.GetCalculatedCapacityAsync(CultureInfo.InvariantCulture, token).ConfigureAwait(false);
            string newCapacity = await implant.GetCalculatedCapacityForRatingAsync(CultureInfo.InvariantCulture, rating, token).ConfigureAwait(false);
            bool capacity = true;
            if (await character.Settings.GetEnforceCapacityAsync(token).ConfigureAwait(false))
            {
                var parent = await implant.GetParentAsync(token).ConfigureAwait(false);
                if (parent != null && implant.ParentID != parent.InternalId && !await implant.PlugsIntoTargetCyberwareAsync(parent, token).ConfigureAwait(false))
                    capacity = await parent.GetCapacityRemainingAsync(token).ConfigureAwait(false) + CapacityNumber(oldCapacity, true) >= CapacityNumber(newCapacity, true);
                if (capacity && (CapacityNumber(oldCapacity, false) > 0 || CapacityNumber(newCapacity, false) > 0))
                    capacity = await implant.GetCapacityRemainingAsync(token).ConfigureAwait(false) + CapacityNumber(newCapacity, false) - CapacityNumber(oldCapacity, false) >= 0;
            }
            bool affordable = !career || cost <= await character.GetNuyenAsync(token).ConfigureAwait(false);
            return new ImplantUpgradeQuote { Cost = cost, TradeIn = tradeIn, OldValue = oldValue, NewValue = newValue, OldGrade = currentGrade.Name, NewGrade = grade.Name,
                OldEssence = await implant.GetCalculatedESSPrototypeInvariantAsync(token).ConfigureAwait(false),
                NewEssence = await implant.GetCalculatedESSPrototypeInvariantAsync(rating, grade, token).ConfigureAwait(false), OldCapacity = oldCapacity, NewCapacity = newCapacity,
                CanAfford = affordable, CanApply = changed && capacity && affordable, Message = !changed ? "Select a different grade or rating." : !capacity ? "The replacement exceeds implant capacity." : !affordable ? "Insufficient funds." : "" };
        }

        public static async Task<PurchaseResult> ApplyAsync(Character character, Cyberware implant, string gradeId, int rating, decimal refundFraction, bool free, CancellationToken token = default)
        {
            var quote = await QuoteAsync(character, implant, gradeId, rating, refundFraction, free, token).ConfigureAwait(false);
            if (quote == null || !quote.CanApply) return new PurchaseResult(quote != null && !quote.CanAfford ? RuleResult.NotEnoughNuyen : RuleResult.NotAllowed);
            var grade = (await GradesAsync(character, implant, token).ConfigureAwait(false)).First(x => x.SourceIDString == gradeId || x.Name == gradeId);
            var oldGrade = await implant.GetGradeAsync(token).ConfigureAwait(false); int oldRating = await implant.GetRatingAsync(token).ConfigureAwait(false);
            decimal oldEssence = await implant.GetCalculatedESSAsync(token).ConfigureAwait(false);
            bool career = await character.GetCreatedAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            try { await implant.ApplyUpgradeFieldsAsync(grade, rating, CancellationToken.None).ConfigureAwait(false); }
            catch { await implant.ApplyUpgradeFieldsAsync(oldGrade, oldRating, CancellationToken.None).ConfigureAwait(false); throw; }
            ExpenseLogEntry expense = null;
            if (career)
            {
                decimal delta = await implant.GetCalculatedESSPrototypeInvariantAsync(rating, grade, CancellationToken.None).ConfigureAwait(false) - oldEssence;
                if (delta > 0) await character.DecreaseEssenceHoleAsync(delta, token: CancellationToken.None).ConfigureAwait(false);
                else if (delta < 0) await character.IncreaseEssenceHoleAsync(-delta, token: CancellationToken.None).ConfigureAwait(false);
                string description = await LanguageManager.GetStringAsync("String_ExpenseUpgradedCyberware", token: CancellationToken.None).ConfigureAwait(false) + " "
                    + await implant.GetCurrentDisplayNameShortAsync(CancellationToken.None).ConfigureAwait(false) + " (" + oldGrade.Name + " -> " + grade.Name + ", " + oldRating + " -> " + rating + ")";
                expense = new ExpenseLogEntry(character); expense.Create(-quote.Cost, description, ExpenseType.Nuyen, DateTime.Now);
                // The original upgrade used AddGear, which cannot restore an implant's old state.
                await character.ExpenseEntries.AddWithSortAsync(expense, token: CancellationToken.None).ConfigureAwait(false);
                await character.ModifyNuyenAsync(-quote.Cost, CancellationToken.None).ConfigureAwait(false);
            }
            return new PurchaseResult(RuleResult.Changed, implant, quote.Cost, expense);
        }
    }
}
