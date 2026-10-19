using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace RuntimeTranslator.Tests
{
    public sealed class DerivedDef : Verse.Def { }
    public sealed class BlankProvider : ITranslationProvider
    {
        public string DisplayName { get { return "Blank"; } }
        public Task<TranslationBatchResult> TranslateBatchAsync(TranslationBatchRequest request, CancellationToken token)
        {
            TranslationBatchResult result = new TranslationBatchResult();
            foreach (TranslationEntry entry in request.Entries) result.Translations[entry.Id] = "<b>\u200b </b>";
            return Task.FromResult(result);
        }
    }
    public static class NativeTranslationChecks
    {
        private static int passed;
        private static void Check(bool valid, string name)
        {
            if (!valid) throw new Exception("FAILED: " + name);
            passed++; Console.WriteLine("PASS: " + name);
        }
        private static void Worker(List<TranslationEntry> entries, ITranslationProvider provider, RuntimeTranslatorSettings settings)
        {
            ((Task)typeof(TranslationManager).GetMethod("RunWorker", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
                new object[] { entries, provider, settings.Snapshot(), CancellationToken.None, false })).GetAwaiter().GetResult();
        }
        public static int Run(string folder)
        {
            Directory.CreateDirectory(folder);
            Verse.GenFilePaths.ConfigFolderPath = folder;
            Verse.LanguageDatabase.activeLanguage = new Verse.LoadedLanguage { folderName = "ChineseSimplified" };
            Verse.Def translated = new Verse.Def { defName = "Native", label = "模组自带译名" };
            Verse.Def missing = new Verse.Def { defName = "Missing", label = "untranslated label" };
            Verse.Def derived = new DerivedDef { defName = "Derived", label = "继承类自带翻译" };
            Verse.Def fromPatch = new Verse.Def { defName = "Patched", label = "独立汉化译名" };
            Verse.ModContentPack pack = new Verse.ModContentPack { RootDir = folder, PackageIdPlayerFacing = "native.mod", Name = "Native",
                AllDefs = new List<Verse.Def> { translated, missing, fromPatch, derived } };
            Verse.LoadedModManager.RunningMods.Clear(); Verse.LoadedModManager.RunningMods.Add(pack);
            string native = Path.Combine(folder, "Languages", "ChineseSimplified", "DefInjected", "Def"); Directory.CreateDirectory(native);
            File.WriteAllText(Path.Combine(native, "Native.xml"), "<LanguageData><Native.label>模组自带译名</Native.label><Missing.label> </Missing.label></LanguageData>");
            Verse.DefInjectionPackage package = new Verse.DefInjectionPackage { defType = typeof(Verse.Def) };
            package.injections.Add("Patched.label", new Verse.DefInjectionPackage.DefInjection { path = "Patched.label", normalizedPath = "Patched.label", replacedString = "独立汉化译名" });
            package.injections.Add("Native.label", new Verse.DefInjectionPackage.DefInjection { path = "Native.label", normalizedPath = "Native.label", replacedString = "模组自带译名" });
            package.injections.Add("Derived.label", new Verse.DefInjectionPackage.DefInjection { path = "Derived.label", normalizedPath = "Derived.label", replacedString = "继承类自带翻译" });
            Verse.LanguageDatabase.activeLanguage.defInjections.Add(package);
            RuntimeTranslatorSettings settings = new RuntimeTranslatorSettings { SelectedPackageIds = new List<string> { pack.PackageIdPlayerFacing },
                SkipModsWithBundledChinese = false, SkipCompletedTranslations = false, ExportLanguageFiles = false, ApplyRuntimeTranslations = true };
            TranslationManager.Initialize(settings);
            List<TranslationEntry> scanned = ModTextScanner.Scan(settings);
            Check(scanned.Count == 1 && scanned[0].DefName == "Missing", "partial Chinese catalogs only expose missing fields even when whole-mod skipping is disabled");
            TranslationEntry old = new TranslationEntry { Id = "old-native", PackageId = pack.PackageIdPlayerFacing, DefTypeName = "Def", DefName = "Native", FieldName = "label",
                SourceLanguage = "English", TargetLanguage = "ChineseSimplified", SourceText = "old English source", TranslatedText = "错误机器译名", SourceHash = "old" };
            Check(!RuntimeTextApplier.Apply(old) && translated.label == "模组自带译名", "stale cached and manual translations cannot replace a bundled native Def label");
            old.DefName = "Patched";
            Check(!RuntimeTextApplier.Apply(old) && fromPatch.label == "独立汉化译名", "game-loaded Def injections from another translation mod are preserved");
            old.DefName = "Derived"; old.DefTypeName = nameof(DerivedDef);
            Check(!RuntimeTextApplier.Apply(old) && derived.label == "继承类自带翻译", "base-type Def injections also protect custom derived Def classes");
            old.DefName = "Native"; old.DefTypeName = "Def";
            BatchProbeProvider probe = new BatchProbeProvider();
            Worker(new List<TranslationEntry> { old, scanned[0] }, probe, settings);
            TranslationManager.Tick();
            Check(probe.Texts.SequenceEqual(new[] { "untranslated label" }) && translated.label == "模组自带译名", "worker excludes native entries before cache/API processing");
            Check(missing.label == "译untranslated label", "missing text still receives runtime translations");
            TranslationEntry fallback = scanned[0]; fallback.TranslatedText = "\u200b\t ";
            Check(RuntimeTextApplier.Apply(fallback) && missing.label == "untranslated label", "invisible runtime replacements restore original text instead of blanking it");
            Check(!TextProtection.HasVisibleText("<b>\u200b </b>") && !TextProtection.HasVisibleText("\u200b\r\n") && TextProtection.HasVisibleText("<b>中文</b>") && TextProtection.HasVisibleText("{0}"),
                "visible-text validation rejects markup-only and zero-width output while retaining text and variables");
            BlankProvider blank = new BlankProvider();
            Worker(scanned, blank, settings);
            TranslationManager.Tick();
            Check(TranslationManager.Status.Failed == 1 && missing.label == "untranslated label" && TranslationManager.GetCachedTranslationsSnapshot().All(record => record.Provider != blank.DisplayName),
                "blank provider output is a failed translation, never a saved/displayed silent replacement");
            string generated = Path.Combine(folder, "RuntimeTranslator", "GeneratedLanguages", "ChineseSimplified", pack.PackageIdPlayerFacing, "DefInjected", "Def");
            Directory.CreateDirectory(generated);
            File.WriteAllText(Path.Combine(generated, "RuntimeTranslator.xml"), "<LanguageData><Native.label>旧错误译名</Native.label></LanguageData>");
            fallback.TranslatedText = "有效补充译名";
            LanguageFileExporter.Export(new[] { TranslationCacheRecord.FromEntry(old, "Old"), TranslationCacheRecord.FromEntry(fallback, "New") }, "ChineseSimplified");
            XElement output = XDocument.Load(Path.Combine(generated, "RuntimeTranslator.xml")).Root;
            Check(output.Elements().Count() == 1 && output.Element("Missing.label").Value == "有效补充译名", "export removes native conflicts from old generated files and only writes missing translations");
            fallback.TranslatedText = "<b>\u200b</b>";
            LanguageFileExporter.Export(new[] { TranslationCacheRecord.FromEntry(fallback, "New") }, "ChineseSimplified");
            Check(!XDocument.Load(Path.Combine(generated, "RuntimeTranslator.xml")).Root.Elements().Any(), "invisible exported values cannot suppress original language fallback");
            TranslationBatchRequest request = new TranslationBatchRequest { Entries = new List<TranslationEntry> { new TranslationEntry { Id = "x", SourceText = "text" } } };
            foreach (string response in new[] { @"{""choices"":[{""finish_reason"":""content_filter"",""message"":{""content"":null}}]}",
                @"{""choices"":[{""message"":{""refusal"":""Refused"",""content"":null}}]}" })
            {
                bool refused = false;
                try { OpenAiCompatibleProvider.ReadResponse(request, new List<ProtectedText> { TextProtection.Protect("text") }, response); }
                catch (InvalidOperationException) { refused = true; }
                Check(refused, "explicit API refusal/filter is rejected without producing replacement text");
            }
            string key = UiText.Key("Task");
            string englishKeys = Path.Combine(folder, "Languages", "English", "Keyed"); Directory.CreateDirectory(englishKeys);
            File.WriteAllText(Path.Combine(englishKeys, "RuntimeTranslator.xml"), "<LanguageData><" + key + ">Task</" + key + "></LanguageData>");
            UiText.Initialize(pack);
            UiText.Apply(new TranslationEntry { PackageId = pack.PackageIdPlayerFacing, DefName = key, TranslatedText = "旧机器任务" });
            Verse.LanguageDatabase.activeLanguage.keyedReplacements[key] = new Verse.LoadedLanguage.KeyedReplacement { value = "作者的任务翻译" };
            Check(UiText.Get("Task") == "作者的任务翻译", "native Keyed translation has priority over the translator UI fallback");
            Verse.LanguageDatabase.activeLanguage = null;
            NativeTranslations.Reset();
            return passed;
        }
    }
}
