using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace RuntimeTranslator.Tests
{
    public static class PerformanceChecks
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
        private static int passed;
        private static void Check(bool valid, string name)
        {
            if (!valid) throw new Exception("FAILED: " + name);
            passed++;
            Console.WriteLine("PASS: " + name);
        }

        public static void FinishScan()
        {
            Task worker = (Task)typeof(TranslationManager).GetField("worker", Hidden).GetValue(null);
            Stopwatch timer = Stopwatch.StartNew();
            while (worker != null && !worker.IsCompleted && timer.ElapsedMilliseconds < 10000)
            {
                TranslationManager.Tick();
                Thread.Sleep(1);
            }
            if (worker != null && !worker.IsCompleted) throw new Exception("Scan did not finish in time");
            worker?.GetAwaiter().GetResult();
        }

        public static int Run(string folder)
        {
            var previous = LoadedModManager.RunningMods.ToList();
            GenFilePaths.ConfigFolderPath = folder;
            RuntimeTranslatorSettings settings = new RuntimeTranslatorSettings
            {
                SelectedPackageIds = new List<string> { "performance.mod" }, ApplyRuntimeTranslations = false, ExportLanguageFiles = false
            };
            ModContentPack pack = new ModContentPack
            {
                PackageIdPlayerFacing = "performance.mod", Name = "Performance", AllDefs = Enumerable.Range(0, 1200)
                    .Select(i => new Def { defName = "D" + i, label = "source " + i }).ToList()
            };
            try
            {
                LoadedModManager.RunningMods.Clear(); LoadedModManager.RunningMods.Add(pack);
                TranslationManager.Initialize(settings);
                TranslationCacheStore cache = (TranslationCacheStore)typeof(TranslationManager).GetField("cache", Hidden).GetValue(null);
                foreach (TranslationEntry entry in ModTextScanner.Scan(settings)) { entry.TranslatedText = "cached"; cache.Put(entry, "Test"); }
                NativeTranslations.Reset();
                TranslationManager.Start();
                Check(TranslationManager.Status.State == TranslationState.Scanning, "starting a large task returns before scanning game objects");
                TranslationManager.Tick();
                Check(TranslationManager.Status.State == TranslationState.Scanning, "large mod scans yield across frames");
                TranslationManager.Cancel(); FinishScan();
                Check(TranslationManager.Status.State == TranslationState.Cancelled, "scanning can be cancelled without making a provider request");
                TranslationManager.Start(); FinishScan();
                Check(TranslationManager.Status.State == TranslationState.Completed && TranslationManager.Status.Skipped == 1200,
                    "incremental scanning restarts and reuses every valid cached translation");

                TranslationEntry indexed = ModTextScanner.Scan(settings).First();
                Def original = pack.AllDefs.First(def => def.defName == indexed.DefName);
                indexed.TranslatedText = "indexed translation";
                pack.AllDefs.Clear();
                Check(RuntimeTextApplier.Apply(indexed) && original.label == "indexed translation", "application uses its captured Def index without enumerating all definitions again");

                TranslationManager.SaveCache();
                string path = Path.Combine(folder, "RuntimeTranslator", "cache.xml");
                cache.Put(new TranslationEntry { Id = "new", SourceText = "new", TranslatedText = "new" }, "Test");
                long capturedRevision = cache.Revision;
                List<TranslationCacheRecord> snapshot = TranslationManager.GetCachedTranslationsSnapshot();
                cache.TryUpdate("new", null, "Test", "edited while saving", out _);
                Check(cache.SaveSnapshot(snapshot), "cache writes an independent snapshot");
                cache.MarkSaved(capturedRevision);
                Check(cache.IsDirty && snapshot.Single(record => record.Id == "new").TranslatedText == "new", "edits made during a save remain dirty and cannot change the serialized snapshot");
                TranslationManager.SaveCache();
                TranslationCacheStore loaded = new TranslationCacheStore(); loaded.Load();
                Check(!cache.IsDirty && loaded.Records.Single(record => record.Id == "new").TranslatedText == "edited while saving",
                    "the next save persists concurrent edits");
                File.Delete(path + ".bak");
                TranslationManager.SaveCache();
                Check(!File.Exists(path + ".bak"), "saving an unchanged cache performs no file replacement");
                long revision = cache.Revision;
                cache.TryUpdate("new", null, "Test", "edited while saving", out _);
                Check(cache.Revision == revision, "unchanged edits do not invalidate preview snapshots or dirty the cache");
            }
            finally { LoadedModManager.RunningMods.Clear(); LoadedModManager.RunningMods.AddRange(previous); }
            return passed;
        }
    }
}
