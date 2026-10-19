using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Verse;

namespace RuntimeTranslator
{
    public static class UiText
    {
        private static readonly Dictionary<string, string> originals = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> overrides = new Dictionary<string, string>();
        private static string packageId;
        private static string modName;

        public static void Initialize(ModContentPack pack)
        {
            packageId = pack.PackageIdPlayerFacing;
            modName = pack.Name;
            originals.Clear();
            overrides.Clear();
            try
            {
                string path = Path.Combine(pack.RootDir, "Languages", "English", "Keyed", "RuntimeTranslator.xml");
                foreach (XElement element in XDocument.Load(path).Root.Elements()) originals[element.Name.LocalName] = element.Value;
            }
            catch (Exception ex) { Log.Warning("[RuntimeTranslator] Could not load UI language table: " + ex.Message); }
        }

        internal static string Key(string english) { return "RT_" + TextHasher.Sha1(english); }

        public static string Get(string english)
        {
            string key = Key(english);
            string native = NativeTranslations.ActiveKeyed(key);
            if (native != null) return native;
            if (overrides.TryGetValue(key, out string runtime)) return runtime;
            if (!originals.ContainsKey(key)) return english;
            string translated = key.Translate().ToString();
            return translated == key ? english : translated;
        }

        public static IEnumerable<TranslationEntry> Scan(ModContentPack pack, RuntimeTranslatorSettings settings)
        {
            if (pack.PackageIdPlayerFacing != packageId) yield break;
            List<GlossaryTerm> terms = Glossary.ForLanguages(settings, settings.SourceLanguage, settings.TargetLanguage);
            foreach (KeyValuePair<string, string> text in originals.OrderBy(pair => pair.Key))
            {
                yield return new TranslationEntry
                {
                    Id = string.Join("|", packageId, "Keyed", text.Key, "text", settings.SourceLanguage, settings.TargetLanguage),
                    PackageId = packageId, ModName = modName, DefTypeName = "Keyed", DefName = text.Key, FieldName = "text",
                    SourceLanguage = settings.SourceLanguage, TargetLanguage = settings.TargetLanguage,
                    SourceText = text.Value, SourceHash = Glossary.SourceHash(text.Value, terms)
                };
            }
        }

        public static bool Apply(TranslationEntry entry)
        {
            if (entry.PackageId != packageId || !originals.ContainsKey(entry.DefName)) return false;
            if (!TextProtection.HasVisibleText(entry.TranslatedText)) overrides.Remove(entry.DefName);
            else overrides[entry.DefName] = entry.TranslatedText;
            return true;
        }
    }
}