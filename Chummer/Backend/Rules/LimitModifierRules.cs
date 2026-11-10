/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Chummer.Rules
{
    public static class LimitModifierRules
    {
        public static async Task<RuleResult> SaveAsync(Character character, string name, string limit, int bonus, string condition,
            string notes, LimitModifier existing = null, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(name) || (limit != "Physical" && limit != "Mental" && limit != "Social")) return RuleResult.NotAllowed;
            if (existing != null && (!existing.CanDelete || !ReferenceEquals(existing.CharacterObject, character)
                || !await character.LimitModifiers.ContainsAsync(existing, token).ConfigureAwait(false))) return RuleResult.NotAllowed;
            name = await LanguageManager.ReverseTranslateExtraAsync(name.Trim(), objCharacter: character, token: token).ConfigureAwait(false);
            condition = await LanguageManager.ReverseTranslateExtraAsync(condition ?? "", objCharacter: character, token: token).ConfigureAwait(false);
            var replacement = new LimitModifier(character, existing?.InternalId ?? "");
            replacement.Create(name, bonus, limit, condition, true);
            replacement.Notes = notes ?? "";
            if (existing != null) replacement.NotesColor = existing.NotesColor;
            token.ThrowIfCancellationRequested();
            if (existing != null)
            {
                int index = await character.LimitModifiers.IndexOfAsync(existing, token).ConfigureAwait(false);
                if (index < 0) return RuleResult.NotAllowed;
                await character.LimitModifiers.SetValueAtAsync(index, replacement, token).ConfigureAwait(false);
            }
            else await character.LimitModifiers.AddAsync(replacement, token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        public static async Task<RuleResult> RemoveAsync(Character character, LimitModifier modifier, CancellationToken token = default)
        {
            if (modifier == null || !modifier.CanDelete || !ReferenceEquals(modifier.CharacterObject, character)) return RuleResult.NotAllowed;
            return await character.LimitModifiers.RemoveAsync(modifier, token).ConfigureAwait(false) ? RuleResult.Changed : RuleResult.NotAllowed;
        }
    }
}
