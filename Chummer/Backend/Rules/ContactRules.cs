/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Chummer.Backend.Enums;

namespace Chummer.Rules
{
    public static class ContactRules
    {
        private static bool CanLinkFile(Character character, string file)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return false;
            if (!file.EndsWith(".chum5", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".chum5lz", StringComparison.OrdinalIgnoreCase)) return false;
            return string.IsNullOrEmpty(character.FileName) || !string.Equals(Path.GetFullPath(file), Path.GetFullPath(character.FileName), Utils.PathStringComparison);
        }
        public static async Task<RuleResult> LinkAsync(Character character, Contact contact, string file, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (contact == null || !ReferenceEquals(contact.CharacterObject, character) || !CanLinkFile(character, file)
                || !await character.Contacts.ContainsAsync(contact, token).ConfigureAwait(false) || await contact.GetReadOnlyAsync(token).ConfigureAwait(false)) return RuleResult.NotAllowed;
            if (string.Equals(await contact.GetFileNameAsync(token).ConfigureAwait(false), file, Utils.PathStringComparison)) return RuleResult.Cancelled;
            string previous = await contact.GetFileNameAsync(token).ConfigureAwait(false);
            try
            {
                await contact.SetFileNameAsync(Path.GetFullPath(file), token).ConfigureAwait(false);
                if (await contact.GetLinkedCharacterAsync(token).ConfigureAwait(false) != null) return RuleResult.Changed;
            }
            catch
            {
                await contact.SetFileNameAsync(previous).ConfigureAwait(false);
                throw;
            }
            await contact.SetFileNameAsync(previous).ConfigureAwait(false);
            return RuleResult.NotAllowed;
        }
        public static async Task<RuleResult> AddFromFileAsync(Character character, string file, ContactType type, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!Enum.IsDefined(typeof(ContactType), type) || !CanLinkFile(character, file)) return RuleResult.NotAllowed;
            var contact = new Contact(character);
            bool added = false;
            try
            {
                await contact.SetEntityTypeAsync(type, token).ConfigureAwait(false);
                await contact.SetFileNameAsync(Path.GetFullPath(file), token).ConfigureAwait(false);
                if (await contact.GetLinkedCharacterAsync(token).ConfigureAwait(false) == null) return RuleResult.NotAllowed;
                await character.Contacts.AddAsync(contact, token: token).ConfigureAwait(false);
                added = true;
                return RuleResult.Changed;
            }
            finally
            {
                if (!added)
                {
                    await character.Contacts.RemoveAsync(contact).ConfigureAwait(false);
                    await contact.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
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
