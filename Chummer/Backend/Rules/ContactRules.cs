/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Chummer.Rules
{
    public static class ContactRules
    {
        public static async Task<RuleResult> SetRatingAsync(Character character, Contact contact, int value, bool connection, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (contact == null || !ReferenceEquals(contact.CharacterObject, character)
                || !await character.Contacts.ContainsAsync(contact, token).ConfigureAwait(false)
                || await contact.GetReadOnlyAsync(token).ConfigureAwait(false) || value < 1) return RuleResult.NotAllowed;
            if (connection)
            {
                if (value > await character.GetMaxContactConnectionAsync(token).ConfigureAwait(false)) return RuleResult.NotAllowed;
                if (value == await contact.GetConnectionAsync(token).ConfigureAwait(false)) return RuleResult.Cancelled;
                await contact.SetConnectionAsync(value, token).ConfigureAwait(false);
            }
            else
            {
                if (value > 6 || !await contact.GetLoyaltyEnabledAsync(token).ConfigureAwait(false)) return RuleResult.NotAllowed;
                if (value == await contact.GetLoyaltyAsync(token).ConfigureAwait(false)) return RuleResult.Cancelled;
                await contact.SetLoyaltyAsync(value, token).ConfigureAwait(false);
            }
            return RuleResult.Changed;
        }
    }
}
