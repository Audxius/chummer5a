using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Chummer;
using Chummer.Desktop.ViewModels;
using Chummer.Desktop.Views;
using Chummer.Desktop.Views.Dialogs;
using Chummer.Desktop.Views.Sections;
using Chummer.Rules;

internal static class PageReviewScript
{
    public static async Task CheckPetEditorAsync(MainWindow window, CharacterViewModel runner, string output, Func<Task> settle)
    {
        var fields = new ContactFields { Name = "Review companion", Metatype = "Dog" };
        var editing = ContactEditorWindow.EditAsync(window, runner.Character, fields, "New pet or cohort", true, true, true, runner.Character.MaxContactConnection, true);
        await settle();
        var editor = window.OwnedWindows.OfType<ContactEditorWindow>().Single();
        if (!editor.GetVisualDescendants().OfType<AutoCompleteBox>().Any(box => box.ItemsSource?.Cast<string>().Contains("Dog") == true && box.ItemsSource.Cast<string>().Contains("Human")))
            throw new InvalidOperationException("Pet/cohort editor is missing critter and metahuman species.");
        Save(editor, output, "50-pet-cohort-editor"); editor.Close();
        if (await editing) throw new InvalidOperationException("Closing the pet editor unexpectedly accepted the record.");
        Console.WriteLine("Pet/cohort species suggestions and editor cancellation passed.");
    }
    public static async Task RunAsync(MainWindow window, CharacterViewModel runner, string output, Func<Task> settle)
    {
        foreach (var section in new[] { CharacterSection.Overview, CharacterSection.Attributes, CharacterSection.Skills, CharacterSection.Inventory, CharacterSection.Network, CharacterSection.Notes, CharacterSection.Ledger })
        {
            runner.ShowSectionCommand.Execute(section); await settle();
            Save(window, output, "40-" + section.ToString().ToLowerInvariant());
        }
        runner.ShowSectionCommand.Execute(CharacterSection.Skills); await settle();
        foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(x => (x.Classes.Contains("seg") || x.Classes.Contains("nav") || x.Classes.Contains("section"))
            && x.IsVisible && x.GetVisualAncestors().OfType<Control>().All(c => c.IsVisible)).ToArray())
            await CheckHoverAsync(window, button);
        var sectionTabs = window.GetVisualDescendants().OfType<ToggleButton>().Where(x => x.Classes.Contains("section") && x.CommandParameter is CharacterSection).ToArray();
        Click(window, sectionTabs.Single(x => (CharacterSection)x.CommandParameter == CharacterSection.Overview)); await settle();
        if (!runner.IsOverview) throw new InvalidOperationException("Pointer click did not open Overview.");
        Click(window, sectionTabs.Single(x => (CharacterSection)x.CommandParameter == CharacterSection.Skills)); await settle();
        if (!runner.IsSkills) throw new InvalidOperationException("Pointer click did not return to Skills.");
        var nameSort = window.GetVisualDescendants().OfType<Button>().Single(x => x.Classes.Contains("seg") && x.CommandParameter is SkillSortMode mode && mode == SkillSortMode.Name);
        Click(window, nameSort); await settle();
        if (!runner.SortIsName) throw new InvalidOperationException("Pointer click did not select name sorting.");
        Save(window, output, "41-skills-hover-and-sort");
        Console.WriteLine("Repeated pointer moves preserved navigation/filter hit targets and bounds; pointer click sorted skills.");
        runner.ShowGroupsPageCommand.Execute(null); await settle();
        if (!runner.IsSkillGroupsPage || !runner.SkillGroups.Any(x => x.Rating == 0)) throw new InvalidOperationException("Untrained/broken skill groups remain hidden.");
        Save(window, output, "41-skill-groups");
        runner.ShowSkillsPageCommand.Execute(null); await settle();

        runner.ShowSectionCommand.Execute(CharacterSection.Notes); await settle();
        var notesTabs = window.GetVisualDescendants().OfType<TabControl>().Single(x => x.DataContext is CharacterInfoViewModel);
        notesTabs.SelectedIndex = 4; await settle(); Save(window, output, "41-notes-editor");
        var notesPanel = ((TabItem)notesTabs.SelectedItem).Content as Control;
        var notesEditor = notesPanel.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "NotebookBody");
        if (notesEditor.Bounds.Height < 250) throw new InvalidOperationException("Notes editor did not fill its available height.");
        string originalNotes = notesEditor.Text ?? "";
        string savePath = Path.Combine(output, "notes-roundtrip.chum5");
        await runner.Character.SetFileNameAsync(savePath);
        runner.Info.Notes = originalNotes + " draft"; runner.Info.Notes = originalNotes;
        notesEditor.Focus(); notesEditor.CaretIndex = (notesEditor.Text ?? "").Length;
        window.KeyTextInput(" UI roundtrip note");
        var saveButton = window.GetVisualDescendants().OfType<Button>().Single(x => ReferenceEquals(x.Command, runner.SaveCommand));
        Click(window, saveButton);
        if (runner.SaveCommand.ExecutionTask != null) await runner.SaveCommand.ExecutionTask;
        using (var restored = new Character())
        {
            if (!await Task.Run(() => restored.LoadAsync(savePath)) || restored.Notes != originalNotes + " UI roundtrip note")
                throw new InvalidOperationException("Immediate save lost or reordered the last Notes edit.");
        }
        Console.WriteLine("Real text input, immediate Save click and Notes save/reload passed.");
        notesEditor.Focus(); notesEditor.CaretIndex = (notesEditor.Text ?? "").Length;
        window.KeyTextInput(" CtrlS");
        window.KeyPress(Key.S, RawInputModifiers.Control, default, "s");
        window.KeyRelease(Key.S, RawInputModifiers.Control, default, "s"); Dispatcher.UIThread.RunJobs();
        if (runner.SaveCommand.ExecutionTask != null) await runner.SaveCommand.ExecutionTask;
        using (var restored = new Character())
        {
            if (!await Task.Run(() => restored.LoadAsync(savePath)) || restored.Notes != originalNotes + " UI roundtrip note CtrlS")
                throw new InvalidOperationException("Ctrl+S lost the focused Notes editor's last edit.");
        }
        Console.WriteLine("Focused Notes editor Ctrl+S save/reload passed.");

        var notebook = runner.Info.Notebook;
        notebook.AddPageCommand.Execute(null);
        notebook.SelectedPage.Title = "Seattle leads";
        notebook.SelectedPage.Body = "Meet the fixer.\nBring the deck.";
        var pageId = notebook.SelectedPage.Id;
        notebook.MoveUpCommand.Execute(null);
        notebook.AddPageCommand.Execute(null);
        await notebook.DeletePageCommand.ExecuteAsync(null);
        await settle();
        await runner.SaveCommand.ExecuteAsync(null);
        using (var restored = new Character())
        {
            if (!await Task.Run(() => restored.LoadAsync(savePath))) throw new InvalidOperationException("Notebook save failed to reload.");
            var pages = NotebookCodec.Parse(restored.Notes);
            if (pages.Count != 2 || pages[0].Id != pageId || pages[0].Title != "Seattle leads" || pages[0].Body != "Meet the fixer.\nBring the deck." || pages[1].Body != originalNotes + " UI roundtrip note CtrlS")
                throw new InvalidOperationException("Notebook add, rename, reorder or delete lost page content.");
        }
        Save(window, output, "41-notebook-pages");
        Console.WriteLine("Notebook page creation, renaming, reordering, deletion and legacy Notes save/reload passed.");

        if (runner.ContactCards.Count == 0)
        {
            await runner.RunRuleAsync(async t =>
            {
                var contact = new Contact(runner.Character) { Name = "Street doctor", Role = "Medic", Notes = "A discreet local medic with reliable clinic contacts." };
                await runner.Character.Contacts.AddAsync(contact, t); return RuleResult.Changed;
            }); await settle();
        }
        runner.ShowSectionCommand.Execute(CharacterSection.Network); await settle();
        if (Environment.GetEnvironmentVariable("CHUMMER_REVIEW_CONTACT_ART") == "1" && (runner.ContactCards.Count != 10 || runner.ContactCards.Any(x => !x.HasPortrait)))
            throw new InvalidOperationException("Not all ten contact portraits loaded.");
        Save(window, output, "42-contacts-collapsed");
        var first = runner.ContactCards.First(); string id = first.Id;
        var card = window.GetVisualDescendants().OfType<Button>().Single(x => ReferenceEquals(x.DataContext, first) && x.Classes.Contains("contactCard"));
        Click(window, card); await settle();
        var profile = window.GetVisualDescendants().OfType<ContactProfileView>().Single();
        if (window.OwnedWindows.Any()) throw new InvalidOperationException("Contact profile opened another window.");
        var profileArt = profile.GetVisualDescendants().OfType<Image>().Single(x => x.Name == "ContactProfileArtwork");
        if (profileArt.Bounds.Width < 250 || first.HasPortrait && ((Avalonia.Media.Imaging.Bitmap)profileArt.Source).PixelSize.Width <= 256)
            throw new InvalidOperationException("Contact profile does not show large original artwork.");
        Save(window, output, "43-contact-profile");
        if (first.IsEditable)
        {
            Click(window, profile.GetVisualDescendants().OfType<Button>().Single(x => x.Content as string == "Edit profile…")); await settle();
            var editor = window.OwnedWindows.Single();
            if (!editor.Title.StartsWith("Edit ", StringComparison.Ordinal)) throw new InvalidOperationException("Contact editor did not belong to its profile window.");
            editor.Close(); await settle();
            if (profile.Profile.Connection != first.Connection) throw new InvalidOperationException("Cancelling contact editing changed its rating.");
        }
        if (first.IsEditable)
        {
            int expected = first.Connection < first.MaxConnection ? first.Connection + 1 : first.Connection - 1;
            var change = profile.GetVisualDescendants().OfType<Button>().First(x => x.Content as string == (first.Connection < first.MaxConnection ? "+" : "−"));
            Click(window, change);
            await settle();
            var refreshed = runner.ContactCards.Single(x => x.Id == id);
            if (refreshed.Connection != expected || profile.Profile.Connection != expected) throw new InvalidOperationException("Profile rating change did not refresh both views.");
        }
        Click(window, profile.GetVisualDescendants().OfType<Button>().Single(x => x.Content as string == "← Contacts")); await settle();
        runner.ShowCompanionsPageCommand.Execute(null); await settle(); Save(window, output, "43-pets-and-cohorts");
        if (!runner.ShowCompanions) throw new InvalidOperationException("Pets and cohorts view failed to open.");
        using (var companion = new Character { SettingsKey = runner.Character.SettingsKey, Name = "Review cohort" })
        {
            string companionPath = Path.Combine(output, "review-cohort.chum5");
            await companion.SetFileNameAsync(companionPath);
            if (!await Task.Run(() => companion.SaveAsync(companionPath))) throw new InvalidOperationException("Could not prepare cohort fixture.");
            await CharacterRegistry.OpenCharacters.AddAsync(companion);
            try
            {
                await runner.RunRuleAsync(t => ContactRules.AddFromFileAsync(runner.Character, companionPath, Chummer.Backend.Enums.ContactType.Pet, t));
                await settle();
                var cohort = runner.PetCards.Single(x => x.LinkedFile == companionPath);
                var cohortCard = window.GetVisualDescendants().OfType<Button>().Single(x => ReferenceEquals(x.DataContext, cohort) && x.Classes.Contains("contactCard"));
                Click(window, cohortCard); await settle();
                var cohortProfile = window.GetVisualDescendants().OfType<ContactProfileView>().Single();
                if (cohortProfile.Profile.LinkedStats.Count != companion.AttributeSection.AttributeList.Count)
                    throw new InvalidOperationException("Linked cohort attributes missing from profile.");
                Save(window, output, "43-linked-cohort-profile");
                Click(window, cohortProfile.GetVisualDescendants().OfType<Button>().Single(x => x.Content as string == "Open full character sheet")); await settle();
                var main = MainWindowViewModel.Current;
                if (main.CurrentPage is not CharacterViewModel linkedView || !ReferenceEquals(linkedView.Character, companion))
                    throw new InvalidOperationException("Opening the linked sheet duplicated the character.");
                await main.CloseCharacterCommand.ExecuteAsync(linkedView); await settle();
                if (companion.IsDisposed || !CharacterRegistry.OpenCharacters.Contains(companion))
                    throw new InvalidOperationException("Closing a linked sheet discarded the character still owned by its contact.");
                await main.OpenCharacterFileAsync(companionPath); await settle();
                if (main.CurrentPage is not CharacterViewModel reopened || !ReferenceEquals(reopened.Character, companion))
                    throw new InvalidOperationException("Reopening the linked sheet did not reuse the live character.");
                await main.CloseCharacterCommand.ExecuteAsync(reopened); main.CurrentPage = runner; await settle();
                await runner.SaveCommand.ExecuteAsync(null);
                using var restored = new Character();
                if (!await Task.Run(() => restored.LoadAsync(savePath)) || !restored.Contacts.Any(x => x.EntityType == Chummer.Backend.Enums.ContactType.Pet && x.FileName == companionPath))
                    throw new InvalidOperationException("Cohort file link was not preserved by character save/reload.");
            }
            finally
            {
                runner.SelectedContactProfile = null;
                await runner.RunRuleAsync(async t =>
                {
                    foreach (var item in runner.Character.Contacts.Where(x => x.FileName == companionPath).ToArray())
                    { await item.SetFileNameAsync(""); await runner.Character.Contacts.RemoveAsync(item); await item.DisposeAsync(); }
                    return RuleResult.Changed;
                });
                await CharacterRegistry.OpenCharacters.RemoveAsync(companion);
            }
        }
        runner.ShowContactsPageCommand.Execute(null); await settle();

        window.Width = 1920; window.Height = 1080; await settle();
        foreach (var section in new[] { CharacterSection.Attributes, CharacterSection.Overview, CharacterSection.Ledger })
        {
            runner.ShowSectionCommand.Execute(section); await settle();
            if (section == CharacterSection.Ledger)
            {
                runner.Ledger.ShowLogPageCommand.Execute(null); await settle(); CheckLedgerWidth(window, "LedgerLogBody");
                Save(window, output, "44-ledger-wide");
                runner.Ledger.ShowCalendarPageCommand.Execute(null); await settle(); CheckLedgerWidth(window, "LedgerCalendarBody");
                Save(window, output, "45-calendar-wide"); runner.Ledger.ShowLogPageCommand.Execute(null);
            }
            else Save(window, output, "46-wide-" + section.ToString().ToLowerInvariant());
        }
        window.Width = 980; window.Height = 720; await settle();
        foreach (var section in new[] { CharacterSection.Attributes, CharacterSection.Overview, CharacterSection.Network, CharacterSection.Notes, CharacterSection.Ledger })
        {
            runner.ShowSectionCommand.Execute(section); await settle(); Save(window, output, "47-compact-" + section.ToString().ToLowerInvariant());
        }
        var account = window.GetVisualDescendants().OfType<ComboBox>().Single(x => x.Name == "CompactLedgerAccount");
        if (!account.IsVisible) throw new InvalidOperationException("Compact Ledger account selector was not shown.");
        account.SelectedIndex = 1; await settle();
        var nuyenPane = window.GetVisualDescendants().OfType<DockPanel>().Single(x => x.Name == "NuyenAccountPane");
        if (!nuyenPane.IsVisible || nuyenPane.Bounds.Width < (window.ClientSize.Width - 232) * 0.9)
            throw new InvalidOperationException("Compact Nuyen account did not fill the ledger.");
        Save(window, output, "48-compact-nuyen");
        account.SelectedIndex = 0; await settle();
        if (nuyenPane.IsVisible) throw new InvalidOperationException("Compact Ledger account switch did not hide Nuyen.");

        runner.ShowSectionCommand.Execute(CharacterSection.Qualities); await settle();
        var qualityCard = window.GetVisualDescendants().OfType<Border>().First(x => x.Classes.Contains("qcard") && x.DataContext is QualityViewModel);
        if (qualityCard.DataContext is not QualityViewModel quality || string.IsNullOrWhiteSpace(quality.Description))
            throw new InvalidOperationException("Quality explanation missing.");
        window.MouseMove(qualityCard.TranslatePoint(new Point(20, 20), window)!.Value, RawInputModifiers.None);
        await Task.Delay(650); Dispatcher.UIThread.RunJobs();
        if (!ToolTip.GetIsOpen(qualityCard)) throw new InvalidOperationException("Hover did not open the quality explanation.");
        if (!ToolTip.GetShouldUseOverlayLayer(qualityCard)) throw new InvalidOperationException("Quality tooltip still uses a native popup surface.");
        for (int i = 0; i < 12; ++i)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); await Task.Delay(40); Dispatcher.UIThread.RunJobs();
            if (!qualityCard.IsPointerOver || !ToolTip.GetIsOpen(qualityCard)) throw new InvalidOperationException("Stationary quality hover flickered during rendering.");
        }
        var tooltip = window.GetVisualDescendants().OfType<ToolTip>().Single(x => x.IsVisible);
        if (tooltip.IsHitTestVisible) throw new InvalidOperationException("Tooltip can intercept pointer input.");
        var pointerPoint = qualityCard.TranslatePoint(new Point(20, 20), window)!.Value;
        var tipOrigin = tooltip.TranslatePoint(default, window)!.Value;
        if (Math.Abs(tipOrigin.X - pointerPoint.X) > 50 || Math.Abs(tipOrigin.Y - pointerPoint.Y) > 50)
            throw new InvalidOperationException("Quality tooltip is not anchored near the hovered point.");
        var tipPoint = tooltip.TranslatePoint(new Point(tooltip.Bounds.Width / 2, tooltip.Bounds.Height / 2), window)!.Value;
        if (window.InputHitTest(tipPoint) is Visual hit && (ReferenceEquals(hit, tooltip) || hit.GetVisualAncestors().Contains(tooltip)))
            throw new InvalidOperationException("Tooltip blocks underlying page hit testing.");
        Save(window, output, "49-quality-hover");
        Console.WriteLine("Page layout, contact profiles, linked cohorts, skill groups, pointer tooltips and full-width ledger checks passed.");
    }

    public static async Task CheckEnginePromptsAsync(MainWindow window, CharacterViewModel runner, string output, Func<Task> settle)
    {
        var dialogs = new Chummer.Desktop.Services.AvaloniaDialogService();
        using (var armor = new Chummer.Backend.Equipment.Armor(runner.Character) { Name = "Full Body Armor", Category = "Armor" })
        {
        var request = new SelectArmorModRequest(runner.Character, armor) { AllowedCategories = "General", ExcludeGeneralCategory = true };
        var picking = dialogs.ShowAsync(request, window); await settle();
        var picker = window.OwnedWindows.Single();
        var list = picker.GetVisualDescendants().OfType<ListBox>().Single();
        list.SelectedIndex = 0; await settle(); Save(picker, output, "68-included-armor-choice");
        var accept = picker.GetVisualDescendants().OfType<Button>().Single(x => x.IsDefault);
        var point = accept.TranslatePoint(new Point(accept.Bounds.Width / 2, accept.Bounds.Height / 2), picker)!.Value;
        picker.MouseDown(point, MouseButton.Left, RawInputModifiers.None); picker.MouseUp(point, MouseButton.Left, RawInputModifiers.None); await settle();
        if (await picking != DialogOutcome.OK || string.IsNullOrWhiteSpace(request.SelectedArmorMod) || armor.ArmorMods.Count != 0) throw new InvalidOperationException("Included armor choice failed or mutated its preview.");
        var cancelRequest = new SelectArmorModRequest(runner.Character, armor) { AllowedCategories = "General", ExcludeGeneralCategory = true };
        picking = dialogs.ShowAsync(cancelRequest, window); await settle(); window.OwnedWindows.Single().Close(); await settle();
        if (await picking != DialogOutcome.Cancel) throw new InvalidOperationException("Armor selection did not cancel.");
        }
        using var fresh = new Character { SettingsKey = runner.Character.SettingsKey };
        string originalMetatype = fresh.Metatype;
        var metatypePicking = dialogs.ShowAsync(new SelectMetatypePriorityRequest(fresh), window);
        for (int i = 0; i < 20 && !window.OwnedWindows.Any(); i++) { await Task.Delay(100); await settle(); }
        var editor = window.OwnedWindows.Single();
        if (editor is not MetatypeWindow) throw new InvalidOperationException("Engine metatype prompt did not use the native editor.");
        Save(editor, output, "69-engine-metatype-editor"); editor.Close(); await settle();
        if (await metatypePicking != DialogOutcome.Cancel || fresh.Metatype != originalMetatype) throw new InvalidOperationException("Cancelled metatype prompt changed the character.");
        string originalReader = GlobalSettings.PdfAppPath;
        var optionsPicking = dialogs.ShowAsync(new EditGlobalSettingsRequest(), window); await settle();
        var options = window.OwnedWindows.Single();
        options.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "PdfAppPath").Text = "/tmp/unsaved-pdf-reader";
        Save(options, output, "70-engine-global-options"); options.Close(); await settle();
        if (await optionsPicking != DialogOutcome.Cancel || GlobalSettings.PdfAppPath != originalReader) throw new InvalidOperationException("Cancelled global options changed the PDF reader.");
        fresh.Name = "Critter power review";
        var main = MainWindowViewModel.Current;
        await CharacterRegistry.OpenCharacters.AddAsync(fresh); await main.OpenCharacterAsync(fresh);
        var powerReview = (CharacterViewModel)main.CurrentPage; powerReview.ShowSectionCommand.Execute(CharacterSection.Abilities); await settle();
        var addAction = powerReview.AbilitySections.Single(x => x.Title == "CRITTER POWERS").Actions.Single();
        var addPower = window.GetVisualDescendants().OfType<Button>().Single(x => ReferenceEquals(x.Command, addAction.Command));
        addPower.BringIntoView(); await settle(); Click(window, addPower);
        var adding = ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)addPower.Command).ExecutionTask; await settle();
        if (adding == null) throw new InvalidOperationException("The critter-power add button did not start its command.");
        for (int i = 0; i < 40 && !window.OwnedWindows.Any() && !adding.IsCompleted; i++) { await Task.Delay(100); Dispatcher.UIThread.RunJobs(); }
        if (adding.IsCompleted) await adding;
        var powerPicker = window.OwnedWindows.Single();
        var powerChoices = powerPicker.GetVisualDescendants().OfType<ListBox>().Single();
        powerChoices.SelectedItem = powerChoices.ItemsSource.Cast<ListItem>().Single(x => x.Name == "Accident"); await settle();
        Save(powerPicker, output, "71-critter-power-picker");
        var powerAccept = powerPicker.GetVisualDescendants().OfType<Button>().Single(x => x.IsDefault);
        var powerPoint = powerAccept.TranslatePoint(new Point(powerAccept.Bounds.Width / 2, powerAccept.Bounds.Height / 2), powerPicker)!.Value;
        powerPicker.MouseDown(powerPoint, MouseButton.Left, RawInputModifiers.None); powerPicker.MouseUp(powerPoint, MouseButton.Left, RawInputModifiers.None);
        await adding; await settle();
        if (fresh.CritterPowers.Single().Name != "Accident" || fresh.ExpenseEntries.Count != 0) throw new InvalidOperationException("Creation critter power did not add without spending karma.");
        Save(window, output, "72-critter-power-added");
        var powerCard = powerReview.AbilitySections.Single(x => x.Title == "CRITTER POWERS").Cards.Single();
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)powerCard.RemoveCommand).ExecuteAsync(null); await settle();
        if (fresh.CritterPowers.Count != 0) throw new InvalidOperationException("Critter power removal failed.");
        string qualityId = fresh.LoadData("qualities.xml").SelectSingleNode("/chummer/qualities/quality[name='First Impression']/id").InnerText;
        await powerReview.RunRuleAsync(t => QualityRules.AddAsync(fresh, qualityId, 1, true, t)); await settle();
        string bonuses = QualityDescriptionRules.Modifiers(fresh, fresh.Qualities.Single());
        powerReview.ShowSectionCommand.Execute(CharacterSection.Improvements); await settle();
        var refresh = window.GetVisualDescendants().OfType<Button>().Single(x => x.Content as string == "Refresh source bonuses");
        Click(window, refresh); var refreshing = ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)refresh.Command).ExecutionTask;
        if (refreshing == null) throw new InvalidOperationException("Source refresh button did not start.");
        await refreshing; await settle();
        if (QualityDescriptionRules.Modifiers(fresh, fresh.Qualities.Single()) != bonuses) throw new InvalidOperationException("UI refresh changed the quality's actual bonuses.");
        Save(window, output, "73-source-bonuses-refreshed");
        powerReview.IsDirty = false; await main.CloseCharacterCommand.ExecuteAsync(powerReview); main.CurrentPage = runner;
        var cyberSource = Improvement.ImprovementSource.Cyberware;
        var standard = (await runner.Character.GetGradesListAsync(cyberSource)).First(x => x.Name == "Standard");
        var skilljackNode = runner.Character.LoadData("cyberware.xml").SelectSingleNode("/chummer/cyberwares/cyberware[name='Skilljack']");
        var skilljack = new Chummer.Backend.Equipment.Cyberware(runner.Character);
        await skilljack.CreateAsync(skilljackNode, standard, cyberSource, 2, new System.Collections.Generic.List<Chummer.Backend.Equipment.Weapon>(), new System.Collections.Generic.List<Chummer.Backend.Equipment.Vehicle>());
        await runner.Character.Cyberware.AddAsync(skilljack);
        string skilljackId = skilljack.InternalId;
        runner.Character.Improvements.Single(x => x.SourceName == skilljackId && x.ImproveType == Improvement.ImprovementType.SkillsoftAccess).Value = 0;
        string migrationFile = Path.Combine(output, "migration-review.chum5");
        if (!await runner.Character.SaveAsync(migrationFile, false, false)) throw new InvalidOperationException("Could not prepare the migration fixture.");
        runner.IsDirty = false; await main.CloseCharacterCommand.ExecuteAsync(runner);
        await main.OpenCharacterFileAsync(migrationFile); await settle();
        var repaired = (CharacterViewModel)main.CurrentPage;
        if (!repaired.IsDirty || repaired.Character.Improvements.Single(x => x.SourceName == skilljackId && x.ImproveType == Improvement.ImprovementType.SkillsoftAccess).Value != 2)
            throw new InvalidOperationException("Opening a legacy skilljack save did not run its queued repair.");
        Save(window, output, "74-legacy-load-repaired");
        repaired.IsDirty = false; await main.CloseCharacterCommand.ExecuteAsync(repaired);
        Console.WriteLine("Native bundled armor and metatype engine prompts, read-only previews and cancellation passed.");
    }

    public static async Task CheckInlineProfilesAndRollsAsync(MainWindow window, CharacterViewModel runner, string output, Func<Task> settle)
    {
        int qualityCount = runner.Character.Qualities.Count;
        var qualityPicker = new Chummer.Desktop.Views.Dialogs.QualityPickerWindow(runner.Character);
        qualityPicker.Show(window); await settle();
        var qualityList = qualityPicker.GetVisualDescendants().OfType<ListBox>().Single();
        qualityList.SelectedIndex = 0; await settle();
        var description = qualityPicker.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Name == "QualityPreviewDescription");
        if (string.IsNullOrWhiteSpace(description.Text)) throw new InvalidOperationException("Quality picker has no selected explanation.");
        var hoverRow = qualityPicker.GetVisualDescendants().OfType<Grid>().First(x => ToolTip.GetTip(x) is StackPanel);
        var hoverPoint = hoverRow.TranslatePoint(new Point(20, 8), qualityPicker)!.Value;
        qualityPicker.MouseMove(hoverPoint, RawInputModifiers.None); await Task.Delay(900); await settle();
        if (!ToolTip.GetIsOpen(hoverRow)) throw new InvalidOperationException("Quality picker hover did not open its explanation.");
        Save(qualityPicker, output, "59-quality-picker-explanation");
        qualityPicker.Close(); await settle();
        if (runner.Character.Qualities.Count != qualityCount) throw new InvalidOperationException("Quality preview changed the runner.");
        runner.ShowSectionCommand.Execute(CharacterSection.Skills); await settle();
        foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(x => (x.Classes.Contains("nav") || x.Classes.Contains("section") || x.Classes.Contains("seg"))
            && x.IsVisible && x.GetVisualAncestors().OfType<Control>().All(c => c.IsVisible)).ToArray()) await CheckHoverAsync(window, button);
        runner.ShowSectionCommand.Execute(CharacterSection.Network); await settle();
        var card = window.GetVisualDescendants().OfType<Button>().First(x => x.Classes.Contains("contactCard") && x.IsVisible);
        Click(window, card); await settle();
        var profile = window.GetVisualDescendants().OfType<ContactProfileView>().Single();
        if (window.OwnedWindows.Any() || !runner.HasContactProfile) throw new InvalidOperationException("Profile did not stay inside the character page.");
        Save(window, output, "60-inline-profile");
        window.Width = 1920; window.Height = 1080; await settle();
        Save(window, output, "60-inline-profile-wide");
        window.Width = 980; window.Height = 720; await settle();
        Save(window, output, "60-inline-profile-compact");
        window.Width = 1440; window.Height = 900; await settle();
        var art = profile.GetVisualDescendants().OfType<Image>().Single(x => x.Name == "ContactProfileArtwork");
        if (art.Source is not Avalonia.Media.Imaging.Bitmap bitmap || bitmap.PixelSize.Width <= 256) throw new InvalidOperationException("Original contact art was not loaded.");
        Click(window, profile.GetVisualDescendants().OfType<Button>().Single(x => x.Content as string == "Enlarge artwork")); await settle();
        if (window.OwnedWindows.Any() || !profile.GetVisualDescendants().OfType<Image>().Any(x => x.Name == "ContactArtworkViewer")) throw new InvalidOperationException("Artwork inspection opened another window.");
        Save(window, output, "61-inline-artwork");
        Click(window, profile.GetVisualDescendants().OfType<Button>().Single(x => x.Content as string == "← Back to profile")); await settle();
        Click(window, profile.GetVisualDescendants().OfType<Button>().Single(x => x.Content as string == "← Contacts")); await settle();
        if (runner.HasContactProfile) throw new InvalidOperationException("Back did not restore the contact list.");
        window.Width = 980; window.Height = 620; await settle();
        var navigation = window.GetVisualDescendants().OfType<ToggleButton>().Single(x => x.Name == "DiceNavigation");
        var navPoint = navigation.TranslatePoint(new Point(1, navigation.Bounds.Height - 1), window)!.Value;
        if (navPoint.Y > window.ClientSize.Height - 26 || navigation.Bounds.Height < 20) throw new InvalidOperationException("Roll picker navigation is clipped at minimum window height.");
        Click(window, window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "CharacterRollPicker"));
        var main = MainWindowViewModel.Current; var dice = main.Dice; await dice.WaitForCatalogAsync(); await settle();
        if (!ReferenceEquals(main.CurrentPage, dice) || !ReferenceEquals(dice.SelectedCharacter, runner)) throw new InvalidOperationException("Sheet roll picker opened the wrong runner.");
        var picker = window.GetVisualDescendants().OfType<ComboBox>().Single(x => x.Name == "RollPicker");
        if (!picker.IsVisible || picker.Bounds.Height < 20 || dice.RollChoices.Count < 30) throw new InvalidOperationException("Dice action picker is missing or empty.");
        int edge = runner.Character.EdgeUsed;
        picker.SelectedItem = dice.RollChoices.Single(x => x.Action.Name == Chummer.Rolls.RollCatalog.ComposureRoll); await settle();
        if (dice.Pool != runner.Character.CHA.TotalValue + runner.Character.WIL.TotalValue || dice.SelectedRoll?.Action.Name != Chummer.Rolls.RollCatalog.ComposureRoll)
            throw new InvalidOperationException("Picker did not evaluate the selected action for this runner.");
        if (runner.Character.EdgeUsed != edge) throw new InvalidOperationException("Merely selecting a roll spent Edge.");
        Save(window, output, "62-roll-picker-compact");
        string selectedWeaponId = dice.SelectedWeapon?.Id;
        var contextWeapon = new Chummer.Backend.Equipment.Weapon(runner.Character) { Name = "Context refresh probe" };
        var childWeapon = new Chummer.Backend.Equipment.Weapon(runner.Character) { Name = "Underbarrel context probe" };
        var contextVehicle = new Chummer.Backend.Equipment.Vehicle(runner.Character) { Name = "Context vehicle" };
        var mountedWeapon = new Chummer.Backend.Equipment.Weapon(runner.Character) { Name = "Mounted context probe" };
        await runner.RunRuleAsync(async token =>
        {
            await contextWeapon.Children.AddAsync(childWeapon, token); await runner.Character.Weapons.AddAsync(contextWeapon, token);
            await contextVehicle.Weapons.AddAsync(mountedWeapon, token); await runner.Character.Vehicles.AddAsync(contextVehicle, token);
            return RuleResult.Changed;
        });
        runner.OpenRollPickerCommand.Execute(null); await dice.WaitForCatalogAsync(); await settle();
        if (!dice.Weapons.Any(x => x.Id == contextWeapon.InternalId) || !dice.Weapons.Any(x => x.Id == childWeapon.InternalId) || !dice.Weapons.Any(x => x.Id == mountedWeapon.InternalId))
            throw new InvalidOperationException("Returning to the picker did not refresh root, underbarrel and vehicle weapons.");
        if (selectedWeaponId != null && dice.SelectedWeapon?.Id != selectedWeaponId) throw new InvalidOperationException("Context refresh lost the selected weapon.");
        dice.SelectedWeapon = dice.Weapons.Single(x => x.Id == childWeapon.InternalId);
        await runner.RunRuleAsync(async token =>
        {
            await runner.Character.Weapons.RemoveAsync(contextWeapon, token); await runner.Character.Vehicles.RemoveAsync(contextVehicle, token);
            contextWeapon.Dispose(); contextVehicle.Dispose(); return RuleResult.Changed;
        });
        runner.OpenRollPickerCommand.Execute(null); await dice.WaitForCatalogAsync(); await settle();
        if (dice.Weapons.Any(x => x.Id == childWeapon.InternalId) || ReferenceEquals(dice.SelectedWeapon?.Value, childWeapon)) throw new InvalidOperationException("The picker retained a removed/disposed weapon.");
        if (runner.Character.EdgeUsed != edge) throw new InvalidOperationException("Refreshing equipment contexts spent Edge.");
        Click(window, window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "LocalDiceNavigation")); await settle();
        if (!dice.ShowLocalRoller || dice.LocalDice.Dice != Math.Clamp(dice.Pool, 0, 100)) throw new InvalidOperationException("Local dice did not inherit the selected pool.");
        dice.LocalDice.Dice = 12; dice.LocalDice.RollCommand.Execute(null); await settle();
        if (!dice.LocalDice.HasResult || dice.LocalDice.Faces.Count != 12) throw new InvalidOperationException("Local roller did not display its dice.");
        if (runner.Character.EdgeUsed != edge) throw new InvalidOperationException("Generic local roller spent character Edge.");
        Save(window, output, "64-local-dice-compact");
        window.Width = 1440; window.Height = 900; await settle(); Save(window, output, "65-local-dice-wide");
        Click(window, window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "InitiativeNavigation")); await settle();
        var initiative = dice.Initiative; initiative.UseRunnerCommand.Execute(null);
        if (initiative.Basis != runner.Character.InitiativeValue || initiative.Dice != runner.Character.InitiativeDice) throw new InvalidOperationException("Initiative import does not match the engine.");
        initiative.Automatic = false; initiative.Dice = 2; initiative.ManualTotal = 7; initiative.Basis = 10; initiative.Name = "Fast runner";
        Click(window, window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "InitiativeAdd")); await settle();
        initiative.Name = "Guard"; initiative.Basis = 4; initiative.ManualTotal = 5;
        Click(window, window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "InitiativeAdd")); await settle();
        if (initiative.Combatants.Count != 2 || initiative.Session.Current?.Name != "Fast runner") throw new InvalidOperationException("Initiative combatants were not added in score order.");
        Save(window, output, "66-initiative-wide");
        Click(window, window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "InitiativeNext")); await settle();
        if (initiative.Session.Current?.Name != "Guard") throw new InvalidOperationException("Next combatant did not advance initiative.");
        window.Width = 980; window.Height = 720; await settle(); Save(window, output, "67-initiative-compact");
        dice.ShowActionsCommand.Execute(null); await settle();
        using var other = new Character { SettingsKey = runner.Character.SettingsKey, Name = "Other runner" };
        await CharacterRegistry.OpenCharacters.AddAsync(other);
        await main.OpenCharacterAsync(other); var otherView = (CharacterViewModel)main.CurrentPage;
        otherView.OpenRollPickerCommand.Execute(null); await dice.WaitForCatalogAsync(); await settle();
        if (!ReferenceEquals(dice.SelectedCharacter, otherView)) throw new InvalidOperationException("Roll picker retained the previously selected runner.");
        runner.OpenRollPickerCommand.Execute(null); otherView.OpenRollPickerCommand.Execute(null); runner.OpenRollPickerCommand.Execute(null);
        await dice.WaitForCatalogAsync(); await settle();
        if (!ReferenceEquals(dice.SelectedCharacter, runner) || dice.Weapons.Any(x => x.Value is Chummer.Backend.Equipment.Weapon weapon && !ReferenceEquals(weapon.CharacterObject, runner.Character))
            || runner.Character.Weapons.Count > 0 && !dice.Weapons.Any(x => x.Id == runner.Character.Weapons.First().InternalId))
            throw new InvalidOperationException("Rapid runner switching applied stale equipment choices.");
        await main.CloseCharacterCommand.ExecuteAsync(otherView); await dice.WaitForCatalogAsync();
        main.CurrentPage = runner; runner.OpenRollPickerCommand.Execute(null); await dice.WaitForCatalogAsync(); await settle();
        if (!ReferenceEquals(dice.SelectedCharacter, runner)) throw new InvalidOperationException("Returning to the runner did not restore its roll catalog.");
        main.CurrentPage = runner; window.Width = 1440; window.Height = 900; await settle();
        runner.SelectedContactProfile = runner.ContactCards.First(); await settle(); Save(window, output, "63-inline-profile-wide");
        Console.WriteLine("Inline contact/artwork navigation, visible dice access, action selection, correct pools, no Edge spending and runner switching passed.");
    }

    private static async Task CheckHoverAsync(MainWindow window, Button button)
    {
        if (button.Bounds.Width < 1 || button.Bounds.Height < 1) return;
        Rect bounds = button.Bounds;
        var origin = button.TranslatePoint(default, window) ?? throw new InvalidOperationException("No pointer coordinates.");
        Rect visible = new Rect(origin, bounds.Size).Intersect(new Rect(window.ClientSize));
        foreach (var ancestor in button.GetVisualAncestors().Where(x => x.ClipToBounds))
        {
            var ancestorOrigin = ancestor.TranslatePoint(default, window);
            if (ancestorOrigin.HasValue) visible = visible.Intersect(new Rect(ancestorOrigin.Value, ancestor.Bounds.Size));
        }
        // Scrollable navigation intentionally clips offscreen tabs. Probe only the exposed area,
        // including its edges, while still requiring stable control bounds and pointer ownership.
        if (visible.Width < 1 || visible.Height < 1) return;
        foreach (double x in new[] { visible.Center.X, visible.Left + 0.5, visible.Right - 0.5 })
            for (int i = 0; i < 6; ++i)
            {
                var point = new Point(x, visible.Center.Y);
                window.MouseMove(point, RawInputModifiers.None); Dispatcher.UIThread.RunJobs(); await Task.Delay(15);
                if (!button.IsPointerOver || button.Bounds != bounds) throw new InvalidOperationException($"Hover lost {button.Content} ({button.CommandParameter}) at {point}, exposed {visible}, bounds {bounds}.");
            }
        window.MouseMove(new Point(1, 1), RawInputModifiers.None);
    }
    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window) ?? throw new InvalidOperationException("No click coordinates.");
        window.MouseMove(point, RawInputModifiers.None); window.MouseDown(point, MouseButton.Left, RawInputModifiers.None); window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }
    private static void CheckLedgerWidth(MainWindow window, string name)
    {
        var body = window.GetVisualDescendants().OfType<Control>().Single(x => x.Name == name);
        if (body.Bounds.Width < (window.ClientSize.Width - 232) * 0.9) throw new InvalidOperationException(name + " did not fill the available width.");
    }
    private static void Save(Window window, string output, string name)
    {
        Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.CaptureRenderedFrame()?.Save(Path.Combine(output, name + ".png")); Console.WriteLine("saved " + name);
    }
}
