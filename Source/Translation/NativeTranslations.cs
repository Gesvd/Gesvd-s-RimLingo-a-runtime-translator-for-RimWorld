using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace RuntimeTranslator
{
    public static class NativeTranslations
    {
        private static readonly object gate = new object();
        private static readonly Dictionary<string, HashSet<string>> languages = new Dictionary<string, HashSet<string>>();

        private static readonly Dictionary<string, LoadedLanguage> snapshots = new Dictionary<string, LoadedLanguage>();
        private static readonly Dictionary<string, string[]> defTypes = new Dictionary<string, string[]>();

        public static void Reset() { lock (gate) { languages.Clear(); snapshots.Clear(); defTypes.Clear(); } }

        // Capture on the main thread. Workers/exporters only read the immutable key set.
        public static void Prepare(string target)
        {
            foreach (int step in PrepareSteps(target, CancellationToken.None)) if (step < 0) Thread.Yield();
        }

        internal static IEnumerable<int> PrepareSteps(string target, CancellationToken token)
        {
            string language = Glossary.LanguageKey(target);
            LoadedLanguage active = LanguageDatabase.activeLanguage;
            bool ready;
            lock (gate) ready = languages.ContainsKey(language) && ReferenceEquals(snapshots[language], active);
            if (ready) yield break;
            HashSet<string> keys = new HashSet<string>();
            Dictionary<string, string[]> hierarchy = new Dictionary<string, string[]>();
            foreach (ModContentPack pack in LoadedModManager.RunningMods.Where(pack => pack != null))
            foreach (Def def in ModTextScanner.GetDefs(pack))
            {
                token.ThrowIfCancellationRequested();
                Type type = def?.GetType();
                if (type != null && !hierarchy.ContainsKey(type.Name))
                {
                    List<string> names = new List<string>();
                    for (Type current = type; current != null && typeof(Def).IsAssignableFrom(current); current = current.BaseType) names.Add(current.Name);
                    hierarchy[type.Name] = names.ToArray();
                }
                yield return 0;
            }
            if (active != null && Glossary.LanguageKey(active.folderName) == language)
            {
                foreach (var pair in active.keyedReplacements)
                {
                    token.ThrowIfCancellationRequested();
                    if (!RuntimeTextApplier.IsRuntimeKey(active, pair.Key) && TextProtection.HasVisibleText(pair.Value.value)) keys.Add("Keyed|" + pair.Key);
                    yield return 0;
                }
                foreach (DefInjectionPackage package in active.defInjections)
                {
                    if (package.defType == null) continue;
                    foreach (var injection in package.injections.Values.Where(item => TextProtection.HasVisibleText(item.replacedString)))
                    {
                        token.ThrowIfCancellationRequested();
                        keys.Add("DefInjected|" + package.defType.Name + "|" + injection.path);
                        keys.Add("DefInjected|" + package.defType.Name + "|" + injection.normalizedPath);
                        yield return 0;
                    }
                }
            }
            else
            {
                List<List<string>> folders = new List<List<string>>();
                foreach (ModContentPack pack in LoadedModManager.RunningMods.Where(pack => pack != null))
                {
                    token.ThrowIfCancellationRequested();
                    folders.Add(ModTextScanner.ContentFolders(pack));
                    yield return 0;
                }
                Task<HashSet<string>> reading = Task.Run(() =>
                {
                    HashSet<string> native = new HashSet<string>();
                    foreach (List<string> paths in folders) native.UnionWith(BundledTranslations.NativeKeys(paths, target, token));
                    return native;
                }, token);
                while (!reading.IsCompleted) { token.ThrowIfCancellationRequested(); yield return -1; }
                keys.UnionWith(reading.GetAwaiter().GetResult());
            }
            lock (gate)
            {
                languages[language] = keys; snapshots[language] = active;
                foreach (var pair in hierarchy) defTypes[pair.Key] = pair.Value;
            }
        }

        public static bool Contains(TranslationEntry entry)
        {
            lock (gate)
            {
                if (!languages.TryGetValue(Glossary.LanguageKey(entry.TargetLanguage), out HashSet<string> keys)) return false;
                if (entry.DefTypeName == "Keyed") return keys.Contains("Keyed|" + entry.DefName);
                string[] names = defTypes.TryGetValue(entry.DefTypeName ?? "", out string[] hierarchy) ? hierarchy : new[] { entry.DefTypeName };
                return names.Any(name => keys.Contains("DefInjected|" + name + "|" + entry.DefName + "." + entry.FieldName));
            }
        }

        public static string ActiveKeyed(string key)
        {
            LoadedLanguage active = LanguageDatabase.activeLanguage;
            if (active != null && Glossary.LanguageKey(active.folderName) != "en" && active.keyedReplacements.TryGetValue(key, out var replacement) &&
                TextProtection.HasVisibleText(replacement.value)) return replacement.value;
            return null;
        }
    }
}
