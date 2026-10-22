using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Verse;

namespace RuntimeTranslator.Tests
{
    public static class MultilingualAndKeyedChecks
    {
        private static int passed;
        private static void Check(bool valid, string name)
        {
            if (!valid) throw new Exception("FAILED: " + name);
            passed++; Console.WriteLine("PASS: " + name);
        }
        private static void Write(string root, string language, params string[] pairs)
        {
            string folder = Path.Combine(root, "Languages", language, "Keyed"); Directory.CreateDirectory(folder);
            XElement document = new XElement("LanguageData");
            for (int i = 0; i < pairs.Length; i += 2) document.Add(new XElement(pairs[i], pairs[i + 1]));
            document.Save(Path.Combine(folder, "Keys.xml"));
        }
        public static int Run(string root)
        {
            var previousMods = LoadedModManager.RunningMods.ToList();
            var previousLanguage = LanguageDatabase.activeLanguage;
            try
            {
                GenFilePaths.ConfigFolderPath = root;
                const string action = "RimLingoTestAction", native = "RimLingoTestNative", paragraphs = "RimLingoTestParagraphs";
                Write(root, "English", action, "Launch", native, "Native menu", paragraphs, "  First {0}\n\nSecond  ");
                Write(root, "Japanese", native, "公式メニュー");
                Translator.Fallbacks[action] = "Launch";
                Translator.Fallbacks[native] = "Native menu";
                Translator.Fallbacks[paragraphs] = "  First {0}\n\nSecond  ";
                ModContentPack pack = new ModContentPack { RootDir = root, PackageIdPlayerFacing = "keyed.test", Name = "Keyed Test", AllDefs = new List<Def>() };
                LoadedModManager.RunningMods.Clear(); LoadedModManager.RunningMods.Add(pack);
                LanguageDatabase.activeLanguage = new LoadedLanguage { folderName = "Japanese" };
                LanguageDatabase.activeLanguage.keyedReplacements[native] = new LoadedLanguage.KeyedReplacement { value = "公式メニュー" };
                RuntimeTranslatorSettings settings = new RuntimeTranslatorSettings
                { SelectedPackageIds = new List<string> { pack.PackageIdPlayerFacing }, TargetLanguage = "Japanese", SkipModsWithBundledChinese = false };
                TranslationManager.Initialize(settings);
                var entries = ModTextScanner.Scan(settings);
                Check(entries.Count == 2 && entries.All(entry => entry.DefTypeName == "Keyed" && entry.ModName == "Keyed Test"),
                    "generic mod Keyed XML is scanned with correct mod ownership while native keys stay protected");
                settings.SourceLanguage = "Auto";
                Check(ModTextScanner.Scan(settings).Count == 2, "Auto source discovers the same English Keyed sources");
                settings.SourceLanguage = "English";
                Check(entries.Single(entry => entry.DefName == paragraphs).SourceText == "  First {0}\n\nSecond  ", "Keyed scanning preserves whitespace, placeholders and paragraph breaks");
                TranslationEntry entry = entries.Single(item => item.DefName == action);
                string hash = entry.SourceHash;
                entry.TranslatedText = "発射";
                Check(RuntimeTextApplier.Apply(entry) && action.Translate() == "発射", "generic Keyed translations reach the game's normal translation dictionary");
                NativeTranslations.Reset(); NativeTranslations.Prepare("Japanese");
                Check(!NativeTranslations.Contains(entry) && ModTextScanner.Scan(settings).Single(item => item.DefName == action).SourceHash == hash,
                    "runtime Keyed overrides do not become native translations or corrupt the rescan source");
                entry.TranslatedText = "";
                Check(RuntimeTextApplier.Apply(entry) && action.Translate() == "Launch", "clearing a generic Keyed translation restores the original language fallback");
                Translator.Fallbacks[action] = "Another mod owns this key";
                entry.TranslatedText = "wrong owner";
                // Use a fresh language table to exercise first application, before we own an override.
                LanguageDatabase.activeLanguage = new LoadedLanguage { folderName = "Japanese" };
                NativeTranslations.Reset();
                Check(!RuntimeTextApplier.Apply(entry), "a duplicate Keyed key cannot overwrite another mod's different effective source");
                Translator.Fallbacks[action] = "Launch";
                settings.SkipModsWithBundledChinese = true;
                Check(ModTextScanner.Scan(settings).Count == 0, "whole-mod native skipping works for Japanese targets");

                Write(root, "French", "Steel", "acier", "Extra", "menu supplémentaire");
                Write(root, "German", "Steel", "Stahl");
                Write(root, "PortugueseBrazilian", "Steel", "aço");
                Write(root, "English", "Steel", "steel", "Extra", "extra menu");
                List<List<string>> folders = new List<List<string>> { new List<string> { root } };
                var imported = BundledTranslations.ReadGlossary(folders, "French", "German");
                Check(imported.Count == 1 && imported[0].Source == "acier" && imported[0].Translation == "Stahl",
                    "glossary import pairs two non-English native catalogs by their XML keys");
                Check(BundledTranslations.HasTranslation(new[] { root }, "German") && BundledTranslations.HasTranslation(new[] { root }, "Portuguese"),
                    "target-language detection recognizes German and Brazilian Portuguese folder aliases");
                settings.TargetLanguage = "German";
                Check(ModTextScanner.Scan(settings).Count == 0, "whole-mod native skipping works for Latin-script target languages");
                settings.SkipModsWithBundledChinese = false;
                NativeTranslations.Reset();
                var missing = ModTextScanner.Scan(settings);
                Check(missing.Count == 1 && missing[0].DefName == "Extra", "disabling whole-mod skip still preserves existing German keys and exposes missing ones");
                Check(Glossary.CountWords(new[] { "der Stahl und der Stahl" }, source: "German")["stahl"] == 2 &&
                    !Glossary.CountWords(new[] { "der Stahl und der Stahl" }, source: "German").ContainsKey("der"), "German term discovery filters German function words");
                Check(Glossary.CountWords(new[] { "électricité et électricité" }, source: "French")["électricité"] == 2 &&
                    Glossary.CountWords(new[] { "сталь и сталь" }, source: "Russian")["сталь"] == 2,
                    "accented Latin and Cyrillic terms are counted in their source language");
                Check(Glossary.CountWords(new[] { "강철 그리고 강철" }, source: "Korean")["강철"] == 2, "Korean term discovery handles Hangul and common function words");
                Check(Glossary.CountWords(new[] { "寄生卵。寄生卵。" }, source: "ChineseSimplified")["寄生卵"] == 2 &&
                    Glossary.CountWords(new[] { "鋼の剣、鋼の剣。" }, source: "Japanese")["鋼の剣"] == 2,
                    "Chinese and Japanese discovery counts repeated short fragments without inventing word boundaries");
                Check(!Glossary.CountWords(new[] { new string('鋼', 40) }, source: "Japanese").Any(), "long unsegmented CJK sentences are not chopped into arbitrary glossary fragments");
                var protectedText = TextProtection.Protect("这个寄生卵由{0}携带。", new[] { new GlossaryTerm { Source = "寄生卵", Translation = "parasitic egg" },
                    new GlossaryTerm { Source = "寄生", Translation = "parasite" } });
                Check(protectedText.Restore(protectedText.Text) == "这个parasitic egg由{0}携带。", "CJK glossary phrases match inside unspaced sentences with longest-phrase and placeholder protection");
                var frenchCounts = Glossary.ScanFiles(new[] { root }, source: "French");
                Check(frenchCounts.ContainsKey("acier") && !frenchCounts.ContainsKey("steel"), "file discovery reads the selected source-language catalog rather than English");
            }
            finally
            {
                LoadedModManager.RunningMods.Clear(); LoadedModManager.RunningMods.AddRange(previousMods);
                LanguageDatabase.activeLanguage = previousLanguage; Translator.Fallbacks.Clear(); NativeTranslations.Reset();
            }
            return passed;
        }
    }
}
