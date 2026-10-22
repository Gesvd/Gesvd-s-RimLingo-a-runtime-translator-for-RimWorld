using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Threading;
using System.Xml.Linq;
using Verse;

namespace RuntimeTranslator
{
    public sealed class GlossaryTerm : IExposable
    {
        public string Source = string.Empty;
        public string Translation = string.Empty;
        public string SourceLanguage = "English";
        public string TargetLanguage = "ChineseSimplified";
        public int Count;

        public GlossaryTerm Clone() { return (GlossaryTerm)MemberwiseClone(); }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Source, "source", string.Empty);
            Scribe_Values.Look(ref Translation, "translation", string.Empty);
            Scribe_Values.Look(ref SourceLanguage, "sourceLanguage", "English");
            Scribe_Values.Look(ref TargetLanguage, "targetLanguage", "ChineseSimplified");
            Scribe_Values.Look(ref Count, "count", 0);
        }
    }

    public static class Glossary
    {
        private static readonly HashSet<string> StopWords = new HashSet<string>((
            "a an the and or but nor so yet for of to in on at by with from into onto upon out off over under above below " +
            "between through during before after without within against about around as than if then else when where while " +
            "because although though unless until whether that this these those it its it's is isn't are aren't was wasn't were weren't " +
            "be been being am do does did don't doesn't didn't have has had having will would shall should can could may might must " +
            "not no yes all any some each every both either neither other another such same more most less least very much many few " +
            "i me my mine we us our ours you your yours he him his she her hers they them their theirs who whom whose which what " +
            "there here also only just even still already now up down again once too how why own itself themselves yourself yourselves " +
            "one two first second get gets got use used using make made makes need needs needed able cannot can't won't wouldn't").Split(' '));

        public static List<GlossaryTerm> Defaults()
        {
            return new List<GlossaryTerm>
            {
                new GlossaryTerm { Source = "pawn", Translation = "小人" },
                new GlossaryTerm { Source = "pawns", Translation = "小人" }
            };
        }

        public static string LanguageKey(string language)
        {
            string code = LanguageCatalog.Code(language);
            return code == "auto" ? "en" : code;
        }

        public static List<GlossaryTerm> ForLanguages(RuntimeTranslatorSettings settings, string source, string target)
        {
            return (settings.GlossaryTerms ?? new List<GlossaryTerm>())
                .Where(term => term != null && !string.IsNullOrWhiteSpace(term.Source) && LanguageKey(term.SourceLanguage) == LanguageKey(source) &&
                    LanguageKey(term.TargetLanguage) == LanguageKey(target)).ToList();
        }

        public static string SourceHash(string source, IEnumerable<GlossaryTerm> terms)
        {
            ProtectedText protection = TextProtection.Protect(source, terms);
            string enforced = protection.Restore(protection.Text);
            return TextHasher.Sha1(source == enforced ? source : source + "\0Glossary\0" + enforced);
        }

        public static Dictionary<string, int> CountWords(IEnumerable<string> texts, CancellationToken token = default(CancellationToken))
        {
            List<List<string>> runs = new List<List<string>>();
            Dictionary<string, int> phrases = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string text in texts)
            {
                token.ThrowIfCancellationRequested();
                string plain = Regex.Replace(TextProtection.Protect(text).Text, @"__RT_TOKEN_\d+__", "|");
                List<string> run = null;
                int previousEnd = -1;
                foreach (Match match in Regex.Matches(plain, @"(?<![\p{L}\p{N}_])[A-Za-z]+(?:'[A-Za-z]+)?(?![\p{L}\p{N}_])"))
                {
                    string word = match.Value.ToLowerInvariant();
                    if (word.EndsWith("'s", StringComparison.Ordinal)) word = word.Substring(0, word.Length - 2);
                    if (word.Length < 2 || StopWords.Contains(word)) { run = null; previousEnd = -1; continue; }
                    if (run == null || !Regex.IsMatch(plain.Substring(previousEnd, match.Index - previousEnd), @"^[ \t]+$"))
                    {
                        run = new List<string>(); runs.Add(run);
                    }
                    run.Add(word);
                    previousEnd = match.Index + match.Length;
                }
            }
            foreach (List<string> run in runs)
            {
                token.ThrowIfCancellationRequested();
                Dictionary<string, int> nextStart = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int offset = 0; offset < run.Count; offset++)
                for (int length = 2; length <= Math.Min(6, run.Count - offset); length++)
                {
                    string phrase = string.Join(" ", run.GetRange(offset, length));
                    if (nextStart.TryGetValue(phrase, out int next) && offset < next) continue;
                    nextStart[phrase] = offset + length;
                    phrases.TryGetValue(phrase, out int count);
                    phrases[phrase] = count + 1;
                }
            }
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (List<string> run in runs)
            {
                token.ThrowIfCancellationRequested();
                for (int offset = 0; offset < run.Count;)
                {
                    int length = Math.Min(6, run.Count - offset);
                    string candidate = null;
                    for (; length >= 2; length--)
                    {
                        candidate = string.Join(" ", run.GetRange(offset, length));
                        if (phrases[candidate] >= 2) break;
                    }
                    if (length < 2) { length = 1; candidate = run[offset]; }
                    counts.TryGetValue(candidate, out int count);
                    counts[candidate] = count + 1;
                    offset += length;
                }
            }
            return counts;
        }

        public static Dictionary<string, int> ScanFiles(IEnumerable<string> folders, CancellationToken token = default(CancellationToken))
        {
            HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> texts = new List<string>();
            foreach (string folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (string relative in new[] { "Defs", Path.Combine("Languages", "English") })
                {
                    string path = Path.Combine(folder, relative);
                    if (!Directory.Exists(path)) continue;
                    foreach (string file in Directory.EnumerateFiles(path, "*.xml", SearchOption.AllDirectories))
                    {
                        token.ThrowIfCancellationRequested();
                        if (!files.Add(file)) continue;
                        try
                        {
                            using (XmlReader reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                            {
                                XDocument document = XDocument.Load(reader);
                                texts.AddRange(document.Descendants().Where(element => !element.HasElements &&
                                    (relative != "Defs" || ModTextScanner.IsSupportedField(element.Name.LocalName)))
                                    .Select(element => element.Value));
                            }
                        }
                        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is XmlException)
                        {
                            Log.Warning("[RuntimeTranslator] Glossary skipped " + file + ": " + ex.Message);
                        }
                    }
                }
            }
            return CountWords(texts, token);
        }

        public static void Merge(RuntimeTranslatorSettings settings, Dictionary<string, int> counts, string source, string target)
        {
            List<GlossaryTerm> existing = ForLanguages(settings, source, target);
            foreach (GlossaryTerm term in existing)
            {
                counts.TryGetValue(term.Source, out int count);
                term.Count = count;
            }
            HashSet<string> known = new HashSet<string>(existing.Select(term => term.Source), StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, int> word in counts.Where(pair => pair.Value >= 2))
            {
                if (!known.Add(word.Key)) continue;
                settings.GlossaryTerms.Add(new GlossaryTerm { Source = word.Key, Count = word.Value, SourceLanguage = source, TargetLanguage = target });
            }
        }
    }
}
