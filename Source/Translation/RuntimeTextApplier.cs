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
                if (entry.DefTypeName == "Keyed") return UiText.Apply(entry);
                ModContentPack pack = LoadedModManager.RunningMods
                    .FirstOrDefault(mod => mod.PackageIdPlayerFacing == entry.PackageId);
                Def def = pack == null
                    ? null
                    : GetDefs(pack).FirstOrDefault(candidate =>
                        candidate != null &&
                        candidate.defName == entry.DefName &&
                        candidate.GetType().Name == entry.DefTypeName);

                if (def == null)
                {
                    return false;
                }

                FieldInfo field = def.GetType().GetField(entry.FieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null || field.FieldType != typeof(string))
                {
                    return false;
                }

                if (!originalTexts.TryGetValue(def, out Dictionary<string, string> fields))
                {
                    fields = new Dictionary<string, string>();
                    originalTexts.Add(def, fields);
                }
                if (!fields.ContainsKey(field.Name))
                {
                    fields.Add(field.Name, field.GetValue(def) as string);
                }
                field.SetValue(def, string.IsNullOrWhiteSpace(entry.TranslatedText) ? fields[field.Name] : entry.TranslatedText);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("[RuntimeTranslator] Could not apply translation for " + entry.Id + ": " + ex.Message);
                return false;
            }
        }

        private static IEnumerable<Def> GetDefs(ModContentPack pack)
        {
            PropertyInfo allDefsProperty = pack.GetType().GetProperty("AllDefs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (allDefsProperty != null && allDefsProperty.GetValue(pack, null) is IEnumerable<Def> defs)
            {
                return defs;
            }
            return Enumerable.Empty<Def>();
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
