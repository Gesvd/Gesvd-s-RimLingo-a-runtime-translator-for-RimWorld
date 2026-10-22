using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Verse;

namespace RuntimeTranslator
{
    public static class BundledTranslations
    {
        internal static string[] LanguageFolders(string language)
        {
            string key = Glossary.LanguageKey(language);
            if (key == "en") return new[] { "English", "en" };
            if (key == "zh") return new[] { "ChineseSimplified", "Chinese", "zh", "zh-CN", "zh-Hans" };
            if (key == "zt" || key == "chinesetraditional" || key == "zh-tw" || key == "zh-hant") return new[] { "ChineseTraditional", "zh-TW", "zh-Hant" };
            if (key == "pt") return new[] { "Portuguese", "PortugueseBrazilian", "PortugueseBrazil", "pt", "pt-BR", "pt-PT" };
            if (key == "es") return new[] { "Spanish", "SpanishLatin", "SpanishLatinAmerican", "es", "es-ES", "es-419" };
            string canonical = LanguageCatalog.Canonical(language);
            return new[] { canonical, key }.Where(name => !string.IsNullOrWhiteSpace(name) && name != "." && name != ".." && name.IndexOfAny(new[] { '/', '\\' }) < 0).Distinct().ToArray();
        }

        public static bool CanImport(string source, string target)
        { return LanguageCatalog.IsSupported(source, true) && LanguageCatalog.IsSupported(target, false) && Glossary.LanguageKey(source) != Glossary.LanguageKey(target); }

        private static bool HasChinese(string text) { return Regex.IsMatch(text ?? "", @"[\u3400-\u9fff]"); }

        private static bool HasLanguageText(string text, string language)
        {
            string code = Glossary.LanguageKey(language);
            if (code == "zh" || code == "zt") return HasChinese(text);
            if (code == "ja") return Regex.IsMatch(text ?? "", @"[\u3040-\u30ff\u3400-\u9fff]");
            if (code == "ko") return Regex.IsMatch(text ?? "", @"[\uac00-\ud7af\u1100-\u11ff]");
            if (code == "ru") return Regex.IsMatch(text ?? "", @"[\u0400-\u052f]");
            return TextProtection.HasVisibleText(text) && (text ?? "").Any(char.IsLetter);
        }

        private static XDocument Read(string file)
        {
            try
            {
                using (XmlReader reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                    return XDocument.Load(reader);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is XmlException)
            {
                Log.Warning("[RuntimeTranslator] Bundled translations skipped " + file + ": " + ex.Message);
                return null;
            }
        }

        // Folders arrive in game load priority order; the first value for a key wins.
        private static IEnumerable<KeyValuePair<string, string>> LanguageEntries(IEnumerable<string> folders, string[] languages, CancellationToken token, bool keyedOnly = false)
        {
            foreach (string folder in folders)
            foreach (string language in languages)
            foreach (string kind in keyedOnly ? new[] { "Keyed" } : new[] { "Keyed", "DefInjected" })
            {
                token.ThrowIfCancellationRequested();
                string directory = Path.Combine(folder, "Languages", language, kind);
                if (!Directory.Exists(directory)) continue;
                foreach (string file in Directory.EnumerateFiles(directory, "*.xml", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    token.ThrowIfCancellationRequested();
                    XDocument document = Read(file);
                    if (document?.Root == null || document.Root.Name.LocalName != "LanguageData") continue;
                    string prefix = kind == "Keyed" ? "Keyed|" : "DefInjected|" + file.Substring(directory.Length + 1).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0] + "|";
                    foreach (XElement element in document.Root.Elements().Where(element => !element.HasElements))
                    {
                        token.ThrowIfCancellationRequested();
                        yield return new KeyValuePair<string, string>(prefix + element.Name.LocalName, keyedOnly ? element.Value : element.Value.Trim());
                    }
                }
            }
        }

        internal static HashSet<string> NativeKeys(IEnumerable<string> folders, string target, CancellationToken token = default(CancellationToken))
        {
            string[] languages = LanguageFolders(target);
            Dictionary<string, string> entries = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> pair in LanguageEntries(folders, languages, token))
                if (!entries.ContainsKey(pair.Key)) entries.Add(pair.Key, pair.Value);
            return new HashSet<string>(entries.Where(pair => TextProtection.HasVisibleText(pair.Value)).Select(pair => pair.Key));
        }

        public static bool HasChineseTranslation(IEnumerable<string> folders, string target, CancellationToken token = default(CancellationToken))
        {
            string code = Glossary.LanguageKey(target);
            return (code == "zh" || code == "zt") && HasTranslation(folders, target, token);
        }

        public static bool HasTranslation(IEnumerable<string> folders, string target, CancellationToken token = default(CancellationToken))
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            return LanguageEntries(folders, LanguageFolders(target), token).Any(pair => seen.Add(pair.Key) && HasLanguageText(pair.Value, target));
        }

        internal static Dictionary<string, string> KeyedSources(IEnumerable<string> folders, string source, CancellationToken token)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            foreach (var pair in LanguageEntries(folders, LanguageFolders(source), token, true))
                if (!result.ContainsKey(pair.Key)) result.Add(pair.Key, pair.Value);
            return result;
        }

        private static Dictionary<string, string> Sources(IEnumerable<string> folders, string source, CancellationToken token)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> pair in LanguageEntries(folders, LanguageFolders(source), token))
                if (!result.ContainsKey(pair.Key)) result.Add(pair.Key, pair.Value);
            if (Glossary.LanguageKey(source) != "en") return result;
            foreach (string folder in folders)
            {
                string defs = Path.Combine(folder, "Defs");
                if (!Directory.Exists(defs)) continue;
                foreach (string file in Directory.EnumerateFiles(defs, "*.xml", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    token.ThrowIfCancellationRequested();
                    XDocument document = Read(file);
                    if (document?.Root == null || document.Root.Name.LocalName != "Defs") continue;
                    foreach (XElement definition in document.Root.Elements())
                    {
                        token.ThrowIfCancellationRequested();
                        string name = definition.Element("defName")?.Value.Trim();
                        string label = definition.Element("label")?.Value.Trim();
                        if (string.IsNullOrEmpty(name) || label == null) continue;
                        string key = "DefInjected|" + definition.Name.LocalName + "|" + name + ".label";
                        if (!result.ContainsKey(key)) result.Add(key, label);
                    }
                }
            }
            return result;
        }

        public static List<GlossaryTerm> ReadGlossary(IEnumerable<List<string>> mods, string source, string target, CancellationToken token = default(CancellationToken))
        {
            if (!CanImport(source, target))
                throw new InvalidOperationException("Choose different supported source and target languages for glossary import.");
            Dictionary<string, Dictionary<string, int>> candidates = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            foreach (List<string> folders in mods)
            {
                token.ThrowIfCancellationRequested();
                Dictionary<string, string> originals = Sources(folders, source, token);
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> pair in LanguageEntries(folders, LanguageFolders(target), token))
                {
                    if (!seen.Add(pair.Key) || !HasLanguageText(pair.Value, target) || pair.Value.Length > 64 || !originals.TryGetValue(pair.Key, out string original)) continue;
                    if (original.Length > 64 || !Regex.IsMatch(original, @"^[\p{L}\p{N}][\p{L}\p{M}\p{N}'’ -]*$") || original.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length > 6 ||
                        !Glossary.CountWords(new[] { original }, token, source).Any() || !TextProtection.IsGlossarySource(original) || !TextProtection.IsGlossarySource(pair.Value)) continue;
                    if (!candidates.TryGetValue(original, out Dictionary<string, int> translations)) candidates.Add(original, translations = new Dictionary<string, int>());
                    translations.TryGetValue(pair.Value, out int count);
                    translations[pair.Value] = count + 1;
                }
            }
            return candidates.Where(pair => pair.Value.Count == 1).OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new GlossaryTerm { Source = pair.Key, Translation = pair.Value.Single().Key, Count = pair.Value.Single().Value,
                    SourceLanguage = source, TargetLanguage = target }).ToList();
        }

        public static int ImportGlossary(RuntimeTranslatorSettings settings, IEnumerable<GlossaryTerm> terms)
        {
            int added = 0;
            foreach (GlossaryTerm term in terms)
            {
                GlossaryTerm existing = Glossary.ForLanguages(settings, term.SourceLanguage, term.TargetLanguage)
                    .FirstOrDefault(item => string.Equals(item.Source, term.Source, StringComparison.OrdinalIgnoreCase));
                if (existing != null && !string.IsNullOrWhiteSpace(existing.Translation)) continue;
                if (existing == null) settings.GlossaryTerms.Add(term.Clone());
                else { existing.Translation = term.Translation; existing.Count = Math.Max(existing.Count, term.Count); }
                added++;
            }
            return added;
        }
    }
}
