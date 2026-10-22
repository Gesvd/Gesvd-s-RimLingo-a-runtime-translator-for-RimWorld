using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading;
using Verse;

namespace RuntimeTranslator
{
    public static class ModTextScanner
    {
        internal sealed class ModFiles
        {
            public string Name;
            public List<string> Folders;
        }
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
            return FinishScan(CaptureSteps(settings, CancellationToken.None).Where(entry => entry != null).ToList(),
                settings, SelectedFolders(settings), CancellationToken.None);
        }

        internal static Dictionary<string, ModFiles> SelectedFolders(RuntimeTranslatorSettings settings)
        {
            HashSet<string> selected = new HashSet<string>(settings.SelectedPackageIds ?? new List<string>());
            return LoadedModManager.RunningMods.Where(pack => IsPlayerMod(pack) && selected.Contains(pack.PackageIdPlayerFacing))
                .GroupBy(pack => pack.PackageIdPlayerFacing).ToDictionary(group => group.Key,
                    group => new ModFiles { Name = group.First().Name, Folders = ContentFolders(group.First()) });
        }

        // Only this iterator touches loaded game objects; null is a frame checkpoint.
        internal static IEnumerable<TranslationEntry> CaptureSteps(RuntimeTranslatorSettings settings, CancellationToken token)
        {
            foreach (int step in NativeTranslations.PrepareSteps(settings.TargetLanguage, token)) yield return null;
            HashSet<string> selected = new HashSet<string>(settings.SelectedPackageIds ?? new List<string>());
            foreach (ModContentPack pack in LoadedModManager.RunningMods)
            {
                token.ThrowIfCancellationRequested();
                if (!IsPlayerMod(pack) || !selected.Contains(pack.PackageIdPlayerFacing))
                {
                    continue;
                }

                foreach (TranslationEntry entry in UiText.Scan(pack, settings)) yield return entry;
                IEnumerable<Def> defs = GetDefs(pack);
                foreach (Def def in defs)
                {
                    token.ThrowIfCancellationRequested();
                    RuntimeTextApplier.Register(pack, def);
                    foreach (TranslationEntry entry in ScanDef(pack, def, settings)) yield return entry;
                    yield return null;
                }
            }
        }

        internal static List<TranslationEntry> FinishScan(List<TranslationEntry> entries, RuntimeTranslatorSettings settings,
            Dictionary<string, ModFiles> folders, CancellationToken token)
        {
            HashSet<string> excluded = new HashSet<string>();
            if (settings.SkipModsWithBundledChinese)
                foreach (var mod in folders)
                {
                    token.ThrowIfCancellationRequested();
                    if (BundledTranslations.HasTranslation(mod.Value.Folders, settings.TargetLanguage, token)) excluded.Add(mod.Key);
                }
            foreach (var mod in folders.Where(pair => !excluded.Contains(pair.Key)))
            foreach (var text in BundledTranslations.KeyedSources(mod.Value.Folders, settings.SourceLanguage, token))
            {
                token.ThrowIfCancellationRequested();
                if (!ShouldTranslate(text.Value)) continue;
                string key = text.Key.Substring("Keyed|".Length);
                entries.Add(new TranslationEntry
                {
                    Id = string.Join("|", mod.Key, "Keyed", key, "text", settings.SourceLanguage, settings.TargetLanguage),
                    PackageId = mod.Key, ModName = mod.Value.Name, DefTypeName = "Keyed", DefName = key, FieldName = "text",
                    SourceLanguage = settings.SourceLanguage, TargetLanguage = settings.TargetLanguage, SourceText = text.Value
                });
            }
            List<GlossaryTerm> terms = Glossary.ForLanguages(settings, settings.SourceLanguage, settings.TargetLanguage);
            foreach (TranslationEntry entry in entries)
            {
                token.ThrowIfCancellationRequested();
                entry.SourceHash = Glossary.SourceHash(entry.SourceText, terms);
            }
            return entries
                .Where(entry => !excluded.Contains(entry.PackageId) && !NativeTranslations.Contains(entry))
                .GroupBy(entry => entry.Id)
                .Select(group => group.First())
                .OrderBy(entry => entry.ModName)
                .ThenBy(entry => entry.DefTypeName)
                .ThenBy(entry => entry.DefName)
                .ThenBy(entry => entry.FieldName)
                .ToList();
        }

        internal static IEnumerable<Def> GetDefs(ModContentPack pack)
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

        public static bool IsPlayerMod(ModContentPack pack) { return pack != null && !pack.IsOfficialMod; }

        public static void ExcludeOfficialSelections(RuntimeTranslatorSettings settings)
        {
            HashSet<string> official = new HashSet<string>(LoadedModManager.RunningMods.Where(pack => pack != null && pack.IsOfficialMod).Select(pack => pack.PackageIdPlayerFacing));
            settings.SelectedPackageIds?.RemoveAll(official.Contains);
        }

        public static List<string> ContentFolders(ModContentPack pack)
        {
            Type type = pack.GetType();
            object loaded = type.GetProperty("FoldersToLoad", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(pack, null)
                ?? type.GetField("foldersToLoadDescendingOrder", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(pack)
                ?? type.GetField("foldersToLoad", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(pack);
            IEnumerable<string> folders = loaded is IEnumerable<string> paths && paths.Any()
                ? paths.Select(path => Path.IsPathRooted(path) ? path : Path.Combine(pack.RootDir, path)) : new[] { pack.RootDir };
            return folders.Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<List<string>> SelectedModFolders(RuntimeTranslatorSettings settings)
        {
            HashSet<string> selected = new HashSet<string>(settings.SelectedPackageIds ?? new List<string>());
            return LoadedModManager.RunningMods.Where(pack => IsPlayerMod(pack) && selected.Contains(pack.PackageIdPlayerFacing))
                .Select(ContentFolders).ToList();
        }

        public static List<string> SelectedContentFolders(RuntimeTranslatorSettings settings)
        {
            return SelectedModFolders(settings).SelectMany(folders => folders).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static IEnumerable<TranslationEntry> ScanDef(ModContentPack pack, Def def, RuntimeTranslatorSettings settings)
        {
            if (def == null)
            {
                yield break;
            }

            Type type = def.GetType();
            foreach (FieldInfo field in RuntimeTextApplier.Fields(type))
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
                    SourceHash = null
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
