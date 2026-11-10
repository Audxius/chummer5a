/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Chummer.Rules
{
    public static class SustainingRules
    {
        private static async Task<bool> IsOwnedAsync(Character character, IHasInternalId source, CancellationToken token)
        {
            switch (source)
            {
                case Spell spell: return ReferenceEquals(spell.CharacterObject, character) && await character.Spells.ContainsAsync(spell, token).ConfigureAwait(false);
                case ComplexForm form: return ReferenceEquals(form.CharacterObject, character) && await character.ComplexForms.ContainsAsync(form, token).ConfigureAwait(false);
                case CritterPower power: return ReferenceEquals(power.CharacterObject, character) && await character.CritterPowers.ContainsAsync(power, token).ConfigureAwait(false);
                default: return false;
            }
        }
        private static bool Valid(int force, int hits) => force >= 0 && force <= 100 && hits >= 0 && hits <= 100;

        public static async Task<RuleResult> AddAsync(Character character, IHasInternalId source, int force, int hits, bool selfSustained, CancellationToken token = default)
        {
            if (!Valid(force, hits) || !await IsOwnedAsync(character, source, token).ConfigureAwait(false)) return RuleResult.NotAllowed;
            var sustained = new SustainedObject(character, source);
            await sustained.SetForceAsync(force, token).ConfigureAwait(false); await sustained.SetNetHitsAsync(hits, token).ConfigureAwait(false);
            await sustained.SetSelfSustainedAsync(selfSustained, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await character.SustainedCollection.AddAsync(sustained, CancellationToken.None).ConfigureAwait(false);
            await character.RefreshSustainingPenaltiesAsync(CancellationToken.None).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        public static async Task<RuleResult> UpdateAsync(Character character, SustainedObject sustained, int force, int hits, bool selfSustained, CancellationToken token = default)
        {
            if (sustained == null || !Valid(force, hits) || !ReferenceEquals(sustained.CharacterObject, character)
                || !await character.SustainedCollection.ContainsAsync(sustained, token).ConfigureAwait(false)
                || !await IsOwnedAsync(character, sustained.LinkedObject, token).ConfigureAwait(false)) return RuleResult.NotAllowed;
            token.ThrowIfCancellationRequested();
            await sustained.SetForceAsync(force, CancellationToken.None).ConfigureAwait(false);
            await sustained.SetNetHitsAsync(hits, CancellationToken.None).ConfigureAwait(false);
            await sustained.SetSelfSustainedAsync(selfSustained, CancellationToken.None).ConfigureAwait(false);
            // Force changes can change which effects qualify for penalty-free sustaining bonuses.
            await character.RefreshSustainingPenaltiesAsync(CancellationToken.None).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        public static async Task<RuleResult> RemoveAsync(Character character, SustainedObject sustained, CancellationToken token = default)
        {
            if (sustained == null || !ReferenceEquals(sustained.CharacterObject, character)
                || !await character.SustainedCollection.ContainsAsync(sustained, token).ConfigureAwait(false)) return RuleResult.NotAllowed;
            token.ThrowIfCancellationRequested();
            await character.SustainedCollection.RemoveAsync(sustained, CancellationToken.None).ConfigureAwait(false);
            await character.RefreshSustainingPenaltiesAsync(CancellationToken.None).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        public static async Task<RuleResult> SetPsycheAsync(Character character, bool active, CancellationToken token = default)
        {
            await character.SetPsycheActiveAsync(active, token).ConfigureAwait(false);
            await character.RefreshSustainingPenaltiesAsync(CancellationToken.None).ConfigureAwait(false);
            return RuleResult.Changed;
        }
    }
}
