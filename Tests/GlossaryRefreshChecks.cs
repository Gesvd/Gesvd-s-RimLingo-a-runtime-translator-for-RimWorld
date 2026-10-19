using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace RuntimeTranslator.Tests
{
    public sealed class GlossaryRefreshProvider : ITranslationProvider
    {
        public string DisplayName { get { return "GlossaryRefresh"; } }
        public List<GlossaryTerm> Terms;
        public Task<TranslationBatchResult> TranslateBatchAsync(TranslationBatchRequest request, CancellationToken token)
        {
            TranslationBatchResult result = new TranslationBatchResult();
            foreach (TranslationEntry entry in request.Entries)
            {
                ProtectedText text = TextProtection.Protect(entry.SourceText, Terms);
                result.Translations[entry.Id] = text.Restore(text.Text);
            }
            return Task.FromResult(result);
        }
    }
    public static class GlossaryRefreshChecks
    {
        private static int passed;
        private static void Check(bool valid, string name)
        {
            if (!valid) throw new Exception("FAILED: " + name);
            passed++; Console.WriteLine("PASS: " + name);
        }
        private static void Worker(List<TranslationEntry> entries, ITranslationProvider provider, RuntimeTranslatorSettings settings, CancellationToken token)
        {
            ((Task)typeof(TranslationManager).GetMethod("RunWorker", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
                new object[] { entries, provider, settings.Snapshot(), token, true })).GetAwaiter().GetResult();
        }
        public static int Run(string folder)
        {
            Verse.GenFilePaths.ConfigFolderPath = folder;
            Verse.LanguageDatabase.activeLanguage = new Verse.LoadedLanguage { folderName = "ChineseSimplified" };
            Verse.Def egg = new Verse.Def { defName = "Egg", label = "parasitic egg" };
            Verse.ModContentPack pack = new Verse.ModContentPack { RootDir = folder, Name = "Egg Mod", PackageIdPlayerFacing = "egg.mod",
                AllDefs = new List<Verse.Def> { egg } };
            Verse.LoadedModManager.RunningMods.Clear(); Verse.LoadedModManager.RunningMods.Add(pack);
            RuntimeTranslatorSettings settings = new RuntimeTranslatorSettings
            {
                ExportLanguageFiles = true, ApplyRuntimeTranslations = true, SkipCompletedTranslations = false,
                GlossaryTerms = new List<GlossaryTerm> { new GlossaryTerm { Source = "parasitic egg", Translation = "寄生卵" }, new GlossaryTerm { Source = "parasitic", Translation = "寄生质" } }
            };
            TranslationManager.Initialize(settings);
            TranslationCacheStore cache = (TranslationCacheStore)typeof(TranslationManager).GetField("cache", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            TranslationEntry old = new TranslationEntry { Id = "egg.mod|Def|Egg|label|English|ChineseSimplified", PackageId = "egg.mod", ModName = "Egg Mod",
                DefTypeName = "Def", DefName = "Egg", FieldName = "label", SourceLanguage = "English", TargetLanguage = "ChineseSimplified",
                SourceText = "parasitic egg", SourceHash = "old-hash", TranslatedText = "寄生质卵" };
            cache.Put(old, "OldProvider"); cache.Put(old, "OtherOldProvider"); RuntimeTextApplier.Apply(old);
            TranslationEntry unrelated = TranslationCacheRecord.FromEntry(old, "Test").ToEntry();
            unrelated.Id = "unrelated"; unrelated.SourceText = "ordinary rifle"; unrelated.TranslatedText = "普通步枪"; cache.Put(unrelated, "OldProvider");
            TranslationEntry japanese = TranslationCacheRecord.FromEntry(old, "Test").ToEntry();
            japanese.Id = "japanese"; japanese.TargetLanguage = "Japanese"; japanese.TranslatedText = "旧日语"; cache.Put(japanese, "OldProvider");
            TranslationEntry cleared = TranslationCacheRecord.FromEntry(old, "Test").ToEntry();
            cleared.Id = "cleared"; cleared.TranslatedText = ""; cache.Put(cleared, "OldProvider");
            string nativeKey = UiText.Key("native glossary test");
            Verse.LanguageDatabase.activeLanguage.keyedReplacements[nativeKey] = new Verse.LoadedLanguage.KeyedReplacement { value = "官方寄生卵" };
            TranslationEntry native = TranslationCacheRecord.FromEntry(old, "Test").ToEntry();
            native.Id = "native"; native.DefTypeName = "Keyed"; native.DefName = nativeKey; cache.Put(native, "OldProvider");
            TranslationEntry stale = TranslationCacheRecord.FromEntry(old, "Test").ToEntry();
            stale.Id = "stale"; stale.SourceText = "parasitic egg old description"; cache.Put(stale, "OldProvider");
            TranslationEntry unloaded = TranslationCacheRecord.FromEntry(old, "Test").ToEntry();
            unloaded.Id = "unloaded"; unloaded.PackageId = "unloaded.mod"; cache.Put(unloaded, "OldProvider");
            NativeTranslations.Reset();
            List<TranslationEntry> entries = TranslationManager.GlossaryRefreshEntries(settings);
            Check(entries.Count == 1 && entries[0].Id == old.Id && entries[0].SourceHash == Glossary.SourceHash(old.SourceText, settings.GlossaryTerms),
                "glossary refresh selects affected cached sources once and excludes unrelated, other-language, cleared, native, unloaded and stale-source entries");
            Check(settings.SelectedPackageIds.Count == 0 && cache.Records.Count == 8, "glossary refresh selection needs no mod selection and does not mutate cache");
            Worker(entries, new BatchProbeProvider { FailRequest = 1 }, settings, CancellationToken.None);
            Check(cache.Records.Count == 8 && cache.Records.Where(record => record.Id == old.Id).All(record => record.TranslatedText == "寄生质卵"),
                "failed glossary refresh preserves every old translation and provider variant");
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel(); Worker(entries, new GlossaryRefreshProvider { Terms = settings.GlossaryTerms }, settings, cancellation.Token);
                Check(cache.Records.Count == 8, "cancelled glossary refresh leaves old translations intact");
            }
            Worker(entries, new GlossaryRefreshProvider { Terms = settings.GlossaryTerms }, settings, CancellationToken.None);
            TranslationManager.Tick();
            Check(egg.label == "寄生卵", "successful glossary refresh applies the phrase translation instead of the component-word translation");
            Check(cache.Records.Count == 7 && cache.Records.Single(record => record.Id == old.Id).TranslatedText == "寄生卵" &&
                cache.Records.Single(record => record.Id == old.Id).SourceHash == entries[0].SourceHash,
                "successful refresh replaces obsolete provider/hash variants and stores the current glossary hash");
            Check(cache.Records.Single(record => record.Id == "japanese").TranslatedText == "旧日语" &&
                cache.Records.Single(record => record.Id == "native").TranslatedText == "寄生质卵", "refresh does not modify other-language or native-protected cache records");
            string exported = Path.Combine(folder, "RuntimeTranslator", "GeneratedLanguages", "ChineseSimplified", "egg.mod", "DefInjected", "Def", "RuntimeTranslator.xml");
            Check(System.Xml.Linq.XDocument.Load(exported).Root.Element("Egg.label").Value == "寄生卵", "glossary refresh updates generated language files");
            TranslationCacheStore reloaded = new TranslationCacheStore(); reloaded.Load();
            Check(reloaded.Records.Single(record => record.Id == old.Id).TranslatedText == "寄生卵", "refreshed translations survive cache reload");
            settings.GlossaryTerms.Clear();
            TranslationManager.Start(true);
            Check(TranslationManager.Status.State == TranslationState.Completed && TranslationManager.Status.Total == 0,
                "applying an empty glossary exits without requiring selected mods or making a provider request");
            Verse.LanguageDatabase.activeLanguage = null; NativeTranslations.Reset();
            return passed;
        }
    }
}
