using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace RuntimeTranslator.Tests
{
    public static class BundledTranslationChecks
    {
        private static int passed;
        private static void Check(bool valid, string name)
        {
            if (!valid) throw new Exception("FAILED: " + name);
            passed++;
            Console.WriteLine("PASS: " + name);
        }
        private static void Write(string root, string file, string text)
        {
            string path = Path.Combine(root, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
        }
        private static Verse.ModContentPack Pack(string root, string id, bool official = false)
        {
            return new Verse.ModContentPack { RootDir = root, PackageIdPlayerFacing = id, Name = id, IsOfficialMod = official,
                AllDefs = new List<Verse.Def> { new Verse.Def { defName = id.Replace(".", ""), label = "charge rifle" } } };
        }
        public static int Run(string root)
        {
            string current = Path.Combine(root, "translated", "1.6");
            Write(current, "Defs/Weapons.xml", "<Defs><Def><defName>Rifle</defName><label>charge rifle</label><description>A pawn with a rifle.</description></Def><Def><defName>Pawn</defName><label>pawn</label></Def></Defs>");
            Write(current, "Languages/ChineseSimplified/DefInjected/Def/Weapons.xml", "<LanguageData><Rifle.label>电荷步枪</Rifle.label><Pawn.label>棋子</Pawn.label></LanguageData>");
            Write(current, "Languages/English/Keyed/Keys.xml", "<LanguageData><Steel>steel</Steel><Power>power</Power><Variable>pawn {0}</Variable><Article>the</Article><Long>a fairly short line with too many words</Long></LanguageData>");
            Write(current, "Languages/ChineseSimplified/Keyed/Keys.xml", "<LanguageData><Steel>钢铁</Steel><Power>电力</Power><Variable>角色 {0}</Variable><Article>这个</Article><Long>这是一段很长的说明文字。</Long><Unknown>未知</Unknown></LanguageData>");
            Write(current, "Languages/ChineseSimplified/Keyed/Broken.xml", "<broken>");
            Write(current, "Languages/ChineseTraditional/Keyed/Keys.xml", "<LanguageData><Steel>鋼鐵</Steel></LanguageData>");
            string fallback = Path.Combine(root, "translated", "Common");
            Write(fallback, "Languages/ChineseSimplified/Keyed/Keys.xml", "<LanguageData><Steel>旧钢铁</Steel></LanguageData>");
            string stale = Path.Combine(root, "translated", "1.5");
            Write(stale, "Languages/English/Keyed/Keys.xml", "<LanguageData><Old>obsolete</Old></LanguageData>");
            Write(stale, "Languages/ChineseSimplified/Keyed/Keys.xml", "<LanguageData><Old>过时</Old></LanguageData>");
            string other = Path.Combine(root, "other");
            Write(other, "Languages/English/Keyed/Keys.xml", "<LanguageData><Power>power</Power><Crystal>crystal</Crystal></LanguageData>");
            Write(other, "Languages/ChineseSimplified/Keyed/Keys.xml", "<LanguageData><Power>能力</Power><Crystal>水晶</Crystal></LanguageData>");
            string placeholders = Path.Combine(root, "placeholders");
            Write(placeholders, "Languages/ChineseSimplified/Keyed/Keys.xml", "<LanguageData><Rifle>TODO</Rifle><Empty></Empty></LanguageData>");
            string unsafeXml = Path.Combine(root, "unsafe");
            Write(unsafeXml, "Languages/ChineseSimplified/Keyed/Keys.xml", "<!DOCTYPE LanguageData [<!ENTITY example '不应加载'>]><LanguageData><Name>&example;</Name></LanguageData>");
            Check(!BundledTranslations.HasChineseTranslation(new[] { unsafeXml }, "ChineseSimplified"), "DTD/entity catalogs are rejected instead of treated as usable Chinese translations");
            string notCurrent = Path.Combine(root, "not-current");
            Write(notCurrent, "1.5/Languages/ChineseSimplified/Keyed/Keys.xml", "<LanguageData><Name>旧版翻译</Name></LanguageData>");
            Write(notCurrent, "1.6/Defs/Defs.xml", "<Defs/>");
            Verse.ModContentPack translated = Pack(Path.Combine(root, "translated"), "translated.mod");
            translated.AllDefs[0].defName = "Rifle";
            translated.foldersToLoadDescendingOrder = new List<string> { "1.6", "Common" };
            Verse.ModContentPack ordinary = Pack(other, "other.mod");
            Verse.ModContentPack core = Pack(current, "ludeon.rimworld", true);
            Verse.ModContentPack dlc = Pack(current, "ludeon.rimworld.royalty", true);
            Verse.ModContentPack futureDlc = Pack(current, "future.official.dlc", true);
            Verse.ModContentPack empty = Pack(placeholders, "empty.mod");
            Verse.ModContentPack inactive = Pack(notCurrent, "inactive.mod");
            inactive.foldersToLoadDescendingOrder = new List<string> { "1.6" };
            Verse.LoadedModManager.RunningMods.Clear();
            Verse.LoadedModManager.RunningMods.AddRange(new[] { translated, ordinary, core, dlc, futureDlc, empty, inactive });
            NativeTranslations.Reset();
            RuntimeTranslatorSettings settings = new RuntimeTranslatorSettings { SelectedPackageIds = Verse.LoadedModManager.RunningMods.Select(pack => pack.PackageIdPlayerFacing).ToList() };
            Check(!ModTextScanner.IsPlayerMod(core) && !ModTextScanner.IsPlayerMod(dlc) && !ModTextScanner.IsPlayerMod(futureDlc) && ModTextScanner.IsPlayerMod(translated),
                "official flag excludes Core, existing DLC and future DLC without name guessing");
            List<List<string>> folders = ModTextScanner.SelectedModFolders(settings);
            Check(folders.Count == 4 && folders[0].SequenceEqual(new[] { current, fallback }), "glossary folder capture excludes official content and keeps active version priority");
            Check(settings.SkipModsWithBundledChinese && ModTextScanner.Scan(settings).Select(entry => entry.PackageId).SequenceEqual(new[] { "empty.mod", "inactive.mod" }),
                "default scan skips translated mods but keeps empty/TODO catalogs and mods with only inactive-version Chinese");
            settings.SkipModsWithBundledChinese = false;
            List<TranslationEntry> all = ModTextScanner.Scan(settings);
            Check(all.Count(entry => entry.DefTypeName != "Keyed") == 3 && all.All(entry => !entry.PackageId.StartsWith("ludeon.") && entry.PackageId != futureDlc.PackageIdPlayerFacing),
                "turning off whole-mod skip still protects translated fields and excludes officially flagged DLC");
            settings.SkipModsWithBundledChinese = true;
            settings.TargetLanguage = "Japanese";
            Check(ModTextScanner.Scan(settings).Count(entry => entry.DefTypeName != "Keyed") == 4, "Chinese catalogs do not skip mods when translating to Japanese");
            List<GlossaryTerm> imported = BundledTranslations.ReadGlossary(folders, "English", "ChineseSimplified");
            Check(imported.Any(term => term.Source == "charge rifle" && term.Translation == "电荷步枪"), "DefInjected keys pair original Def labels with bundled Chinese labels");
            Check(imported.Any(term => term.Source == "steel" && term.Translation == "钢铁") && imported.Any(term => term.Source == "crystal" && term.Translation == "水晶"),
                "Keyed imports match per mod and honor current-folder precedence over fallback folders");
            Check(!imported.Any(term => term.Source == "power" || term.Source == "obsolete" || term.Source.Contains("{0}") || term.Source == "the" || term.Source.Split(' ').Length > 6),
                "conflicting meanings, inactive versions, format tokens, stop words and long text are not imported as terms");
            Check(imported.All(term => term.TargetLanguage == "ChineseSimplified" && term.Translation != "鋼鐵"), "Simplified import does not mix Traditional Chinese translations");
            List<GlossaryTerm> traditional = BundledTranslations.ReadGlossary(folders, "en", "ChineseTraditional");
            Check(traditional.Count == 1 && traditional[0].Source == "steel" && traditional[0].Translation == "鋼鐵", "Traditional target imports its own catalog only");
            settings.TargetLanguage = "ChineseSimplified";
            settings.GlossaryTerms.Add(new GlossaryTerm { Source = "steel", Translation = "" });
            settings.GlossaryTerms.Add(new GlossaryTerm { Source = "crystal", Translation = "玩家水晶" });
            int added = BundledTranslations.ImportGlossary(settings, imported);
            Check(added == 2 && settings.GlossaryTerms.Single(term => term.Source == "steel").Translation == "钢铁" &&
                settings.GlossaryTerms.Single(term => term.Source == "pawn").Translation == "小人" && settings.GlossaryTerms.Single(term => term.Source == "crystal").Translation == "玩家水晶",
                "import adds new phrases and fills blanks without replacing defaults or player edits");
            Check(BundledTranslations.ImportGlossary(settings, imported) == 0, "importing the same catalogs again does not duplicate or overwrite terms");
            string hash = Glossary.SourceHash("A charge rifle.", settings.GlossaryTerms);
            settings.GlossaryTerms.Single(term => term.Source == "charge rifle").Translation = "玩家步枪";
            Check(hash != Glossary.SourceHash("A charge rifle.", settings.GlossaryTerms), "imported phrases remain manually editable and invalidate affected cache hashes");
            using (CancellationTokenSource job = new CancellationTokenSource())
            {
                job.Cancel();
                bool cancelled = false;
                try { BundledTranslations.ReadGlossary(folders, "en", "zh", job.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled, "bundled glossary file import supports cancellation before any file is read");
            }
            RuntimeTranslatorSettings snapshot = settings.Snapshot();
            settings.SkipModsWithBundledChinese = false;
            Check(snapshot.SkipModsWithBundledChinese, "running task keeps its bundled-translation skip choice");
            ModTextScanner.ExcludeOfficialSelections(settings);
            Check(settings.SelectedPackageIds.Count == 4 && !settings.SelectedPackageIds.Contains(core.PackageIdPlayerFacing) && !settings.SelectedPackageIds.Contains(futureDlc.PackageIdPlayerFacing),
                "post-load selection cleanup removes stale official IDs while preserving player mod choices");
            return passed;
        }
    }
}
