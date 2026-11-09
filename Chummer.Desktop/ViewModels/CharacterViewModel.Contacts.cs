/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using System.Threading.Tasks;
using Chummer.Backend.Enums;
using Chummer.Desktop.Views.Dialogs;
using CommunityToolkit.Mvvm.Input;

namespace Chummer.Desktop.ViewModels
{
    /// <summary>Adding, editing and deleting contacts / enemies / pets (legacy CharacterShared.AddContact / DeleteContact, ContactControl).</summary>
    public sealed partial class CharacterViewModel
    {
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
            int intMaxConnection = Character.MaxContactConnection;
            if (!await ContactEditorWindow.EditAsync(null, Character, objFields, strTitle, true, true, true, intMaxConnection).ConfigureAwait(true))
                return;
            await Task.Run(async () =>
            {
                Contact objContact = new Contact(Character);
                try
                {
                    await objContact.SetEntityTypeAsync(eType).ConfigureAwait(false);
                    await Character.Contacts.AddAsync(objContact).ConfigureAwait(false);
                    objFields.ApplyTo(objContact);
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
            MarkDirty();
            Reload();
        }

        internal async Task EditContactAsync(Contact objContact)
        {
            ContactFields objFields = await Task.Run(() => ContactFields.From(objContact)).ConfigureAwait(true);
            string strTitle = "Edit " + (objContact.IsEnemy ? "enemy" : objContact.EntityType == ContactType.Pet ? "pet" : "contact");
            if (!await ContactEditorWindow.EditAsync(null, Character, objFields, strTitle, objContact.GroupEnabled, objContact.FreeEnabled,
                                                     objContact.LoyaltyEnabled && !objContact.ReadOnly, Character.MaxContactConnection).ConfigureAwait(true))
                return;
            await Task.Run(() => objFields.ApplyTo(objContact)).ConfigureAwait(true);
            MarkDirty();
            Reload();
        }

        internal async Task DeleteContactAsync(Contact objContact)
        {
            string strKey;
            switch (objContact.EntityType)
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
            await Task.Run(() => Character.Contacts.RemoveAsync(objContact)).ConfigureAwait(true);
            MarkDirty();
            Reload();
        }
    }
}
