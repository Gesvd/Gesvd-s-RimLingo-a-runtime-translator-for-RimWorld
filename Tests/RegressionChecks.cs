using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

// Stand-ins keep these checks independent of Unity, game saves, and paid APIs.
namespace UnityEngine
{
    public class MonoBehaviour { }
    public static class Application { public static event Func<bool> wantsToQuit; public static bool Quit() { return wantsToQuit == null || wantsToQuit(); } }
}
namespace Verse
{
    public class Def { public string defName; public string label; }
    public class ModContentPack
    {
        public string RootDir;
        public List<string> foldersToLoadDescendingOrder;
        public string PackageIdPlayerFacing;
        public string Name;
        public List<Def> AllDefs { get; set; }
    }
    public static class LoadedModManager { public static List<ModContentPack> RunningMods = new List<ModContentPack>(); }
    public static class GenFilePaths { public static string ConfigFolderPath; }
    public static class Log { public static void Warning(string text) { } public static void Error(string text) { } }
    public static class Messages
    {
        public static int Count;
        public static void Message(string text, object type, bool historical) { Count++; }
    }
    public class ModSettings { public virtual void ExposeData() { } }
    public static class Translator { public static string Translate(this string key) { return key; } }
    public interface IExposable { void ExposeData(); }
    public enum LookMode { Value, Deep }
    public static class Scribe_Values { public static void Look<T>(ref T value, string key, T fallback) { } }
    public static class Scribe_Collections { public static void Look<T>(ref List<T> value, string key, LookMode mode) { } }
}
namespace RimWorld
{
    public static class MessageTypeDefOf
    {
        public static readonly object RejectInput = new object();
        public static readonly object TaskCompletion = new object();
    }
}
namespace RuntimeTranslator.Tests
{
    public sealed class RecordingProvider : ITranslationProvider
    {
        public string DisplayName { get { return "Test"; } }
        public string Target;
        public int Requests;
        public Task<TranslationBatchResult> TranslateBatchAsync(TranslationBatchRequest request, CancellationToken token)
        {
            Requests++;
            Target = request.TargetLanguage;
            TranslationBatchResult result = new TranslationBatchResult();
            foreach (TranslationEntry entry in request.Entries) result.Translations[entry.Id] = "translated label";
            return Task.FromResult(result);
        }
    }

    public static class RegressionChecks
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
        private static int passed;

        private static void Check(bool valid, string name)
        {
            if (!valid) throw new Exception("FAILED: " + name);
            passed++;
            Console.WriteLine("PASS: " + name);
        }

        private static void Reject(Action action, string name)
        {
            try { action(); }
            catch (InvalidOperationException) { Check(true, name); return; }
            throw new Exception("FAILED: " + name);
        }

        private static void Worker(List<TranslationEntry> entries, RecordingProvider provider, RuntimeTranslatorSettings snapshot)
        {
            Task task = (Task)typeof(TranslationManager).GetMethod("RunWorker", Hidden)
                .Invoke(null, new object[] { entries, provider, snapshot, CancellationToken.None });
            task.GetAwaiter().GetResult();
        }

        private static void Frame()
        {
            typeof(TranslationUpdate).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(new TranslationUpdate(), null);
        }

        public static void Run(string folder)
        {
            Verse.GenFilePaths.ConfigFolderPath = folder;
            RuntimeTranslatorSettings settings = new RuntimeTranslatorSettings
            {
                ExportLanguageFiles = false,
                SelectedPackageIds = new List<string> { "test.mod" }
            };
            Verse.Def def = new Verse.Def { defName = "Sword", label = "steel sword" };
            Verse.LoadedModManager.RunningMods.Add(new Verse.ModContentPack
            {
                PackageIdPlayerFacing = "test.mod", Name = "Test", AllDefs = new List<Verse.Def> { def }
            });
            TranslationManager.Initialize(settings);
            List<TranslationEntry> entries = ModTextScanner.Scan(settings);
            string originalHash = entries.Single().SourceHash;
            RecordingProvider provider = new RecordingProvider();
            Worker(entries, provider, settings.Snapshot());
            Check(def.label == "steel sword", "worker does not write game objects");
            Frame();
            Check(def.label == "translated label", "Update applies translations without a settings window");
            TranslationEntry rescan = ModTextScanner.Scan(settings).Single();
            Check(rescan.SourceText == "steel sword" && rescan.SourceHash == originalHash, "rescan preserves original text and hash");
            Worker(new List<TranslationEntry> { rescan }, provider, settings.Snapshot());
            Check(provider.Requests == 1, "restart reuses the original cache entry");
            Frame();
            TranslationManager.UpdateCachedTranslation(TranslationManager.GetCachedTranslationsSnapshot().Single(), "");
            Frame();
            Check(def.label == "steel sword", "clearing a translation restores original text");
            Check(TranslationManager.GetCachedTranslationsSnapshot().Count == 1, "cleared entry stays visible");
            Worker(ModTextScanner.Scan(settings), provider, settings.Snapshot());
            Check(provider.Requests == 2, "cleared translation is translated again");
            Frame();

            RuntimeTranslatorSettings snapshot = settings.Snapshot();
            ITranslationProvider configured = ProviderFactory.Create(snapshot);
            string originalUrl = snapshot.OpenAiBaseUrl;
            settings.TargetLanguage = "Japanese";
            settings.OpenAiBaseUrl = "http://changed.invalid";
            settings.OpenAiModel = "changed";
            settings.OpenAiApiKey = "dummy-changed-key";
            settings.ApplyRuntimeTranslations = false;
            settings.SelectedPackageIds.Clear();
            RuntimeTranslatorSettings providerSettings = (RuntimeTranslatorSettings)configured.GetType()
                .GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(configured);
            Check(providerSettings.OpenAiBaseUrl == originalUrl && providerSettings.OpenAiModel != "changed" &&
                providerSettings.OpenAiApiKey != "dummy-changed-key", "provider retains original URL, model and key");
            Check(snapshot.TargetLanguage == "ChineseSimplified" && snapshot.SelectedPackageIds.Count == 1, "task settings and selected mods are independent");
            TranslationCacheRecord record = TranslationManager.GetCachedTranslationsSnapshot().Single();
            TranslationManager.UpdateCachedTranslation(record, "");
            Worker(entries, provider, snapshot);
            Check(provider.Target == "ChineseSimplified", "request target stays aligned with cache target");
            Frame();
            Check(def.label == "translated label", "running task retains its apply setting");
            snapshot.ApplyRuntimeTranslations = false;
            def.label = "unchanged";
            Worker(entries, provider, snapshot);
            Frame();
            Check(def.label == "unchanged", "disabled runtime application does not queue cache hits");

            List<Verse.Def> limitedDefs = new List<Verse.Def>();
            for (int i = 0; i < 25; i++) limitedDefs.Add(new Verse.Def { defName = "Limited" + i, label = "source " + i });
            Verse.LoadedModManager.RunningMods.Single().AllDefs.AddRange(limitedDefs);
            RuntimeTranslatorSettings limitedSettings = snapshot.Snapshot();
            limitedSettings.ApplyRuntimeTranslations = true;
            Worker(ModTextScanner.Scan(limitedSettings).Where(entry => entry.DefName.StartsWith("Limited")).ToList(), provider, limitedSettings);
            Frame();
            Check(limitedDefs.Count(item => item.label == "translated label") == 20, "one frame applies at most 20 entries");
            Frame();
            Check(limitedDefs.All(item => item.label == "translated label"), "next frame drains the remaining entries");

            ProtectedText protection = TextProtection.Protect("Hi {0} [PAWN_nameDef] <b>world</b>\\n __RT_TOKEN_0__");
            Check(protection.Restore(protection.Text) == "Hi {0} [PAWN_nameDef] <b>world</b>\\n __RT_TOKEN_0__", "mixed-case variables, tags and literal marker round-trip");
            Reject(() => protection.Restore(protection.Text.Replace("__RT_TOKEN_0__", "")), "missing placeholder is rejected");
            Reject(() => protection.Restore(protection.Text + "__RT_TOKEN_0__"), "duplicate placeholder is rejected");
            Reject(() => protection.Restore(protection.Text + "__RT_TOKEN_999__"), "unknown placeholder is rejected");

            TranslationCacheRecord old = TranslationCacheRecord.FromEntry(entries.Single(), "Old");
            old.TranslatedText = "old"; old.UpdatedUtcTicks = 1;
            TranslationCacheRecord latest = TranslationCacheRecord.FromEntry(entries.Single(), "New");
            latest.TranslatedText = "latest"; latest.UpdatedUtcTicks = 2;
            TranslationCacheRecord japanese = TranslationCacheRecord.FromEntry(entries.Single(), "Japanese");
            japanese.TargetLanguage = "Japanese"; japanese.TranslatedText = "japanese"; japanese.UpdatedUtcTicks = 3;
            string root = Path.Combine(folder, "RuntimeTranslator", "GeneratedLanguages", "ChineseSimplified");
            string stale = Path.Combine(root, "stale.mod", "DefInjected", "Def", "RuntimeTranslator.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(stale));
            File.WriteAllText(stale, "stale output");
            string custom = Path.Combine(Path.GetDirectoryName(stale), "Custom.xml");
            File.WriteAllText(custom, "custom file");
            int messagesBefore = Verse.Messages.Count;
            LanguageFileExporter.Export(new[] { old, latest, japanese }, "ChineseSimplified");
            Check(Verse.Messages.Count == messagesBefore, "export notifications wait for the main thread");
            Frame();
            Check(Verse.Messages.Count > messagesBefore, "main update delivers export notification");
            string exported = Path.Combine(root, "test.mod", "DefInjected", "Def", "RuntimeTranslator.xml");
            XElement[] values = XDocument.Load(exported).Root.Elements().ToArray();
            Check(values.Length == 1 && values[0].Value == "latest", "export filters language and keeps one latest field");
            Check(!File.Exists(stale) && File.Exists(custom), "stale generated output is removed while custom files remain");
            latest.TranslatedText = "";
            LanguageFileExporter.Export(new[] { old, latest, japanese }, "ChineseSimplified");
            Check(!XDocument.Load(exported).Root.Elements().Any(), "latest cleared translation does not resurrect old export");

            Verse.GenFilePaths.ConfigFolderPath = Path.Combine(folder, "save-checks");
            TranslationCacheStore store = new TranslationCacheStore();
            Check(store.Save(), "first atomic cache save succeeds");
            store.Put(entries.Single(), "Test");
            Check(store.Save(), "atomic replacement succeeds");
            string cacheFile = Path.Combine(Verse.GenFilePaths.ConfigFolderPath, "RuntimeTranslator", "cache.xml");
            byte[] before = File.ReadAllBytes(cacheFile);
            TranslationEntry invalid = new TranslationEntry { Id = "invalid", TranslatedText = "bad\u0001text" };
            store.Put(invalid, "Test");
            Check(!store.Save() && before.SequenceEqual(File.ReadAllBytes(cacheFile)), "serialization failure preserves previous cache byte-for-byte");
            Check(!File.Exists(cacheFile + ".tmp"), "failed temporary cache is cleaned up");
            using (FileStream blocked = File.Open(cacheFile + ".tmp", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                Check(!store.Save() && before.SequenceEqual(File.ReadAllBytes(cacheFile)), "temporary-file write failure preserves the primary cache");
            }
            File.Delete(cacheFile + ".tmp");
            TranslationCacheStore reloaded = new TranslationCacheStore();
            reloaded.Load();
            Check(reloaded.Records.Count == 1 && reloaded.Save(), "valid cache reloads and creates a backup");
            File.WriteAllText(cacheFile, "broken");
            TranslationCacheStore recovered = new TranslationCacheStore();
            recovered.Load();
            Check(recovered.Records.Count == 1, "damaged primary cache recovers from backup");
            File.Delete(cacheFile);
            recovered.Load();
            Check(recovered.Records.Count == 1, "missing primary cache recovers from backup");
            Check(FileNameSanitizer.Sanitize("..") != "..", "generated folder names cannot traverse to a parent");
            passed += TaskRegressionChecks.Run(Path.Combine(folder, "task-checks"));
            passed += GlossaryChecks.Run(Path.Combine(folder, "glossary-checks"));
            passed += WorkflowChecks.Run(Path.Combine(folder, "workflow-checks"));
            passed += SelfAndShutdownChecks.Run(Path.Combine(folder, "self-and-shutdown"));
            Console.WriteLine("Passed " + passed + " checks. Test data: " + folder);
        }
    }
}
