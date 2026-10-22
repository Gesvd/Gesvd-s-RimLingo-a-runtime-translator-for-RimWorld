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
        private static string[] ChineseFolders(string language)
        {
            string key = Glossary.LanguageKey(language);
            if (key == "zh") return new[] { "ChineseSimplified", "Chinese", "zh", "zh-CN", "zh-Hans" };
            if (key == "zt" || key == "chinesetraditional" || key == "zh-tw" || key == "zh-hant") return new[] { "ChineseTraditional", "zh-TW", "zh-Hant" };
            return new string[0];
        }

        public static bool CanImport(string source, string target) { return Glossary.LanguageKey(source) == "en" && ChineseFolders(target).Length > 0; }

        private static bool HasChinese(string text) { return Regex.IsMatch(text ?? "", @"[\u3400-\u9fff]"); }

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
        private static IEnumerable<KeyValuePair<string, string>> LanguageEntries(IEnumerable<string> folders, string[] languages, CancellationToken token)
        {
            foreach (string folder in folders)
            foreach (string language in languages)
            foreach (string kind in new[] { "Keyed", "DefInjected" })
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
                        yield return new KeyValuePair<string, string>(prefix + element.Name.LocalName, element.Value.Trim());
                    }
                }
            }
        }

        internal static HashSet<string> NativeKeys(IEnumerable<string> folders, string target)
        {
            string[] languages = ChineseFolders(target);
            if (languages.Length == 0)
            {
                string key = Glossary.LanguageKey(target);
                string canonical = LanguageCatalog.Canonical(target);
                languages = new[] { canonical, key }.Where(name => !string.IsNullOrWhiteSpace(name) && name != "." && name != ".." && name.IndexOfAny(new[] { '/', '\\' }) < 0).Distinct().ToArray();
            }
            Dictionary<string, string> entries = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> pair in LanguageEntries(folders, languages, TranslationLifetime.Token))
                if (!entries.ContainsKey(pair.Key)) entries.Add(pair.Key, pair.Value);
            return new HashSet<string>(entries.Where(pair => TextProtection.HasVisibleText(pair.Value)).Select(pair => pair.Key));
        }

        public static bool HasChineseTranslation(IEnumerable<string> folders, string target, CancellationToken token = default(CancellationToken))
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            return LanguageEntries(folders, ChineseFolders(target), token).Any(pair => seen.Add(pair.Key) && HasChinese(pair.Value));
        }

        private static Dictionary<string, string> Sources(IEnumerable<string> folders, CancellationToken token)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> pair in LanguageEntries(folders, new[] { "English" }, token))
                if (!result.ContainsKey(pair.Key)) result.Add(pair.Key, pair.Value);
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
                throw new InvalidOperationException("Bundled glossary import requires English -> ChineseSimplified / ChineseTraditional.");
            Dictionary<string, Dictionary<string, int>> candidates = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            foreach (List<string> folders in mods)
            {
                token.ThrowIfCancellationRequested();
                Dictionary<string, string> english = Sources(folders, token);
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> pair in LanguageEntries(folders, ChineseFolders(target), token))
                {
                    if (!seen.Add(pair.Key) || !HasChinese(pair.Value) || pair.Value.Length > 64 || !english.TryGetValue(pair.Key, out string original)) continue;
                    if (original.Length > 64 || !Regex.IsMatch(original, @"^[A-Za-z][A-Za-z0-9' -]*$") || original.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length > 6 ||
                        !Glossary.CountWords(new[] { original }, token).Any() || !TextProtection.IsGlossarySource(original) || !TextProtection.IsGlossarySource(pair.Value)) continue;
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
