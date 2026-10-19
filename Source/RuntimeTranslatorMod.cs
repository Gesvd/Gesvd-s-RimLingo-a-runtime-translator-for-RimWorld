using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RuntimeTranslator
{
    public sealed partial class RuntimeTranslatorMod : Mod
    {
        private enum SettingsTab
        {
            Task,
            Mods,
            Provider,
            Preview,
            Status,
            Glossary
        }

        public static RuntimeTranslatorMod Instance;
        public static RuntimeTranslatorSettings Settings;

        private SettingsTab activeTab = SettingsTab.Task;
        private Vector2 taskScrollPosition;
        private Vector2 modsScrollPosition;
        private Vector2 providerScrollPosition;
        private Vector2 previewScrollPosition;
        private Vector2 statusScrollPosition;
        private string modFilter = string.Empty;
        private string previewSearch = string.Empty;
        private string replaceFrom = string.Empty;
        private string replaceTo = string.Empty;
        private readonly HashSet<string> expandedPreviewMods = new HashSet<string>();
        private readonly HashSet<string> replacedPreviewKeys = new HashSet<string>();

        public RuntimeTranslatorMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<RuntimeTranslatorSettings>();
            TranslationManager.Initialize(Settings);
            UiText.Initialize(content);
            BundledLibreTranslate.Initialize(content.RootDir, System.IO.Path.Combine(GenFilePaths.ConfigFolderPath, "RuntimeTranslator", "LocalEngine"));
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                GameObject updater = new GameObject("RuntimeTranslator");
                UnityEngine.Object.DontDestroyOnLoad(updater);
                updater.AddComponent<TranslationUpdate>();
            });
        }

        public override string SettingsCategory()
        {
            return UiText.Get("Runtime Translator");
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Rect tabsRect = new Rect(inRect.x, inRect.y, inRect.width, 34f);
            DrawTabs(tabsRect);

            Rect contentRect = new Rect(inRect.x, tabsRect.yMax + 8f, inRect.width, inRect.height - tabsRect.height - 8f);
            switch (activeTab)
            {
                case SettingsTab.Task:
                    DrawTaskTab(contentRect);
                    break;
                case SettingsTab.Mods:
                    DrawModsTab(contentRect);
                    break;
                case SettingsTab.Provider:
                    DrawProviderTab(contentRect);
                    break;
                case SettingsTab.Preview:
                    DrawPreviewTab(contentRect);
                    break;
                case SettingsTab.Glossary:
                    DrawGlossaryTab(contentRect);
                    break;
                case SettingsTab.Status:
                    DrawStatusTab(contentRect);
                    break;
            }
        }

        private void DrawTabs(Rect rect)
        {
            const float gap = 6f;
            float width = (rect.width - gap * 5f) / 6f;
            DrawTabButton(new Rect(rect.x, rect.y, width, rect.height), SettingsTab.Task, UiText.Get("Task"));
            DrawTabButton(new Rect(rect.x + (width + gap), rect.y, width, rect.height), SettingsTab.Mods, UiText.Get("Mods (") + Settings.SelectedPackageIds.Count + ")");
            DrawTabButton(new Rect(rect.x + (width + gap) * 2f, rect.y, width, rect.height), SettingsTab.Provider, UiText.Get("Provider"));
            DrawTabButton(new Rect(rect.x + (width + gap) * 3f, rect.y, width, rect.height), SettingsTab.Preview, UiText.Get("Preview"));
            DrawTabButton(new Rect(rect.x + (width + gap) * 4f, rect.y, width, rect.height), SettingsTab.Status, UiText.Get("Status"));
            DrawTabButton(new Rect(rect.x + (width + gap) * 5f, rect.y, width, rect.height), SettingsTab.Glossary, UiText.Get("Glossary"));
        }

        private void DrawTabButton(Rect rect, SettingsTab tab, string label)
        {
            bool selected = activeTab == tab;
            Color oldColor = GUI.color;
            if (selected)
            {
                GUI.color = new Color(0.72f, 0.86f, 1f);
            }
            if (Widgets.ButtonText(rect, label))
            {
                activeTab = tab;
            }
            GUI.color = oldColor;
        }

        private void DrawTaskTab(Rect inRect)
        {
            Listing_Standard listing = BeginScroll(inRect, ref taskScrollPosition, 420f);
            DrawLanguageSection(listing);
            listing.GapLine();
            DrawTaskControls(listing);
            listing.GapLine();
            DrawStatus(listing);
            listing.Gap();
            listing.Label(UiText.Get("Selected mods: ") + Settings.SelectedPackageIds.Count);
            EndScroll(listing);
        }

        private void DrawModsTab(Rect inRect)
        {
            Listing_Standard listing = BeginScroll(inRect, ref modsScrollPosition, GetModsViewHeight(inRect));
            DrawModSelection(listing);
            EndScroll(listing);
        }

        private void DrawProviderTab(Rect inRect)
        {
            Listing_Standard listing = BeginScroll(inRect, ref providerScrollPosition, 520f);
            DrawProviderSection(listing);
            EndScroll(listing);
        }

        private void DrawPreviewTab(Rect inRect)
        {
            List<TranslationCacheRecord> records = GetFilteredPreviewRecords().ToList();
            Listing_Standard listing = BeginScroll(inRect, ref previewScrollPosition, GetPreviewViewHeight(inRect, records));
            DrawPreviewTools(listing, records);
            listing.GapLine();
            DrawPreviewGroups(listing, records);
            EndScroll(listing);
        }

        private void DrawStatusTab(Rect inRect)
        {
            Listing_Standard listing = BeginScroll(inRect, ref statusScrollPosition, 260f);
            DrawStatus(listing);
            listing.GapLine();
            if (listing.ButtonText(UiText.Get("Export cached translations")))
            {
                TranslationManager.ExportCurrentCache();
            }
            EndScroll(listing);
        }

        private static Listing_Standard BeginScroll(Rect inRect, ref Vector2 scrollPosition, float minHeight)
        {
            Listing_Standard listing = new Listing_Standard();
            Rect viewRect = new Rect(0f, 0f, inRect.width - 18f, Math.Max(minHeight, inRect.height - 4f));
            Widgets.BeginScrollView(inRect, ref scrollPosition, viewRect);
            listing.Begin(viewRect);
            return listing;
        }

        private static void EndScroll(Listing_Standard listing)
        {
            listing.End();
            Widgets.EndScrollView();
        }

        public override void WriteSettings()
        {
            if (TranslationLifetime.IsStopping) return;
            base.WriteSettings();
            TranslationManager.SaveCache();
        }

        private void DrawLanguageSection(Listing_Standard listing)
        {
            listing.Label(UiText.Get("Languages"));
            Settings.SourceLanguage = TextFieldLabeled(listing, UiText.Get("Source language"), Settings.SourceLanguage);
            Settings.TargetLanguage = TextFieldLabeled(listing, UiText.Get("Target language"), Settings.TargetLanguage);
            Settings.BatchSize = IntSliderLabeled(listing, UiText.Get("Batch size (next request)"), Settings.BatchSize, 1, 30);
            Settings.SkipCompletedTranslations = listing.CheckboxLabeledReturn(UiText.Get("Skip completed translations (reuse valid cache)"), Settings.SkipCompletedTranslations);
            Settings.ApplyRuntimeTranslations = listing.CheckboxLabeledReturn(UiText.Get("Apply translations to loaded Defs"), Settings.ApplyRuntimeTranslations);
            Settings.ExportLanguageFiles = listing.CheckboxLabeledReturn(UiText.Get("Export RimWorld language files"), Settings.ExportLanguageFiles);
        }

        private void DrawProviderSection(Listing_Standard listing)
        {
            listing.Label(UiText.Get("Provider"));
            if (listing.ButtonText(UiText.Get("Provider: ") + Settings.ProviderKind))
            {
                List<FloatMenuOption> options = Enum.GetValues(typeof(ProviderKind))
                    .Cast<ProviderKind>()
                    .Select(kind => new FloatMenuOption(kind.ToString(), () => Settings.ProviderKind = kind))
                    .ToList();
                Find.WindowStack.Add(new FloatMenu(options));
            }

            listing.GapLine();
            switch (Settings.ProviderKind)
            {
                case ProviderKind.OpenAiCompatible:
                    DrawOpenAiProviderSettings(listing);
                    break;
                case ProviderKind.LibreTranslate:
                    DrawLibreTranslateProviderSettings(listing);
                    break;
                case ProviderKind.LaraTranslate:
                    DrawLaraTranslateProviderSettings(listing);
                    break;
            }
        }

        private void DrawOpenAiProviderSettings(Listing_Standard listing)
        {
            listing.Label(UiText.Get("OpenAI-compatible"));
            Settings.OpenAiBaseUrl = TextFieldLabeled(listing, UiText.Get("API URL"), Settings.OpenAiBaseUrl);
            Settings.OpenAiModel = TextFieldLabeled(listing, UiText.Get("Model"), Settings.OpenAiModel);
            Settings.OpenAiApiKey = PasswordFieldLabeled(listing, UiText.Get("API key"), Settings.OpenAiApiKey);
            Settings.OpenAiRules = TextAreaLabeled(listing, UiText.Get("Rules"), Settings.OpenAiRules, 96f);
        }

        private void DrawLibreTranslateProviderSettings(Listing_Standard listing)
        {
            listing.Label(UiText.Get("LibreTranslate"));
            Settings.UseBundledLibreTranslate = listing.CheckboxLabeledReturn(UiText.Get("Use bundled offline engine (Windows, English to Chinese)"), Settings.UseBundledLibreTranslate);
            if (Settings.UseBundledLibreTranslate)
            {
                listing.Label(UiText.Get("Starts on the first translation request. No API key required."));
                listing.Label(BundledLibreTranslate.Status);
                listing.Label(UiText.Get("License: AGPL-3.0. Source and notices: LocalEngine/sources.zip"));
                return;
            }
            Settings.LibreTranslateUrl = TextFieldLabeled(listing, UiText.Get("Web/API URL"), Settings.LibreTranslateUrl);
            Settings.LibreTranslateApiKey = PasswordFieldLabeled(listing, UiText.Get("API key"), Settings.LibreTranslateApiKey);
            listing.Label(UiText.Get("LibreTranslate does not use a model ID in this adapter."));
        }

        private void DrawLaraTranslateProviderSettings(Listing_Standard listing)
        {
            listing.Label(UiText.Get("Lara Translate SDK Bridge"));
            Settings.LaraTranslateUrl = TextFieldLabeled(listing, UiText.Get("Bridge URL"), Settings.LaraTranslateUrl);
            Settings.LaraTranslateModel = TextFieldLabeled(listing, UiText.Get("Content type"), Settings.LaraTranslateModel);
            Settings.LaraTranslateApiId = TextFieldLabeled(listing, UiText.Get("Access Key ID"), Settings.LaraTranslateApiId);
            Settings.LaraTranslateApiKey = PasswordFieldLabeled(listing, UiText.Get("Access Key Secret"), Settings.LaraTranslateApiKey);
            Settings.LaraTranslateRules = TextAreaLabeled(listing, UiText.Get("Instructions"), Settings.LaraTranslateRules, 96f);
            listing.Label(UiText.Get("Lara's official SDK handles authentication and encryption. This RimWorld mod calls a local SDK bridge because the official .NET SDK targets modern .NET, while RimWorld mods target .NET Framework."));
        }

        private void DrawModSelection(Listing_Standard listing)
        {
            listing.Label(UiText.Get("Loaded mods"));
            modFilter = TextFieldLabeled(listing, UiText.Get("Search"), modFilter);
            listing.Label(UiText.Get("Selected: ") + Settings.SelectedPackageIds.Count);

            Rect row = listing.GetRect(32f);
            float buttonWidth = row.width / 2f - 4f;
            if (Widgets.ButtonText(new Rect(row.x, row.y, buttonWidth, row.height), UiText.Get("Select visible")))
            {
                foreach (ModContentPack pack in GetFilteredMods())
                {
                    string packageId = pack.PackageIdPlayerFacing;
                    if (!Settings.SelectedPackageIds.Contains(packageId))
                    {
                        Settings.SelectedPackageIds.Add(packageId);
                    }
                }
            }
            if (Widgets.ButtonText(new Rect(row.x + buttonWidth + 8f, row.y, buttonWidth, row.height), UiText.Get("Clear selected")))
            {
                Settings.SelectedPackageIds.Clear();
            }
            listing.GapLine();

            foreach (ModContentPack pack in GetFilteredMods())
            {
                string packageId = pack.PackageIdPlayerFacing;
                bool selected = Settings.SelectedPackageIds.Contains(packageId);
                bool newSelected = listing.CheckboxLabeledReturn(pack.Name + " (" + packageId + ")", selected);
                if (newSelected != selected)
                {
                    if (newSelected)
                    {
                        Settings.SelectedPackageIds.Add(packageId);
                    }
                    else
                    {
                        Settings.SelectedPackageIds.Remove(packageId);
                    }
                }
            }
        }

        private IEnumerable<ModContentPack> GetFilteredMods()
        {
            IEnumerable<ModContentPack> packs = LoadedModManager.RunningMods
                .Where(pack => pack != null);

            if (!string.IsNullOrWhiteSpace(modFilter))
            {
                packs = packs.Where(pack =>
                    pack.Name.IndexOf(modFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    pack.PackageIdPlayerFacing.IndexOf(modFilter, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return packs.OrderBy(pack => pack.Name);
        }

        private float GetModsViewHeight(Rect inRect)
        {
            int count = GetFilteredMods().Count();
            return Math.Max(inRect.height - 4f, 150f + count * 30f);
        }

        private void DrawPreviewTools(Listing_Standard listing, List<TranslationCacheRecord> records)
        {
            string previousSearch = previewSearch;
            string previousFrom = replaceFrom;
            string previousTo = replaceTo;
            previewSearch = TextFieldLabeled(listing, UiText.Get("Search text or mod"), previewSearch);

            Rect replaceRow = listing.GetRect(32f);
            const float labelWidth = 82f;
            float buttonWidth = 150f;
            float fieldWidth = (replaceRow.width - labelWidth * 2f - buttonWidth - 18f) / 2f;
            Widgets.Label(new Rect(replaceRow.x, replaceRow.y, labelWidth, replaceRow.height), UiText.Get("Replace"));
            replaceFrom = Widgets.TextField(new Rect(replaceRow.x + labelWidth, replaceRow.y, fieldWidth, replaceRow.height), replaceFrom ?? string.Empty);
            Widgets.Label(new Rect(replaceRow.x + labelWidth + fieldWidth + 6f, replaceRow.y, labelWidth, replaceRow.height), UiText.Get("With"));
            replaceTo = Widgets.TextField(new Rect(replaceRow.x + labelWidth * 2f + fieldWidth + 6f, replaceRow.y, fieldWidth, replaceRow.height), replaceTo ?? string.Empty);
            if (previousSearch != previewSearch || previousFrom != replaceFrom || previousTo != replaceTo)
            {
                replacedPreviewKeys.Clear();
            }

            Rect buttonRect = new Rect(replaceRow.xMax - buttonWidth, replaceRow.y, buttonWidth, replaceRow.height);
            if (Widgets.ButtonText(buttonRect, UiText.Get("Replace next 50")))
            {
                int changed = TranslationManager.ReplaceCachedTranslationsBatch(
                    records.Select(TranslationManager.CacheRecordKey),
                    replaceFrom,
                    replaceTo,
                    50,
                    replacedPreviewKeys);
                Messages.Message(UiText.Get("Runtime Translator replaced ") + changed + UiText.Get(" cached translations."), MessageTypeDefOf.TaskCompletion, false);
            }

            int replaceRemaining = string.IsNullOrEmpty(replaceFrom) || replaceFrom == replaceTo
                ? 0
                : records.Count(record => !replacedPreviewKeys.Contains(TranslationManager.CacheRecordKey(record)) &&
                    (record.TranslatedText ?? string.Empty).Contains(replaceFrom));
            listing.Label(UiText.Get("Showing: ") + records.Count + UiText.Get(" cached translations. Replace remaining in visible results: ") + replaceRemaining);
        }

        private void DrawPreviewGroups(Listing_Standard listing, List<TranslationCacheRecord> records)
        {
            foreach (IGrouping<string, TranslationCacheRecord> group in records
                .GroupBy(record => PreviewModKey(record))
                .OrderBy(group => group.Key))
            {
                bool expanded = expandedPreviewMods.Contains(group.Key);
                string label = (expanded ? "[-] " : "[+] ") + group.First().ModName + " (" + group.First().PackageId + ") - " + group.Count();
                if (listing.ButtonText(label))
                {
                    if (expanded)
                    {
                        expandedPreviewMods.Remove(group.Key);
                    }
                    else
                    {
                        expandedPreviewMods.Add(group.Key);
                    }
                }

                if (!expanded)
                {
                    continue;
                }

                DrawPreviewHeader(listing);
                foreach (TranslationCacheRecord record in group.OrderBy(record => record.DefTypeName).ThenBy(record => record.DefName).ThenBy(record => record.FieldName))
                {
                    DrawPreviewRecord(listing, record);
                }

                listing.Gap();
            }
        }

        private void DrawPreviewHeader(Listing_Standard listing)
        {
            Rect row = listing.GetRect(24f);
            float leftWidth = row.width * 0.48f;
            float metaWidth = row.width * 0.16f;
            Widgets.Label(new Rect(row.x, row.y, metaWidth, row.height), UiText.Get("Def"));
            Widgets.Label(new Rect(row.x + metaWidth + 6f, row.y, leftWidth - metaWidth - 6f, row.height), UiText.Get("Source"));
            Widgets.Label(new Rect(row.x + leftWidth + 12f, row.y, row.width - leftWidth - 12f, row.height), UiText.Get("Translation"));
        }

        private void DrawPreviewRecord(Listing_Standard listing, TranslationCacheRecord record)
        {
            Rect row = listing.GetRect(78f);
            float leftWidth = row.width * 0.48f;
            float metaWidth = row.width * 0.16f;
            Rect metaRect = new Rect(row.x, row.y, metaWidth, row.height);
            Rect sourceRect = new Rect(row.x + metaWidth + 6f, row.y, leftWidth - metaWidth - 6f, row.height);
            Rect translatedRect = new Rect(row.x + leftWidth + 12f, row.y, row.width - leftWidth - 12f, row.height);

            Widgets.Label(metaRect, record.DefTypeName + "\n" + record.DefName + "." + record.FieldName);
            Widgets.Label(sourceRect, record.SourceText ?? string.Empty);

            string oldTranslation = record.TranslatedText ?? string.Empty;
            string newTranslation = Widgets.TextArea(translatedRect, oldTranslation);
            if (newTranslation != oldTranslation)
            {
                replacedPreviewKeys.Remove(TranslationManager.CacheRecordKey(record));
                record.TranslatedText = newTranslation;
                TranslationManager.UpdateCachedTranslation(record, newTranslation);
            }
        }

        private IEnumerable<TranslationCacheRecord> GetFilteredPreviewRecords()
        {
            IEnumerable<TranslationCacheRecord> records = TranslationManager.GetCachedTranslationsSnapshot();
            if (!string.IsNullOrWhiteSpace(previewSearch))
            {
                records = records.Where(MatchesPreviewSearch);
            }

            return records.OrderBy(record => record.ModName).ThenBy(record => record.DefName).ThenBy(record => record.FieldName);
        }

        private bool MatchesPreviewSearch(TranslationCacheRecord record)
        {
            string query = previewSearch ?? string.Empty;
            return ContainsIgnoreCase(record.ModName, query) ||
                ContainsIgnoreCase(record.PackageId, query) ||
                ContainsIgnoreCase(record.DefTypeName, query) ||
                ContainsIgnoreCase(record.DefName, query) ||
                ContainsIgnoreCase(record.FieldName, query) ||
                ContainsIgnoreCase(record.SourceText, query) ||
                ContainsIgnoreCase(record.TranslatedText, query);
        }

        private static bool ContainsIgnoreCase(string value, string query)
        {
            return (value ?? string.Empty).IndexOf(query ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string PreviewModKey(TranslationCacheRecord record)
        {
            return (record.PackageId ?? string.Empty) + "|" + (record.ModName ?? string.Empty);
        }

        private float GetPreviewViewHeight(Rect inRect, List<TranslationCacheRecord> records)
        {
            float height = 150f;
            foreach (IGrouping<string, TranslationCacheRecord> group in records.GroupBy(PreviewModKey))
            {
                height += 38f;
                if (expandedPreviewMods.Contains(group.Key))
                {
                    height += 26f + group.Count() * 80f;
                }
            }

            return Math.Max(inRect.height - 4f, height);
        }

        private void DrawTaskControls(Listing_Standard listing)
        {
            listing.Label(UiText.Get("Task"));
            Rect row = listing.GetRect(32f);
            float width = row.width / 5f - 4f;
            if (Widgets.ButtonText(new Rect(row.x, row.y, width, row.height), UiText.Get("Start")))
            {
                replacedPreviewKeys.Clear();
                TranslationManager.Start();
            }
            if (Widgets.ButtonText(new Rect(row.x + (width + 5f), row.y, width, row.height), UiText.Get("Pause")))
            {
                TranslationManager.Pause();
            }
            if (Widgets.ButtonText(new Rect(row.x + (width + 5f) * 2f, row.y, width, row.height), UiText.Get("Resume")))
            {
                TranslationManager.Resume();
            }
            if (Widgets.ButtonText(new Rect(row.x + (width + 5f) * 3f, row.y, width, row.height), UiText.Get("Cancel")))
            {
                TranslationManager.Cancel();
            }
            if (Widgets.ButtonText(new Rect(row.x + (width + 5f) * 4f, row.y, width, row.height), UiText.Get("Export")))
            {
                TranslationManager.ExportCurrentCache();
            }
        }

        private void DrawStatus(Listing_Standard listing)
        {
            TranslationStatus status = TranslationManager.Status;
            listing.Label(UiText.Get("Status: ") + status.State);
            listing.Label(UiText.Get("Progress: ") + status.Completed + " / " + status.Total + UiText.Get("  Failed: ") + status.Failed + UiText.Get("  Skipped: ") + status.Skipped);
            listing.Label(UiText.Get("Current: ") + status.CurrentItem);
            if (!string.IsNullOrWhiteSpace(status.LastError))
            {
                listing.Label(UiText.Get("Last error: ") + status.LastError);
            }
        }

        private static string TextFieldLabeled(Listing_Standard listing, string label, string value)
        {
            Rect rect = listing.GetRect(30f);
            Widgets.Label(new Rect(rect.x, rect.y, 210f, rect.height), label);
            return Widgets.TextField(new Rect(rect.x + 220f, rect.y, rect.width - 220f, rect.height), value ?? string.Empty);
        }

        private static string PasswordFieldLabeled(Listing_Standard listing, string label, string value)
        {
            Rect rect = listing.GetRect(30f);
            Widgets.Label(new Rect(rect.x, rect.y, 210f, rect.height), label);
            string masked = value ?? string.Empty;
            string next = Widgets.TextField(new Rect(rect.x + 220f, rect.y, rect.width - 220f, rect.height), masked);
            return next;
        }

        private static string TextAreaLabeled(Listing_Standard listing, string label, string value, float height)
        {
            Rect rect = listing.GetRect(height);
            Widgets.Label(new Rect(rect.x, rect.y, 210f, 30f), label);
            return Widgets.TextArea(new Rect(rect.x + 220f, rect.y, rect.width - 220f, rect.height), value ?? string.Empty);
        }

        private static int IntSliderLabeled(Listing_Standard listing, string label, int value, int min, int max)
        {
            listing.Label(label + ": " + value);
            return Mathf.RoundToInt(listing.Slider(value, min, max));
        }
    }
}
