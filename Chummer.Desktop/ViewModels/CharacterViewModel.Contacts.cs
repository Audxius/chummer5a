/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using System.Threading.Tasks;
using Chummer.Backend.Enums;
using Chummer.Desktop.Views.Dialogs;
using Chummer.Rules;
using CommunityToolkit.Mvvm.Input;

namespace Chummer.Desktop.ViewModels
{
    /// <summary>Adding, editing and deleting contacts / enemies / pets (legacy CharacterShared.AddContact / DeleteContact, ContactControl).</summary>
    public sealed partial class CharacterViewModel
    {
        internal System.Collections.Concurrent.ConcurrentDictionary<string, bool> ExpandedContactIds { get; } = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
        [RelayCommand]
        private Task AddContact() => AddContactAsync(ContactType.Contact);

        [RelayCommand]
        private Task AddEnemy() => AddContactAsync(ContactType.Enemy);

        [RelayCommand]
        private Task AddPet() => AddContactAsync(ContactType.Pet);

        private async Task AddContactAsync(ContactType eType)
        {
            string strTitle = eType == ContactType.Enemy ? "New enemy" : eType == ContactType.Pet ? "New pet" : "New contact";
            ContactFields objFields = new ContactFields();
            int intMaxConnection = await ReadModelAsync(() => Task.FromResult(Character.MaxContactConnection));
            if (!await ContactEditorWindow.EditAsync(null, Character, objFields, strTitle, true, true, true, intMaxConnection).ConfigureAwait(true))
                return;
            await RunRuleAsync(async token =>
            {
                Contact objContact = new Contact(Character);
                try
                {
                    await objContact.SetEntityTypeAsync(eType).ConfigureAwait(false);
                    await Character.Contacts.AddAsync(objContact).ConfigureAwait(false);
                    objFields.ApplyTo(objContact);
                    return RuleResult.Changed;
                }
                catch
                {
                    try
                    {
                        await Character.Contacts.RemoveAsync(objContact).ConfigureAwait(false);
                    }
                    catch
                    {
                        // swallow this, like legacy
                    }
                    await objContact.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }).ConfigureAwait(true);
        }

        internal async Task EditContactAsync(Contact objContact)
        {
            var snapshot = await ReadModelAsync(() => Task.FromResult((Fields: ContactFields.From(objContact),
                Title: "Edit " + (objContact.IsEnemy ? "enemy" : objContact.EntityType == ContactType.Pet ? "pet" : "contact"),
                Group: objContact.GroupEnabled, Free: objContact.FreeEnabled, Loyalty: objContact.LoyaltyEnabled && !objContact.ReadOnly, Maximum: Character.MaxContactConnection)));
            ContactFields objFields = snapshot.Fields;
            if (!await ContactEditorWindow.EditAsync(null, Character, objFields, snapshot.Title, snapshot.Group, snapshot.Free,
                                                     snapshot.Loyalty, snapshot.Maximum).ConfigureAwait(true))
                return;
            await RunRuleAsync(_ =>
            {
                if (!Character.Contacts.Contains(objContact)) return Task.FromResult(RuleResult.NotAllowed);
                objFields.ApplyTo(objContact); return Task.FromResult(RuleResult.Changed);
            });
        }

        internal async Task DeleteContactAsync(Contact objContact)
        {
            string strKey;
            switch (await ReadModelAsync(() => Task.FromResult(objContact.EntityType)))
            {
                case ContactType.Enemy:
                    strKey = "Message_DeleteEnemy";
                    break;
                case ContactType.Pet:
                    strKey = "Message_DeletePet";
                    break;
                default:
                    strKey = "Message_DeleteContact";
                    break;
            }
            if (!await CommonFunctions.ConfirmDeleteAsync(await LanguageManager.GetStringAsync(strKey).ConfigureAwait(true)).ConfigureAwait(true))
                return;
            await RunRuleAsync(async token =>
            {
                if (!await Character.Contacts.RemoveAsync(objContact, token)) return RuleResult.Cancelled;
                ExpandedContactIds.TryRemove(objContact.InternalId, out _);
                return RuleResult.Changed;
            });
        }
    }
}
