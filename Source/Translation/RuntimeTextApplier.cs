using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace RuntimeTranslator
{
    public static class RuntimeTextApplier
    {
        private static readonly Dictionary<Def, Dictionary<string, string>> originalTexts = new Dictionary<Def, Dictionary<string, string>>();
        private static readonly Dictionary<string, Def> definitions = new Dictionary<string, Def>();
        private static readonly HashSet<ModContentPack> indexedPacks = new HashSet<ModContentPack>();
        private static readonly Dictionary<Type, FieldInfo[]> fieldsByType = new Dictionary<Type, FieldInfo[]>();
        private static readonly Dictionary<LoadedLanguage, Dictionary<string, LoadedLanguage.KeyedReplacement>> keyedOriginals = new Dictionary<LoadedLanguage, Dictionary<string, LoadedLanguage.KeyedReplacement>>();

        internal static bool IsRuntimeKey(LoadedLanguage language, string key)
        { return language != null && keyedOriginals.TryGetValue(language, out var originals) && originals.ContainsKey(key); }

        private static bool ApplyKeyed(TranslationEntry entry)
        {
            if (UiText.Apply(entry)) return true;
            LoadedLanguage language = LanguageDatabase.activeLanguage;
            if (language == null) return false;
            if (!keyedOriginals.TryGetValue(language, out var originals))
                keyedOriginals[language] = originals = new Dictionary<string, LoadedLanguage.KeyedReplacement>();
            if (!originals.ContainsKey(entry.DefName))
            {
                string current = entry.DefName.Translate().ToString();
                if (entry.SourceText != null && current != entry.SourceText) return false;
                language.keyedReplacements.TryGetValue(entry.DefName, out var original);
                originals[entry.DefName] = original;
            }
            if (!TextProtection.HasVisibleText(entry.TranslatedText))
            {
                if (originals[entry.DefName] == null) language.keyedReplacements.Remove(entry.DefName);
                else language.keyedReplacements[entry.DefName] = originals[entry.DefName];
                originals.Remove(entry.DefName);
            }
            else language.keyedReplacements[entry.DefName] = new LoadedLanguage.KeyedReplacement
            { key = entry.DefName, value = entry.TranslatedText, fileSource = "RuntimeTranslator" };
            return true;
        }

        internal static void ResetIndex() { definitions.Clear(); indexedPacks.Clear(); fieldsByType.Clear(); }

        internal static FieldInfo[] Fields(Type type)
        {
            if (!fieldsByType.TryGetValue(type, out FieldInfo[] fields))
                fieldsByType[type] = fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(field => field.FieldType == typeof(string) && ModTextScanner.IsSupportedField(field.Name)).ToArray();
            return fields;
        }

        internal static void Register(ModContentPack pack, Def def)
        {
            if (def != null) definitions[pack.PackageIdPlayerFacing + "|" + def.GetType().Name + "|" + def.defName] = def;
        }

        public static string ReadSource(Def def, FieldInfo field)
        {
            if (originalTexts.TryGetValue(def, out Dictionary<string, string> fields) && fields.TryGetValue(field.Name, out string source))
            {
                return source;
            }
            return field.GetValue(def) as string;
        }

        public static bool Apply(TranslationEntry entry)
        {
            if (entry == null)
            {
                return false;
            }

            try
            {
                NativeTranslations.Prepare(entry.TargetLanguage);
                if (NativeTranslations.Contains(entry)) return false;
                if (entry.DefTypeName == "Keyed") return ApplyKeyed(entry);
                string key = entry.PackageId + "|" + entry.DefTypeName + "|" + entry.DefName;
                if (!definitions.TryGetValue(key, out Def def))
                {
                    ModContentPack pack = LoadedModManager.RunningMods.FirstOrDefault(mod => mod.PackageIdPlayerFacing == entry.PackageId);
                    if (pack != null && indexedPacks.Add(pack))
                        foreach (Def candidate in ModTextScanner.GetDefs(pack)) Register(pack, candidate);
                    definitions.TryGetValue(key, out def);
                }

                if (def == null)
                {
                    return false;
                }

                FieldInfo field = Fields(def.GetType()).FirstOrDefault(candidate => candidate.Name == entry.FieldName);
                if (field == null || field.FieldType != typeof(string))
                {
                    return false;
                }
                if (entry.SourceText != null && ReadSource(def, field) != entry.SourceText) return false;

                if (!originalTexts.TryGetValue(def, out Dictionary<string, string> fields))
                {
                    fields = new Dictionary<string, string>();
                    originalTexts.Add(def, fields);
                }
                if (!fields.ContainsKey(field.Name))
                {
                    fields.Add(field.Name, field.GetValue(def) as string);
                }
                field.SetValue(def, !TextProtection.HasVisibleText(entry.TranslatedText) ? fields[field.Name] : entry.TranslatedText);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("[RuntimeTranslator] Could not apply translation for " + entry.Id + ": " + ex.Message);
                return false;
            }
        }

        public static void ApplyMany(IEnumerable<TranslationEntry> entries, int limit)
        {
            int count = 0;
            foreach (TranslationEntry entry in entries)
            {
                Apply(entry);
                count++;
                if (count >= limit)
                {
                    return;
                }
            }
        }
    }
}
