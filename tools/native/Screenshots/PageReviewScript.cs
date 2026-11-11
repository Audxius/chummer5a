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
        var profile = window.OwnedWindows.OfType<ContactProfileWindow>().Single();
        var profileArt = profile.GetVisualDescendants().OfType<Image>().Single(x => x.Name == "ContactProfileArtwork");
        if (profileArt.Bounds.Width < 250 || first.HasPortrait && ((Avalonia.Media.Imaging.Bitmap)profileArt.Source).PixelSize.Width <= 256)
            throw new InvalidOperationException("Contact profile does not show large original artwork.");
        Save(profile, output, "43-contact-profile");
        if (first.IsEditable)
        {
            Click(profile, profile.GetVisualDescendants().OfType<Button>().Single(x => x.Content as string == "Edit profile…")); await settle();
            var editor = profile.OwnedWindows.Single();
            if (!editor.Title.StartsWith("Edit ", StringComparison.Ordinal)) throw new InvalidOperationException("Contact editor did not belong to its profile window.");
            editor.Close(); await settle();
            if (profile.Profile.Connection != first.Connection) throw new InvalidOperationException("Cancelling contact editing changed its rating.");
        }
        if (first.IsEditable)
        {
            int expected = first.Connection < first.MaxConnection ? first.Connection + 1 : first.Connection - 1;
            var change = profile.GetVisualDescendants().OfType<Button>().First(x => x.Content as string == (first.Connection < first.MaxConnection ? "+" : "−"));
            Click(profile, change);
            await settle();
            var refreshed = runner.ContactCards.Single(x => x.Id == id);
            if (refreshed.Connection != expected || profile.Profile.Connection != expected) throw new InvalidOperationException("Profile rating change did not refresh both views.");
        }
        profile.Close(); await settle();
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
                var cohortProfile = window.OwnedWindows.OfType<ContactProfileWindow>().Single();
                if (cohortProfile.Profile.LinkedStats.Count != companion.AttributeSection.AttributeList.Count)
                    throw new InvalidOperationException("Linked cohort attributes missing from profile.");
                Save(cohortProfile, output, "43-linked-cohort-profile");
                Click(cohortProfile, cohortProfile.GetVisualDescendants().OfType<Button>().Single(x => x.Content as string == "Open full character sheet")); await settle();
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

    private static async Task CheckHoverAsync(MainWindow window, Button button)
    {
        if (button.Bounds.Width < 1 || button.Bounds.Height < 1) return;
        Rect bounds = button.Bounds;
        foreach (double x in new[] { button.Bounds.Width / 2, 0.5, button.Bounds.Width - 0.5 })
            for (int i = 0; i < 6; ++i)
            {
                var point = button.TranslatePoint(new Point(x, button.Bounds.Height / 2), window);
                if (!point.HasValue) throw new InvalidOperationException("No pointer coordinates for navigation control.");
                window.MouseMove(point.Value, RawInputModifiers.None); Dispatcher.UIThread.RunJobs(); await Task.Delay(15);
                if (!button.IsPointerOver || button.Bounds != bounds) throw new InvalidOperationException("Hover moved or lost a navigation/filter hit target.");
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
