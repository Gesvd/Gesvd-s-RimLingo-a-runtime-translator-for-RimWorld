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
        private static readonly Dictionary<string, HashSet<string>> OtherStopWords = new Dictionary<string, HashSet<string>>
        {
            ["de"] = new HashSet<string>("der die das ein eine einer eines einem einen und oder aber als für von zu mit auf in an im am aus bei ist sind war werden wird nicht nur auch sich des dem den es sie er wir ihr man wenn dann dass durch um ohne nach vor über unter".Split(' ')),
            ["fr"] = new HashSet<string>("le la les un une des du de à au aux et ou mais pour par avec sans dans sur sous en est sont être ce cette ces il elle ils elles nous vous se ne pas qui que quand plus comme son sa ses leur leurs".Split(' ')),
            ["es"] = new HashSet<string>("el la los las un una unos unas de del a al y o pero para por con sin en sobre es son ser se no que quien como cuando su sus este esta estos estas más menos muy lo".Split(' ')),
            ["pt"] = new HashSet<string>("o a os as um uma uns umas de do da dos das e ou mas para por com sem em no na nos nas é são ser se não que quem como quando seu sua seus suas este esta estes estas mais menos muito".Split(' ')),
            ["ru"] = new HashSet<string>("и или но в во на с со к ко у о об от до из за для по под над при через без это этот эта эти его её их он она они мы вы не ни да нет как что если когда то есть был была были быть будет только также".Split(' ')),
            ["ko"] = new HashSet<string>("이 그 저 및 또는 와 과 은 는 을 를 에 의 가 한 수 것 있는 없는 합니다 그리고 하지만 위해 으로 로 에서".Split(' '))
        };

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

        public static Dictionary<string, int> CountWords(IEnumerable<string> texts, CancellationToken token = default(CancellationToken), string source = "English")
        {
            string language = LanguageKey(source);
            bool unspaced = language == "zh" || language == "zt" || language == "ja";
            HashSet<string> stop = OtherStopWords.TryGetValue(language, out var localStop) ? localStop : StopWords;
            string letters = @"\u3400-\u9fff\u3040-\u30ff";
            string pattern = unspaced ? "(?<![" + letters + "])[" + letters + "]{2,16}(?![" + letters + "])"
                : @"(?<![\p{L}\p{M}\p{N}_])[\p{L}\p{M}]+(?:['’][\p{L}\p{M}]+)?(?![\p{L}\p{M}\p{N}_])";
            if (language == "en") pattern = @"(?<![\p{L}\p{N}_])[A-Za-z]+(?:['’][A-Za-z]+)?(?![\p{L}\p{N}_])";
            List<List<string>> runs = new List<List<string>>();
            Dictionary<string, int> phrases = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string text in texts)
            {
                token.ThrowIfCancellationRequested();
                string plain = Regex.Replace(TextProtection.Protect(text).Text, @"__RT_TOKEN_\d+__", "|");
                List<string> run = null;
                int previousEnd = -1;
                foreach (Match match in Regex.Matches(plain, pattern))
                {
                    string word = match.Value.ToLowerInvariant();
                    if (language == "en" && (word.EndsWith("'s", StringComparison.Ordinal) || word.EndsWith("’s", StringComparison.Ordinal))) word = word.Substring(0, word.Length - 2);
                    if (word.Length < 2 || stop.Contains(word)) { run = null; previousEnd = -1; continue; }
                    if (unspaced || run == null || !Regex.IsMatch(plain.Substring(previousEnd, match.Index - previousEnd), @"^[ \t]+$"))
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

        public static Dictionary<string, int> ScanFiles(IEnumerable<string> folders, CancellationToken token = default(CancellationToken), string source = "English")
        {
            HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> texts = new List<string>();
            foreach (string folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                IEnumerable<string> relatives = BundledTranslations.LanguageFolders(source).Select(language => Path.Combine("Languages", language));
                if (LanguageKey(source) == "en") relatives = new[] { "Defs" }.Concat(relatives);
                foreach (string relative in relatives)
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
            return CountWords(texts, token, source);
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
