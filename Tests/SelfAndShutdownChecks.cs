using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace RuntimeTranslator.Tests
{
    public static class SelfAndShutdownChecks
    {
        public static string ProjectRoot;
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
        private static int passed;
        private static void Check(bool valid, string name)
        {
            if (!valid) throw new Exception("FAILED: " + name);
            passed++;
            Console.WriteLine("PASS: " + name);
        }
        public static int Run(string folder)
        {
            passed += BundledEngineChecks.Run(ProjectRoot, folder);
            Verse.GenFilePaths.ConfigFolderPath = folder;
            Verse.ModContentPack pack = new Verse.ModContentPack
            {
                PackageIdPlayerFacing = RuntimeTranslatorSettings.PackageId, Name = XDocument.Load(Path.Combine(ProjectRoot, "About", "About.xml")).Root.Element("name").Value, RootDir = ProjectRoot,
                AllDefs = new List<Verse.Def>()
            };
            Check(XDocument.Load(Path.Combine(ProjectRoot, "About", "About.xml")).Root.Element("packageId").Value == RuntimeTranslatorSettings.PackageId &&
                RuntimeTranslatorSettings.PackageId == "gesvd.rimlingo", "mod metadata uses the RimLingo package ID");
            UiText.Initialize(pack);
            Verse.LoadedModManager.RunningMods.Clear(); Verse.LoadedModManager.RunningMods.Add(pack);
            RuntimeTranslatorSettings settings = new RuntimeTranslatorSettings
            {
                SelectedPackageIds = new List<string> { pack.PackageIdPlayerFacing }, ApplyRuntimeTranslations = false, ExportLanguageFiles = false
            };
            XElement english = XDocument.Load(Path.Combine(ProjectRoot, "Languages", "English", "Keyed", "RuntimeTranslator.xml")).Root;
            XElement chinese = XDocument.Load(Path.Combine(ProjectRoot, "Languages", "ChineseSimplified", "Keyed", "RuntimeTranslator.xml")).Root;
            Check(english.Elements().Select(element => element.Name).SequenceEqual(chinese.Elements().Select(element => element.Name)) &&
                chinese.Elements().All(element => TextProtection.HasVisibleText(element.Value)), "official Chinese table covers every English UI key with visible text");
            Check(english.Elements().All(element => element.Name.LocalName == UiText.Key(element.Value)), "English UI keys remain stable source hashes");
            TranslationEntry brand = UiText.Scan(pack, settings).Single(entry => entry.SourceText == "Gesvd’s RimLingo");
            brand.TranslatedText = "错误机器品牌名";
            UiText.Apply(brand);
            Verse.LanguageDatabase.activeLanguage = new Verse.LoadedLanguage { folderName = "ChineseSimplified" };
            foreach (XElement element in chinese.Elements())
                Verse.LanguageDatabase.activeLanguage.keyedReplacements[element.Name.LocalName] = new Verse.LoadedLanguage.KeyedReplacement { value = element.Value };
            NativeTranslations.Reset();
            Check(UiText.Get("Gesvd’s RimLingo") == "Gesvd的边缘翻译", "official Chinese brand overrides an old machine translation");
            Check(UiText.Get("Task") == "任务" && Enum.GetNames(typeof(TranslationState)).All(state => UiText.Get(state) != state),
                "Chinese UI translates task labels and every task state");
            Check(!RuntimeTextApplier.Apply(brand) && UiText.Get("Gesvd’s RimLingo") == "Gesvd的边缘翻译",
                "stale cached brand cannot overwrite the official Chinese translation");
            Check(ModTextScanner.Scan(settings).Count == 0, "own bundled Chinese UI is skipped by default");
            settings.SkipModsWithBundledChinese = false;
            Check(ModTextScanner.Scan(settings).Count == 0, "disabling whole-mod skipping still protects every official Chinese UI entry");
            Verse.LanguageDatabase.activeLanguage = null;
            NativeTranslations.Reset();
            UiText.Initialize(pack);
            Check(UiText.Get("Gesvd’s RimLingo") == "Gesvd’s RimLingo", "English fallback preserves the English brand");
            settings.TargetLanguage = "Japanese";
            List<TranslationEntry> self = ModTextScanner.Scan(settings);
            Check(self.Count >= 80 && self.All(entry => entry.DefTypeName == "Keyed"), "mod's own settings text is scanned without any Defs");
            TranslationEntry task = self.Single(entry => entry.SourceText == "Task");
            string sourceHash = task.SourceHash;
            task.TranslatedText = "翻訳タスク";
            Check(RuntimeTextApplier.Apply(task) && UiText.Get("Task") == "翻訳タスク", "own UI accepts runtime translations");
            Check(ModTextScanner.Scan(settings).Single(entry => entry.SourceText == "Task").SourceHash == sourceHash,
                "translated UI still scans its stable English source");
            LanguageFileExporter.Export(new[] { TranslationCacheRecord.FromEntry(task, "Test") }, "Japanese");
            string file = Path.Combine(folder, "RuntimeTranslator", "GeneratedLanguages", "Japanese", pack.PackageIdPlayerFacing, "Keyed", "RuntimeTranslator.xml");
            XElement exported = XDocument.Load(file).Root.Elements().Single();
            Check(exported.Name.LocalName == UiText.Key("Task") && exported.Value == "翻訳タスク", "own UI exports standard Keyed XML with correct keys");
            task.TranslatedText = ""; RuntimeTextApplier.Apply(task);
            Check(UiText.Get("Task") == "Task", "clearing an own-UI translation restores the normal language fallback");
            Check(XDocument.Load(Path.Combine(ProjectRoot, "About", "About.xml")).Root.Element("author").Value == "Gesvd & Codex",
                "author metadata credits Gesvd and Codex");

            TranslationManager.Initialize(settings);
            TranslationCacheStore cache = (TranslationCacheStore)typeof(TranslationManager).GetField("cache", Hidden).GetValue(null);
            cache.Put(new TranslationEntry { Id = "saved", TranslatedText = "saved translation" }, "Test");
            cache.Save();
            string cacheFile = Path.Combine(folder, "RuntimeTranslator", "cache.xml");
            byte[] saved = File.ReadAllBytes(cacheFile);
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(TranslationLifetime.Token))
            {
                WaitingProvider mainProvider = new WaitingProvider();
                Task main = (Task)typeof(TranslationManager).GetMethod("RunWorker", Hidden).Invoke(null,
                    new object[] { new List<TranslationEntry> { new TranslationEntry { Id = "active", SourceText = "active" } }, mainProvider, settings, linked.Token, false });
                Check(mainProvider.Entered.Wait(5000), "main translation is waiting for an HTTP-like request before shutdown");
                RuntimeTranslatorSettings glossary = new RuntimeTranslatorSettings();
                glossary.GlossaryTerms.Add(new GlossaryTerm { Source = "active term" });
                WaitingProvider termProvider = new WaitingProvider();
                Task<TranslationStatus> termJob = GlossaryTranslator.TranslateAsync(glossary, termProvider, () => 1, batch => { }, linked.Token);
                Check(termProvider.Entered.Wait(5000), "glossary translation is also active before shutdown");
                bool callbackRan = false;
                TranslationManager.OnMainThread(() => callbackRan = true);
                object cacheLock = typeof(TranslationManager).GetField("cacheLock", Hidden).GetValue(null);
                using (ManualResetEventSlim holding = new ManualResetEventSlim())
                using (ManualResetEventSlim release = new ManualResetEventSlim())
                {
                    Task holder = Task.Run(() => { lock (cacheLock) { holding.Set(); release.Wait(); } });
                    Check(holding.Wait(5000), "test simulates busy cache I/O");
                    try
                    {
                        TranslationUpdate updater = new TranslationUpdate();
                        typeof(TranslationUpdate).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(updater, null);
                        Stopwatch timer = Stopwatch.StartNew();
                        bool allowed = UnityEngine.Application.Quit();
                        TranslationManager.SaveCache();
                        timer.Stop();
                        Check(allowed && timer.ElapsedMilliseconds < 1000, "quit handler does not wait for workers or the busy cache lock");
                        Check(main.Wait(5000) && TranslationManager.Status.State == TranslationState.Cancelled,
                            "quit cancels the main request");
                        Check(termJob.Wait(5000) && termJob.Result.State == TranslationState.Cancelled, "quit cancels the glossary request");
                        TranslationManager.OnMainThread(() => callbackRan = true);
                        TranslationManager.Tick();
                        Check(!callbackRan, "shutdown drops queued and late UI actions");
                        Check(saved.SequenceEqual(File.ReadAllBytes(cacheFile)), "shutdown preserves the completed cache on disk");
                        typeof(TranslationUpdate).GetMethod("OnApplicationQuit", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(updater, null);
                        typeof(TranslationUpdate).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(updater, null);
                        Check(TranslationLifetime.IsStopping, "repeated Unity shutdown callbacks are safe");
                    }
                    finally { release.Set(); holder.Wait(5000); }
                }
                mainProvider.Entered.Dispose(); termProvider.Entered.Dispose();
            }
            string defs = Path.Combine(folder, "scan", "Defs"); Directory.CreateDirectory(defs);
            File.WriteAllText(Path.Combine(defs, "Test.xml"), "<Defs><ThingDef><label>test word</label></ThingDef></Defs>");
            bool stopped = false;
            try { Glossary.ScanFiles(new[] { Path.Combine(folder, "scan") }, TranslationLifetime.Token); }
            catch (OperationCanceledException) { stopped = true; }
            Check(stopped, "quit cancellation interrupts glossary file scans");
            stopped = false;
            try { LanguageFileExporter.Export(new TranslationCacheRecord[0], "ChineseSimplified", TranslationLifetime.Token); }
            catch (OperationCanceledException) { stopped = true; }
            Check(stopped, "quit cancellation interrupts automatic export");
            return passed;
        }
    }
}