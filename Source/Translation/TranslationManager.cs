using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimWorld;
using Verse;

namespace RuntimeTranslator
{
    public static class TranslationManager
    {
        private static RuntimeTranslatorSettings settings;
        private static TranslationCacheStore cache;
        private static CancellationTokenSource cancellation;
        private static ManualResetEventSlim pauseGate = new ManualResetEventSlim(true);
        private static Task worker;
        private static readonly object statusLock = new object();
        private static readonly object pendingLock = new object();
        private static readonly object cacheLock = new object();
        private static readonly Queue<TranslationEntry> pendingApply = new Queue<TranslationEntry>();
        private static readonly Queue<Action> pendingMainThreadActions = new Queue<Action>();
        private static readonly TranslationStatus status = new TranslationStatus();

        public static TranslationStatus Status
        {
            get
            {
                lock (statusLock)
                {
                    return new TranslationStatus
                    {
                        State = status.State,
                        Total = status.Total,
                        Completed = status.Completed,
                        Failed = status.Failed,
                        Skipped = status.Skipped,
                        CurrentItem = status.CurrentItem,
                        LastError = status.LastError
                    };
                }
            }
        }

        public static void Initialize(RuntimeTranslatorSettings modSettings)
        {
            settings = modSettings;
            NativeTranslations.Reset();
            cache = new TranslationCacheStore();
            cache.Load();
        }

        public static void Start(bool applyGlossary = false)
        {
            if (settings == null || TranslationLifetime.IsStopping)
            {
                return;
            }

            if (worker != null && !worker.IsCompleted)
            {
                Messages.Message(UiText.Get("Gesvd’s RimLingo is already running."), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (!LanguageCatalog.IsSupported(settings.SourceLanguage, true) || !LanguageCatalog.IsSupported(settings.TargetLanguage, false))
            {
                Messages.Message(UiText.Get("Choose source and target languages from the dropdown menus."), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (!applyGlossary && (settings.SelectedPackageIds == null || settings.SelectedPackageIds.Count == 0))
            {
                Messages.Message(UiText.Get("Select at least one mod before starting translation."), MessageTypeDefOf.RejectInput, false);
                return;
            }

            cancellation?.Dispose();
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(TranslationLifetime.Token);
            pauseGate.Set();
            SetStatus(TranslationState.Scanning, 0, 0, 0, "Scanning selected mods", string.Empty);

            try
            {
                RuntimeTranslatorSettings taskSettings = settings.Snapshot();
                List<TranslationEntry> scanned = applyGlossary ? GlossaryRefreshEntries(taskSettings) : ModTextScanner.Scan(taskSettings);
                if (applyGlossary)
                {
                    taskSettings.SkipCompletedTranslations = false;
                    if (scanned.Count == 0)
                    {
                        SetStatus(TranslationState.Completed, 0, 0, 0, "Done", string.Empty);
                        Messages.Message(UiText.Get("No cached translations match the active glossary and language pair."), MessageTypeDefOf.RejectInput, false);
                        return;
                    }
                }
                SetStatus(TranslationState.Running, scanned.Count, 0, 0, "Preparing queue", string.Empty);
                ITranslationProvider provider = ProviderFactory.Create(taskSettings);
                CancellationToken token = cancellation.Token;
                // Always enter RunWorker so an early cancellation still updates the final status.
                worker = Task.Run(() => RunWorker(scanned, provider, taskSettings, token, applyGlossary));
                Messages.Message(UiText.Get("Gesvd’s RimLingo started. Preparing translation queue."), MessageTypeDefOf.TaskCompletion, false);
            }
            catch (Exception ex)
            {
                SetStatus(TranslationState.Failed, 0, 0, 0, string.Empty, ex.Message);
                Log.Error("[RuntimeTranslator] Failed to start: " + ex);
            }
        }

        internal static List<TranslationEntry> GlossaryRefreshEntries(RuntimeTranslatorSettings options)
        {
            NativeTranslations.Prepare(options.TargetLanguage);
            List<GlossaryTerm> terms = Glossary.ForLanguages(options, options.SourceLanguage, options.TargetLanguage);
            List<TranslationEntry> entries = new List<TranslationEntry>();
            RuntimeTranslatorSettings scan = options.Snapshot();
            scan.SelectedPackageIds = LoadedModManager.RunningMods.Where(ModTextScanner.IsPlayerMod).Select(pack => pack.PackageIdPlayerFacing).ToList();
            scan.SkipModsWithBundledChinese = false;
            HashSet<string> currentSources = new HashSet<string>(ModTextScanner.Scan(scan).Select(entry =>
                string.Join("|", entry.PackageId, entry.DefTypeName, entry.DefName, entry.FieldName, entry.SourceText)));
            foreach (TranslationCacheRecord record in GetCachedTranslationsSnapshot()
                .Where(record => Glossary.LanguageKey(record.SourceLanguage) == Glossary.LanguageKey(options.SourceLanguage) &&
                    Glossary.LanguageKey(record.TargetLanguage) == Glossary.LanguageKey(options.TargetLanguage))
                .GroupBy(record => record.Id).Select(group => group.OrderByDescending(record => record.UpdatedUtcTicks).First()))
            {
                if (!TextProtection.HasVisibleText(record.TranslatedText) || string.IsNullOrEmpty(record.SourceText) ||
                    !currentSources.Contains(string.Join("|", record.PackageId, record.DefTypeName, record.DefName, record.FieldName, record.SourceText))) continue;
                string hash = Glossary.SourceHash(record.SourceText, terms);
                if (hash == TextHasher.Sha1(record.SourceText)) continue;
                TranslationEntry entry = record.ToEntry();
                entry.SourceHash = hash;
                entry.TranslatedText = null;
                if (!NativeTranslations.Contains(entry)) entries.Add(entry);
            }
            return entries;
        }

        public static void Pause()
        {
            lock (statusLock)
            {
                if (worker == null || worker.IsCompleted || status.State != TranslationState.Running) return;
                pauseGate.Reset();
                status.State = TranslationState.Paused;
            }
        }

        public static void Resume()
        {
            lock (statusLock)
            {
                if (worker == null || worker.IsCompleted || status.State != TranslationState.Paused) return;
                pauseGate.Set();
                status.State = TranslationState.Running;
            }
        }

        public static void Cancel()
        {
            lock (statusLock)
            {
                if (cancellation == null || worker == null || worker.IsCompleted ||
                    (status.State != TranslationState.Running && status.State != TranslationState.Paused)) return;
                status.State = TranslationState.Cancelling;
            }
            cancellation.Cancel();
            pauseGate.Set();
        }

        public static void Tick()
        {
            if (TranslationLifetime.IsStopping) return;
            List<Action> notifications = new List<Action>();
            lock (pendingLock)
            {
                while (pendingMainThreadActions.Count > 0 && notifications.Count < 20)
                {
                    notifications.Add(pendingMainThreadActions.Dequeue());
                }
            }
            foreach (Action notification in notifications)
            {
                if (!TranslationLifetime.IsStopping) notification();
            }

            List<TranslationEntry> toApply = new List<TranslationEntry>();
            lock (pendingLock)
            {
                while (pendingApply.Count > 0 && toApply.Count < 20)
                {
                    toApply.Add(pendingApply.Dequeue());
                }
            }

            RuntimeTextApplier.ApplyMany(toApply, 20);
        }

        public static void SaveCache()
        {
            if (TranslationLifetime.IsStopping) return;
            if (cache != null)
            {
                lock (cacheLock)
                {
                    if (!cache.Save())
                    {
                        UpdateError("Could not save translation cache. The previous file was preserved.");
                        Notify("Gesvd’s RimLingo could not save its cache. See the log for details.", false);
                    }
                }
            }
        }

        public static void ExportCurrentCache()
        {
            if (cache == null || settings == null)
            {
                return;
            }

            if (!TranslationLifetime.IsStopping)
            {
                NativeTranslations.Prepare(settings.TargetLanguage);
                LanguageFileExporter.Export(GetCachedTranslationsSnapshot(), settings.TargetLanguage);
            }
        }

        public static List<TranslationCacheRecord> GetCachedTranslationsSnapshot()
        {
            if (cache == null)
            {
                return new List<TranslationCacheRecord>();
            }

            lock (cacheLock)
            {
                return cache.Records
                    .Select(CloneRecord)
                    .ToList();
            }
        }

        public static void UpdateCachedTranslation(TranslationCacheRecord record, string translatedText)
        {
            if (cache == null || record == null)
            {
                return;
            }

            TranslationCacheRecord updated = null;
            lock (cacheLock)
            {
                if (cache.TryUpdate(record.Id, record.SourceHash, record.Provider, translatedText, out updated))
                {
                    if (settings.ApplyRuntimeTranslations)
                    {
                        QueueApply(updated.ToEntry());
                    }
                }
            }
        }

        public static int ReplaceCachedTranslationsBatch(IEnumerable<string> visibleRecordKeys, string oldText, string newText, int batchSize, ISet<string> processedKeys)
        {
            if (cache == null || string.IsNullOrEmpty(oldText) || oldText == (newText ?? string.Empty))
            {
                return 0;
            }

            HashSet<string> keys = new HashSet<string>(visibleRecordKeys ?? Enumerable.Empty<string>());
            List<TranslationCacheRecord> changed = new List<TranslationCacheRecord>();
            int count = 0;

            lock (cacheLock)
            {
                foreach (TranslationCacheRecord record in cache.Records
                    .Where(record => keys.Contains(CacheRecordKey(record)) && !processedKeys.Contains(CacheRecordKey(record)) &&
                        (record.TranslatedText ?? string.Empty).Contains(oldText)))
                {
                    if (count >= Math.Max(1, batchSize))
                    {
                        break;
                    }

                    record.TranslatedText = record.TranslatedText.Replace(oldText, newText ?? string.Empty);
                    record.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
                    processedKeys.Add(CacheRecordKey(record));
                    changed.Add(CloneRecord(record));
                    count++;
                }

                if (count > 0)
                {
                    SaveCache();
                }
            }

            foreach (TranslationCacheRecord record in changed)
            {
                if (settings.ApplyRuntimeTranslations)
                {
                    QueueApply(record.ToEntry());
                }
            }

            return count;
        }

        public static string CacheRecordKey(TranslationCacheRecord record)
        {
            return (record.Id ?? string.Empty) + "|" + (record.SourceHash ?? string.Empty) + "|" + (record.Provider ?? string.Empty);
        }

        private static async Task RunWorker(List<TranslationEntry> scanned, ITranslationProvider provider, RuntimeTranslatorSettings taskSettings, CancellationToken token, bool applyGlossary = false)
        {
            int completed = 0;
            int failed = 0;
            int skipped = 0;
            int total = scanned.Count;
            try
            {
                string providerName = provider.DisplayName;
                Dictionary<string, TranslationCacheRecord> completedCache = new Dictionary<string, TranslationCacheRecord>();
                if (taskSettings.SkipCompletedTranslations)
                {
                    lock (cacheLock)
                    {
                        completedCache = cache.Records.GroupBy(record => record.Id + "|" + record.SourceHash)
                            .Select(group => group.OrderByDescending(record => record.Provider == providerName)
                                .ThenByDescending(record => record.UpdatedUtcTicks).First())
                            .Where(record => TextProtection.HasVisibleText(record.TranslatedText))
                            .ToDictionary(record => record.Id + "|" + record.SourceHash, CloneRecord);
                    }
                }
                List<TranslationEntry> pending = new List<TranslationEntry>();
                foreach (TranslationEntry entry in scanned)
                {
                    token.ThrowIfCancellationRequested();
                    if (NativeTranslations.Contains(entry)) { skipped++; continue; }
                    TranslationCacheRecord cached;
                    completedCache.TryGetValue(entry.Id + "|" + entry.SourceHash, out cached);
                    if (cached != null && TextProtection.HasVisibleText(cached.TranslatedText))
                    {
                        skipped++;
                        if (taskSettings.ApplyRuntimeTranslations) QueueApply(cached.ToEntry());
                    }
                    else
                    {
                        pending.Add(entry);
                    }
                }

                total = pending.Count;
                UpdateProgress(total, completed, failed, "Skipped completed: " + skipped, skipped);
                for (int offset = 0; offset < pending.Count;)
                {
                    token.ThrowIfCancellationRequested();
                    pauseGate.Wait(token);
                    int count = Math.Min(CurrentBatchSize, pending.Count - offset);
                    List<TranslationEntry> batch = pending.GetRange(offset, count);
                    offset += count;
                    UpdateProgress(total, completed, failed, batch.First().DefName, skipped);
                    try
                    {
                        TranslationBatchRequest request = new TranslationBatchRequest
                        {
                            Entries = batch,
                            SourceLanguage = taskSettings.SourceLanguage,
                            TargetLanguage = taskSettings.TargetLanguage
                        };
                        TranslationBatchResult result = await provider.TranslateBatchAsync(request, token).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        foreach (TranslationEntry entry in batch)
                        {
                            if (result.Translations.TryGetValue(entry.Id, out string translated) && TextProtection.HasVisibleText(translated))
                            {
                                entry.TranslatedText = translated;
                                lock (cacheLock)
                                {
                                    cache.Put(entry, providerName, applyGlossary);
                                }
                                if (taskSettings.ApplyRuntimeTranslations) QueueApply(entry);
                                completed++;
                            }
                            else
                            {
                                failed++;
                            }
                        }
                        SaveCache();
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        failed += batch.Count;
                        UpdateError(ex.Message);
                        Log.Warning("[RuntimeTranslator] Batch failed: " + ex.Message);
                    }
                }

                token.ThrowIfCancellationRequested();
                if (taskSettings.ExportLanguageFiles)
                {
                    LanguageFileExporter.Export(GetCachedTranslationsSnapshot(), taskSettings.TargetLanguage, token);
                }
                token.ThrowIfCancellationRequested();
                SetStatus(TranslationState.Completed, total, completed, failed, "Done", Status.LastError, skipped);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                SetStatus(TranslationState.Cancelled, total, completed, failed, "Cancelled", Status.LastError, skipped);
            }
            catch (Exception ex)
            {
                SetStatus(TranslationState.Failed, total, completed, failed, string.Empty, ex.Message, skipped);
                Log.Error("[RuntimeTranslator] Translation task failed: " + ex);
            }
        }

        internal static void Notify(string message, bool success, string detail = "")
        {
            OnMainThread(() => Messages.Message(UiText.Get(message) + detail,
                success ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.RejectInput, false));
        }

        internal static void OnMainThread(Action action)
        {
            if (TranslationLifetime.IsStopping) return;
            lock (pendingLock)
            {
                if (!TranslationLifetime.IsStopping) pendingMainThreadActions.Enqueue(action);
            }
        }

        public static void Shutdown()
        {
            TranslationLifetime.Stop();
            BundledLibreTranslate.Stop();
            pauseGate.Set();
            lock (pendingLock)
            {
                pendingApply.Clear();
                pendingMainThreadActions.Clear();
            }
            // Previously saved batches survive; never join workers or wait for cache I/O here.
        }

        internal static int CurrentBatchSize
        {
            get { return Math.Max(1, Volatile.Read(ref settings.BatchSize)); }
        }

        private static void QueueApply(TranslationEntry entry)
        {
            lock (pendingLock)
            {
                if (!TranslationLifetime.IsStopping) pendingApply.Enqueue(entry);
            }
        }

        private static void SetStatus(TranslationState state, int total, int completed, int failed, string current, string error, int skipped = 0)
        {
            lock (statusLock)
            {
                status.State = state;
                status.Total = total;
                status.Completed = completed;
                status.Failed = failed;
                status.Skipped = skipped;
                status.CurrentItem = current ?? string.Empty;
                status.LastError = error ?? string.Empty;
            }
        }

        private static void UpdateProgress(int total, int completed, int failed, string current, int skipped = 0)
        {
            lock (statusLock)
            {
                status.Total = total;
                status.Completed = completed;
                status.Failed = failed;
                status.Skipped = skipped;
                status.CurrentItem = current ?? string.Empty;
            }
        }

        private static void UpdateError(string error)
        {
            lock (statusLock)
            {
                status.LastError = error ?? string.Empty;
            }
        }

        private static TranslationCacheRecord CloneRecord(TranslationCacheRecord record)
        {
            return new TranslationCacheRecord
            {
                Id = record.Id,
                PackageId = record.PackageId,
                ModName = record.ModName,
                DefTypeName = record.DefTypeName,
                DefName = record.DefName,
                FieldName = record.FieldName,
                SourceLanguage = record.SourceLanguage,
                TargetLanguage = record.TargetLanguage,
                SourceHash = record.SourceHash,
                SourceText = record.SourceText,
                TranslatedText = record.TranslatedText,
                Provider = record.Provider,
                UpdatedUtcTicks = record.UpdatedUtcTicks
            };
        }
    }
}
