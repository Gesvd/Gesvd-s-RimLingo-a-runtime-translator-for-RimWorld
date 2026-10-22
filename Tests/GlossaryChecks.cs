using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RuntimeTranslator.Tests
{
    public static class GlossaryChecks
    {
        private static int passed;
        private static void Check(bool valid, string name)
        {
            if (!valid) throw new Exception("FAILED: " + name);
            passed++;
            Console.WriteLine("PASS: " + name);
        }
        public static int Run(string folder)
        {
            RuntimeTranslatorSettings settings = new RuntimeTranslatorSettings();
            List<GlossaryTerm> terms = Glossary.ForLanguages(settings, "en", "zh-CN");
            ProtectedText text = TextProtection.Protect("Pawn and pawns spawn near pawn_kind. {pawn} <pawn> [PAWN_nameDef]", terms);
            Check(text.Restore(text.Text) == "小人 and 小人 spawn near pawn_kind. {pawn} <pawn> [PAWN_nameDef]", "glossary enforces whole words without changing format tokens or identifiers");
            terms.Add(new GlossaryTerm { Source = "ancient pawn", Translation = "远古角色" });
            text = TextProtection.Protect("An ancient pawn and a pawn.", terms);
            Check(text.Restore(text.Text) == "An 远古角色 and a 小人.", "longer glossary phrases take priority over individual words");
            Check(Glossary.ForLanguages(settings, "English", "Japanese").Count == 0, "glossary does not leak into another target language");
            Check(Glossary.ForLanguages(settings, "Auto", "ChineseSimplified").Count == 2, "Auto source can use English game terminology");
            ProtectedText variablePhrase = TextProtection.Protect("pawn {0}", new[] { new GlossaryTerm { Source = "pawn {0}", Translation = "wrong" } });
            Check(variablePhrase.Restore(variablePhrase.Text) == "pawn {0}", "glossary phrases cannot consume formatting variables");
            Check(!TextProtection.IsGlossarySource("pawn <color") && TextProtection.IsGlossarySource("ancient pawn"), "manual glossary sources cannot split a format tag");
            RuntimeTranslatorSettings snapshot = settings.Snapshot();
            string before = Glossary.SourceHash("A pawn sleeps.", settings.GlossaryTerms);
            settings.GlossaryTerms[0].Translation = "角色";
            Check(snapshot.GlossaryTerms[0].Translation == "小人", "running tasks retain their own glossary copy");
            Check(before != Glossary.SourceHash("A pawn sleeps.", settings.GlossaryTerms), "editing a matching term invalidates affected translation hashes");
            Check(Glossary.SourceHash("A rifle fires.", settings.GlossaryTerms) == TextHasher.Sha1("A rifle fires."), "unaffected translations keep their existing cache hashes");
            settings.GlossaryTerms[0].Translation = "";
            text = TextProtection.Protect("pawn", settings.GlossaryTerms);
            Check(text.Restore(text.Text) == "pawn", "clearing a preferred translation disables that term");
            settings.GlossaryTerms[0].Translation = "角色";
            Dictionary<string, int> words = Glossary.CountWords(new[] { "The pawn and a pawn's rifle are on the table. <b>Rifle</b> {TOKEN} [PAWN_nameDef] pawn_id pawn2 42" });
            Check(words["pawn"] == 2 && words["rifle"] == 2 && !words.ContainsKey("the") && !words.ContainsKey("and") && !words.ContainsKey("on"), "candidate counting filters stop words and possessives");
            Check(!words.ContainsKey("token") && !words.ContainsKey("b") && !words.ContainsKey("pawn_id") && !words.ContainsKey("pawn2"), "candidate counting excludes placeholders, tags and technical identifiers");
            Dictionary<string, int> phrases = Glossary.CountWords(new[] { "Parasitic egg", "parasitic egg", "parasitic and egg" });
            Check(phrases["parasitic egg"] == 2 && phrases["parasitic"] == 1 && phrases["egg"] == 1,
                "repeated phrases consume their word occurrences while independent words remain counted");
            phrases = Glossary.CountWords(new[] { "parasitic egg sac", "Parasitic egg sac", "parasitic egg" });
            Check(phrases["parasitic egg sac"] == 2 && phrases["parasitic egg"] == 1 && !phrases.ContainsKey("parasitic"),
                "longest repeated phrase wins without double-counting nested phrases or words");
            phrases = Glossary.CountWords(new[] { "parasitic {0} egg", "parasitic {1} egg", "<b>Rifle</b>", "<b>Rifle</b>" });
            Check(!phrases.ContainsKey("parasitic egg") && phrases["parasitic"] == 2 && phrases["egg"] == 2 && phrases["rifle"] == 2,
                "format tokens break phrases without hiding adjacent visible words");
            phrases = Glossary.CountWords(new[] { "parasitic. egg", "parasitic. egg", "parasitic\negg", "parasitic and egg" });
            Check(!phrases.ContainsKey("parasitic egg"), "phrases do not cross punctuation, line breaks or stop words");
            phrases = Glossary.CountWords(new[] { "egg egg egg" });
            Check(phrases["egg"] == 3 && !phrases.ContainsKey("egg egg"), "overlapping substrings alone do not qualify as two phrase occurrences");
            Glossary.Merge(settings, words, "en", "zh-CN");
            Check(settings.GlossaryTerms.First(term => term.Source == "pawn").Translation == "角色", "rescanning preserves manually chosen translations");
            Check(settings.GlossaryTerms.Any(term => term.Source == "rifle" && term.Count == 2 && term.Translation == ""), "frequent words become inactive editable candidates");
            Check(!settings.GlossaryTerms.Any(term => term.Source == "table"), "single-occurrence words are not added automatically");
            int termCount = settings.GlossaryTerms.Count;
            Glossary.Merge(settings, words, "English", "ChineseSimplified");
            Check(settings.GlossaryTerms.Count == termCount, "rescanning does not duplicate terms across language aliases");

            string defs = Path.Combine(folder, "1.6", "Defs");
            string english = Path.Combine(folder, "1.6", "Languages", "English", "Keyed");
            string japanese = Path.Combine(folder, "1.6", "Languages", "Japanese", "Keyed");
            Directory.CreateDirectory(defs); Directory.CreateDirectory(english); Directory.CreateDirectory(japanese);
            File.WriteAllText(Path.Combine(defs, "Things.xml"), "<Defs><ThingDef><defName>TechnicalIdentity</defName><label>ancient rifle</label><description>A pawn uses a rifle.</description></ThingDef></Defs>");
            File.WriteAllText(Path.Combine(english, "Keys.xml"), "<LanguageData><Example>Another pawn and another rifle.</Example></LanguageData>");
            File.WriteAllText(Path.Combine(japanese, "Keys.xml"), "<LanguageData><Example>ExcludedWord ExcludedWord</Example></LanguageData>");
            File.WriteAllText(Path.Combine(defs, "Broken.xml"), "<broken>");
            Dictionary<string, int> scanned = Glossary.ScanFiles(new[] { Path.Combine(folder, "1.6"), Path.Combine(folder, "1.6") });
            Check(scanned["rifle"] == 3 && scanned["pawn"] == 2, "file scan reads Def text and English Keyed text without counting duplicate paths");
            Check(!scanned.ContainsKey("technicalidentity") && !scanned.ContainsKey("excludedword"), "file scan excludes Def IDs and non-English translations");
            File.WriteAllText(Path.Combine(defs, "PhraseA.xml"), "<Defs><ThingDef><label>parasitic egg</label></ThingDef></Defs>");
            File.WriteAllText(Path.Combine(english, "PhraseB.xml"), "<LanguageData><Egg>Parasitic egg</Egg></LanguageData>");
            scanned = Glossary.ScanFiles(new[] { Path.Combine(folder, "1.6") });
            Check(scanned["parasitic egg"] == 2 && !scanned.ContainsKey("parasitic") && !scanned.ContainsKey("egg"),
                "phrase frequency is combined across mod XML files before consuming component words");
            Glossary.Merge(settings, scanned, "en", "zh-CN");
            Check(settings.GlossaryTerms.Any(term => term.Source == "parasitic egg" && term.Count == 2), "repeated phrases become editable glossary candidates");
            string old = Path.Combine(folder, "1.5", "Defs"); Directory.CreateDirectory(old);
            File.WriteAllText(Path.Combine(old, "Old.xml"), "<Defs><ThingDef><label>obsolete obsolete</label></ThingDef></Defs>");
            Verse.ModContentPack pack = new Verse.ModContentPack { RootDir = folder, PackageIdPlayerFacing = "glossary.mod", foldersToLoadDescendingOrder = new List<string> { "1.6" } };
            Verse.LoadedModManager.RunningMods.Clear(); Verse.LoadedModManager.RunningMods.Add(pack);
            settings.SelectedPackageIds = new List<string> { "glossary.mod" };
            List<string> active = ModTextScanner.SelectedContentFolders(settings);
            Check(active.Single() == Path.Combine(folder, "1.6") && !Glossary.ScanFiles(active).ContainsKey("obsolete"), "scan selects the mod's active version folders");
            pack.AllDefs = new List<Verse.Def> { new Verse.Def { defName = "PawnLabel", label = "A pawn sleeps." } };
            string firstHash = ModTextScanner.Scan(settings).Single(entry => entry.DefTypeName == "Def").SourceHash;
            settings.GlossaryTerms[0].Translation = "小人";
            Check(ModTextScanner.Scan(settings).Single(entry => entry.DefTypeName == "Def").SourceHash != firstHash, "actual scanner keys change when the glossary translation changes");
            return passed;
        }
    }
}