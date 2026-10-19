using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using Verse;

namespace RuntimeTranslator
{
    public static class ModTextScanner
    {
        private static readonly HashSet<string> SupportedFields = new HashSet<string>
        {
            "label",
            "description",
            "jobString",
            "deathMessage",
            "gerund",
            "reportString"
        };

        public static List<TranslationEntry> Scan(RuntimeTranslatorSettings settings)
        {
            List<TranslationEntry> entries = new List<TranslationEntry>();
            HashSet<string> selected = new HashSet<string>(settings.SelectedPackageIds ?? new List<string>());
            List<GlossaryTerm> terms = Glossary.ForLanguages(settings, settings.SourceLanguage, settings.TargetLanguage);
            foreach (ModContentPack pack in LoadedModManager.RunningMods)
            {
                if (pack == null || !selected.Contains(pack.PackageIdPlayerFacing))
                {
                    continue;
                }

                entries.AddRange(UiText.Scan(pack, settings));
                IEnumerable<Def> defs = GetDefs(pack);
                foreach (Def def in defs)
                {
                    entries.AddRange(ScanDef(pack, def, settings, terms));
                }
            }

            return entries
                .GroupBy(entry => entry.Id)
                .Select(group => group.First())
                .OrderBy(entry => entry.ModName)
                .ThenBy(entry => entry.DefTypeName)
                .ThenBy(entry => entry.DefName)
                .ThenBy(entry => entry.FieldName)
                .ToList();
        }

        private static IEnumerable<Def> GetDefs(ModContentPack pack)
        {
            try
            {
                PropertyInfo allDefsProperty = pack.GetType().GetProperty("AllDefs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (allDefsProperty != null && allDefsProperty.GetValue(pack, null) is IEnumerable<Def> defs)
                {
                    return defs;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[RuntimeTranslator] Could not read defs for " + pack.Name + ": " + ex.Message);
            }
            return Enumerable.Empty<Def>();
        }

        internal static bool IsSupportedField(string name) { return SupportedFields.Contains(name); }

        public static List<string> SelectedContentFolders(RuntimeTranslatorSettings settings)
        {
            HashSet<string> selected = new HashSet<string>(settings.SelectedPackageIds ?? new List<string>());
            List<string> folders = new List<string>();
            foreach (ModContentPack pack in LoadedModManager.RunningMods.Where(pack => pack != null && selected.Contains(pack.PackageIdPlayerFacing)))
            {
                Type type = pack.GetType();
                object loaded = type.GetProperty("FoldersToLoad", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(pack, null)
                    ?? type.GetField("foldersToLoadDescendingOrder", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(pack)
                    ?? type.GetField("foldersToLoad", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(pack);
                if (loaded is IEnumerable<string> paths && paths.Any())
                    folders.AddRange(paths.Select(path => Path.IsPathRooted(path) ? path : Path.Combine(pack.RootDir, path)));
                else folders.Add(pack.RootDir);
            }
            return folders.Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static IEnumerable<TranslationEntry> ScanDef(ModContentPack pack, Def def, RuntimeTranslatorSettings settings, List<GlossaryTerm> terms)
        {
            if (def == null)
            {
                yield break;
            }

            Type type = def.GetType();
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!IsSupportedField(field.Name) || field.FieldType != typeof(string))
                {
                    continue;
                }

                string source = RuntimeTextApplier.ReadSource(def, field);
                if (!ShouldTranslate(source))
                {
                    continue;
                }

                string defName = def.defName ?? "UnnamedDef";
                string id = string.Join("|", pack.PackageIdPlayerFacing, type.Name, defName, field.Name, settings.SourceLanguage, settings.TargetLanguage);
                yield return new TranslationEntry
                {
                    Id = id,
                    PackageId = pack.PackageIdPlayerFacing,
                    ModName = pack.Name,
                    DefTypeName = type.Name,
                    DefName = defName,
                    FieldName = field.Name,
                    SourceLanguage = settings.SourceLanguage,
                    TargetLanguage = settings.TargetLanguage,
                    SourceText = source,
                    SourceHash = Glossary.SourceHash(source, terms)
                };
            }
        }

        private static bool ShouldTranslate(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }
            if (source.Length <= 1)
            {
                return false;
            }
            return source.Any(char.IsLetter);
        }
    }
}
