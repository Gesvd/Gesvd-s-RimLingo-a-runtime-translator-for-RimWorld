using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using RimWorld;
using UnityEngine;
using Verse;

namespace RuntimeTranslator
{
    public sealed partial class RuntimeTranslatorMod
    {
        private Vector2 glossaryScrollPosition;
        private string glossarySearch = string.Empty;
        private string glossarySource = string.Empty;
        private string glossaryTranslation = string.Empty;
        private int glossaryPage;
        private bool glossaryScanning;
        private CancellationTokenSource glossaryTranslationCancellation;
        private string glossaryTranslationStatus = string.Empty;
        private int glossaryTranslationsApplied;

        private void DrawGlossaryTab(Rect inRect)
        {
            List<GlossaryTerm> terms = Glossary.ForLanguages(Settings, Settings.SourceLanguage, Settings.TargetLanguage)
                .Where(term => ContainsIgnoreCase(term.Source, glossarySearch) || ContainsIgnoreCase(term.Translation, glossarySearch))
                .OrderByDescending(term => term.Count).ThenBy(term => term.Source).ToList();
            int pages = Math.Max(1, (terms.Count + 29) / 30);
            glossaryPage = Math.Max(0, Math.Min(glossaryPage, pages - 1));
            Listing_Standard listing = BeginScroll(inRect, ref glossaryScrollPosition, 370f + Math.Min(30, terms.Count) * 34f);
            listing.Label(UiText.Get("Glossary: ") + Settings.SourceLanguage + " -> " + Settings.TargetLanguage);
            listing.Label(UiText.Get("Scan English words in selected mods. Fill a translation to enforce it; blank means inactive. Changes affect the next translation task."));
            if (glossaryScanning) listing.Label(UiText.Get("Scanning selected mod files..."));
            else if (listing.ButtonText(UiText.Get("Scan selected mods (English candidates)"))) StartGlossaryScan();
            if (glossaryTranslationCancellation != null)
            {
                if (listing.ButtonText(UiText.Get("Cancel glossary translation"))) glossaryTranslationCancellation.Cancel();
            }
            else if (!glossaryScanning && listing.ButtonText(UiText.Get("Translate missing glossary terms (current provider)"))) StartGlossaryTranslation();
            listing.Label(glossaryTranslationStatus);
            string nextSearch = TextFieldLabeled(listing, UiText.Get("Search terms"), glossarySearch);
            if (nextSearch != glossarySearch) { glossarySearch = nextSearch; glossaryPage = 0; }
            glossarySource = TextFieldLabeled(listing, UiText.Get("New word / phrase"), glossarySource);
            glossaryTranslation = TextFieldLabeled(listing, UiText.Get("Preferred translation"), glossaryTranslation);
            if (listing.ButtonText(UiText.Get("Add / update term")))
            {
                string source = glossarySource.Trim();
                if (!TextProtection.IsGlossarySource(source))
                {
                    Messages.Message(UiText.Get("Enter a plain word or phrase without format tags or placeholders."), MessageTypeDefOf.RejectInput, false);
                }
                else
                {
                    GlossaryTerm term = Glossary.ForLanguages(Settings, Settings.SourceLanguage, Settings.TargetLanguage)
                        .FirstOrDefault(item => string.Equals(item.Source, source, StringComparison.OrdinalIgnoreCase));
                    if (term == null)
                    {
                        term = new GlossaryTerm { Source = source, SourceLanguage = Settings.SourceLanguage, TargetLanguage = Settings.TargetLanguage };
                        Settings.GlossaryTerms.Add(term);
                    }
                    term.Translation = glossaryTranslation.Trim();
                    glossarySource = glossaryTranslation = string.Empty;
                    WriteSettings();
                }
            }
            listing.GapLine();
            Rect controls = listing.GetRect(30f);
            if (Widgets.ButtonText(new Rect(controls.x, controls.y, 80f, controls.height), UiText.Get("Previous"))) glossaryPage = Math.Max(0, glossaryPage - 1);
            Widgets.Label(new Rect(controls.x + 90f, controls.y, controls.width - 180f, controls.height),
                UiText.Get("Page ") + (glossaryPage + 1) + " / " + pages + UiText.Get("   Terms: ") + terms.Count + UiText.Get("   Source (occurrences) / Translation"));
            if (Widgets.ButtonText(new Rect(controls.xMax - 80f, controls.y, 80f, controls.height), UiText.Get("Next"))) glossaryPage = Math.Min(pages - 1, glossaryPage + 1);
            foreach (GlossaryTerm term in terms.Skip(glossaryPage * 30).Take(30))
            {
                Rect row = listing.GetRect(32f);
                float sourceWidth = row.width * 0.35f;
                Widgets.Label(new Rect(row.x, row.y, sourceWidth, row.height), term.Source + " (" + term.Count + ")");
                term.Translation = Widgets.TextField(new Rect(row.x + sourceWidth + 6f, row.y, row.width - sourceWidth - 86f, row.height), term.Translation ?? string.Empty);
                if (Widgets.ButtonText(new Rect(row.xMax - 74f, row.y, 74f, row.height), UiText.Get("Remove"))) Settings.GlossaryTerms.Remove(term);
            }
            EndScroll(listing);
        }

        private void StartGlossaryTranslation()
        {
            RuntimeTranslatorSettings snapshot = Settings.Snapshot();
            int total = GlossaryTranslator.MissingTerms(snapshot).Count;
            if (total == 0)
            {
                Messages.Message(UiText.Get("No missing glossary translations for this language pair. Scan mods first or add terms."), MessageTypeDefOf.RejectInput, false);
                return;
            }
            CancellationTokenSource job = CancellationTokenSource.CreateLinkedTokenSource(TranslationLifetime.Token);
            glossaryTranslationCancellation = job;
            glossaryTranslationsApplied = 0;
            glossaryTranslationStatus = UiText.Get("Translating glossary: 0 / ") + total;
            Task.Run(async () =>
            {
                TranslationStatus result;
                try
                {
                    ITranslationProvider provider = ProviderFactory.Create(GlossaryTranslator.ProviderSettings(snapshot));
                    result = await GlossaryTranslator.TranslateAsync(snapshot, provider, () => TranslationManager.CurrentBatchSize,
                        batch => TranslationManager.OnMainThread(() =>
                        {
                            glossaryTranslationsApplied += GlossaryTranslator.ApplyBatch(Settings, batch);
                            glossaryTranslationStatus = UiText.Get("Glossary translations saved: ") + glossaryTranslationsApplied + " / " + total;
                            base.WriteSettings();
                        }), job.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    result = new TranslationStatus { State = TranslationState.Failed, LastError = ex.Message };
                }
                TranslationManager.OnMainThread(() =>
                {
                    glossaryTranslationStatus = UiText.Get("Glossary: ") + result.State + UiText.Get(". Saved: ") + glossaryTranslationsApplied +
                        UiText.Get("; Failed: ") + result.Failed + (string.IsNullOrEmpty(result.LastError) ? "" : ". " + result.LastError);
                    glossaryTranslationCancellation = null;
                    job.Dispose();
                    base.WriteSettings();
                    Messages.Message(glossaryTranslationStatus, result.State == TranslationState.Completed && result.Failed == 0
                        ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.RejectInput, false);
                });
            });
        }

        private void StartGlossaryScan()
        {
            if (Glossary.LanguageKey(Settings.SourceLanguage) != "en")
            {
                Messages.Message(UiText.Get("Automatic word discovery currently supports English / Auto sources. Other languages can use manual terms."), MessageTypeDefOf.RejectInput, false);
                return;
            }
            List<string> folders = ModTextScanner.SelectedContentFolders(Settings);
            if (folders.Count == 0)
            {
                Messages.Message(UiText.Get("Select at least one loaded mod with readable files on the Mods tab."), MessageTypeDefOf.RejectInput, false);
                return;
            }
            string source = Settings.SourceLanguage;
            string target = Settings.TargetLanguage;
            glossaryScanning = true;
            Task.Run(() =>
            {
                try
                {
                    Dictionary<string, int> counts = Glossary.ScanFiles(folders, TranslationLifetime.Token);
                    TranslationManager.OnMainThread(() =>
                    {
                        glossaryScanning = false;
                        Glossary.Merge(Settings, counts, source, target);
                        WriteSettings();
                        Messages.Message(UiText.Get("Glossary scan finished. Words occurring at least twice were added; existing translations were kept. Unreadable files, if any, are listed in the log."), MessageTypeDefOf.TaskCompletion, false);
                    });
                }
                catch (Exception ex)
                {
                    TranslationManager.OnMainThread(() =>
                    {
                        glossaryScanning = false;
                        Messages.Message(UiText.Get("Glossary scan failed: ") + ex.Message, MessageTypeDefOf.RejectInput, false);
                    });
                }
            });
        }
    }
}