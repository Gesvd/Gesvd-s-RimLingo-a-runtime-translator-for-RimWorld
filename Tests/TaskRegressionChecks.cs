using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace RuntimeTranslator.Tests
{
    public sealed class WaitingProvider : ITranslationProvider
    {
        public string DisplayName { get { return "Waiting"; } }
        public readonly ManualResetEventSlim Entered = new ManualResetEventSlim();
        public async Task<TranslationBatchResult> TranslateBatchAsync(TranslationBatchRequest request, CancellationToken token)
        {
            Entered.Set();
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            return new TranslationBatchResult();
        }
    }

    public sealed class BrokenProvider : ITranslationProvider
    {
        public bool FailPreparation = true;
        public string DisplayName
        {
            get
            {
                if (FailPreparation) throw new InvalidOperationException("Preparation failed");
                return "Timeout";
            }
        }
        public Task<TranslationBatchResult> TranslateBatchAsync(TranslationBatchRequest request, CancellationToken token)
        {
            throw new OperationCanceledException("Provider timeout");
        }
    }

    [DataContract]
    public sealed class PromptItem
    {
        [DataMember] public string id { get; set; }
        [DataMember] public string text { get; set; }
    }

    public static class TaskRegressionChecks
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
            catch (Exception ex) when (ex is InvalidOperationException || ex is SerializationException)
            {
                Check(true, name);
                return;
            }
            throw new Exception("FAILED: " + name);
        }

        private static void Field(string name, object value)
        {
            typeof(TranslationManager).GetField(name, Hidden).SetValue(null, value);
        }

        private static Task Worker(TranslationEntry entry, ITranslationProvider provider, RuntimeTranslatorSettings settings, CancellationTokenSource cancellation, bool paused = false)
        {
            Field("cancellation", cancellation);
            ManualResetEventSlim gate = (ManualResetEventSlim)typeof(TranslationManager).GetField("pauseGate", Hidden).GetValue(null);
            if (paused) gate.Reset(); else gate.Set();
            TranslationStatus status = (TranslationStatus)typeof(TranslationManager).GetField("status", Hidden).GetValue(null);
            status.State = paused ? TranslationState.Paused : TranslationState.Running;
            status.LastError = "";
            Task worker = Task.Run(async () =>
            {
                Task inner = (Task)typeof(TranslationManager).GetMethod("RunWorker", Hidden).Invoke(null,
                    new object[] { new List<TranslationEntry> { entry }, provider, settings, cancellation.Token, false });
                await inner.ConfigureAwait(false);
            });
            Field("worker", worker);
            return worker;
        }

        private static void Finish(Task worker)
        {
            if (!worker.Wait(5000)) throw new Exception("Worker did not finish in time");
            worker.GetAwaiter().GetResult();
        }

        private static TranslationBatchResult Parse(TranslationBatchRequest request, string json)
        {
            try
            {
                return (TranslationBatchResult)typeof(OpenAiCompatibleProvider).GetMethod("ParseResult", Hidden).Invoke(null,
                    new object[] { request, request.Entries.Select(entry => TextProtection.Protect(entry.SourceText)).ToList(), json });
            }
            catch (TargetInvocationException ex) { throw ex.InnerException; }
        }

        private static string Item(string id, string text)
        {
            return "{\"id\":" + JsonHttp.Serialize(id) + ",\"text\":" + JsonHttp.Serialize(text) + "}";
        }

        public static int Run(string folder)
        {
            Verse.GenFilePaths.ConfigFolderPath = folder;
            RuntimeTranslatorSettings settings = new RuntimeTranslatorSettings { ApplyRuntimeTranslations = false, ExportLanguageFiles = false };
            TranslationManager.Initialize(settings);
            TranslationEntry entry = new TranslationEntry { Id = "cancel-check", SourceHash = "hash", SourceText = "source" };
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Finish(Worker(entry, new RecordingProvider(), settings, cancellation));
                Check(TranslationManager.Status.State == TranslationState.Cancelled, "cancellation before worker starts reaches Cancelled");
            }
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                WaitingProvider provider = new WaitingProvider();
                Task worker = Worker(entry, provider, settings, cancellation);
                Check(provider.Entered.Wait(5000), "test provider received request");
                TranslationManager.Cancel();
                Finish(worker);
                Check(TranslationManager.Status.State == TranslationState.Cancelled && TranslationManager.Status.Failed == 0,
                    "cancelling an in-flight request is not a failed batch");
                provider.Entered.Dispose();
            }
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                RecordingProvider provider = new RecordingProvider();
                Task worker = Worker(entry, provider, settings, cancellation, true);
                TranslationManager.Cancel();
                Finish(worker);
                Check(TranslationManager.Status.State == TranslationState.Cancelled && provider.Requests == 0,
                    "cancelling a paused worker wakes it without sending a request");
                TranslationManager.Resume();
                TranslationManager.Pause();
                TranslationManager.Cancel();
                Check(TranslationManager.Status.State == TranslationState.Cancelled, "controls do not change a finished task state");
            }
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                Finish(Worker(entry, new BrokenProvider(), settings, cancellation));
                Check(TranslationManager.Status.State == TranslationState.Failed && TranslationManager.Status.LastError == "Preparation failed",
                    "unexpected preparation errors reach Failed with a reason");
            }
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                Finish(Worker(entry, new BrokenProvider { FailPreparation = false }, settings, cancellation));
                Check(TranslationManager.Status.State != TranslationState.Cancelled && TranslationManager.Status.Failed == 1 &&
                    TranslationManager.Status.LastError == "Provider timeout", "provider timeout is reported as a batch failure, not a user cancellation");
            }
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                Finish(Worker(entry, new RecordingProvider(), settings, cancellation));
                Check(TranslationManager.Status.State == TranslationState.Completed, "translation can run again after cancellation or failure");
                TaskCompletionSource<bool> finishing = new TaskCompletionSource<bool>();
                Field("worker", finishing.Task);
                TranslationManager.Cancel();
                TranslationManager.Pause();
                Check(TranslationManager.Status.State == TranslationState.Completed && !cancellation.IsCancellationRequested,
                    "terminal status cannot become Cancelling while task scheduling finishes");
                finishing.SetResult(true);
            }

            TranslationCacheStore store = new TranslationCacheStore();
            for (int i = 0; i < 51; i++) store.Put(new TranslationEntry { Id = "replace-" + i, SourceHash = "h", TranslatedText = "a" }, "Test");
            Field("cache", store);
            List<string> keys = store.Records.Select(TranslationManager.CacheRecordKey).ToList();
            HashSet<string> processed = new HashSet<string>();
            Check(TranslationManager.ReplaceCachedTranslationsBatch(keys, "a", "aa", 50, processed) == 50, "first replace batch processes 50 records");
            Check(TranslationManager.ReplaceCachedTranslationsBatch(keys, "a", "aa", 50, processed) == 1 && store.Records.All(record => record.TranslatedText == "aa"),
                "self-matching replacement reaches entry 51 without processing earlier entries twice");
            Check(TranslationManager.ReplaceCachedTranslationsBatch(keys, "a", "aa", 50, processed) == 0, "finished replace batch remains finished");
            processed.Clear();
            Check(TranslationManager.ReplaceCachedTranslationsBatch(keys.Take(1), "aa", "b", 50, processed) == 1 && store.Records.Count(record => record.TranslatedText == "b") == 1,
                "a new replacement stays within the visible records");
            Check(TranslationManager.ReplaceCachedTranslationsBatch(keys, "aa", "aa", 50, processed) == 0, "identical replacement is a no-op");

            TranslationBatchRequest request = new TranslationBatchRequest
            {
                Entries = new List<TranslationEntry>
                {
                    new TranslationEntry { Id = "first|id", SourceText = "<b>Hello {0}</b>\n\nSecond paragraph" },
                    new TranslationEntry { Id = "second", SourceText = "Text with ||| and quotes" }
                }
            };
            string protectedFirst = TextProtection.Protect(request.Entries[0].SourceText).Text;
            string prompt = (string)typeof(OpenAiCompatibleProvider).GetMethod("BuildPrompt", Hidden).Invoke(null,
                new object[] { request, request.Entries.Select(item => TextProtection.Protect(item.SourceText)).ToList() });
            List<PromptItem> promptItems = JsonHttp.Deserialize<List<PromptItem>>(prompt.Substring(prompt.IndexOf('[')));
            Check(promptItems.Count == 2 && promptItems[0].id == "first|id" && promptItems[0].text == protectedFirst,
                "outgoing JSON prompt preserves source paragraphs and IDs");
            string second = "  第二行\r\n\r\n\"quoted\" ||| \\path  ";
            string json = "[" + Item("second", second) + "," + Item("first|id", protectedFirst) + "]";
            TranslationBatchResult parsed = Parse(request, json);
            Check(parsed.Translations["first|id"] == request.Entries[0].SourceText, "JSON preserves paragraphs and restores placeholders");
            Check(parsed.Translations["second"] == second, "JSON preserves CRLF, blank lines, quotes, delimiters and whitespace");
            Check(Parse(request, "```json\n" + json + "\n```").Translations.Count == 2, "a surrounding JSON code fence is accepted");
            Reject(() => Parse(request, "[" + Item("first|id", protectedFirst) + "]"), "missing JSON entries are rejected");
            Reject(() => Parse(request, "[" + Item("first|id", protectedFirst) + "," + Item("first|id", protectedFirst) + "]"), "duplicate JSON IDs are rejected");
            Reject(() => Parse(request, "[" + Item("unknown", "x") + "," + Item("second", second) + "]"), "unknown JSON IDs are rejected");
            Reject(() => Parse(request, "[" + Item("first|id", protectedFirst.Replace("__RT_TOKEN_0__", "")) + "," + Item("second", second) + "]"), "JSON response cannot drop protected tokens");
            Reject(() => Parse(request, "[" + Item("first|id", " ") + "," + Item("second", second) + "]"), "empty JSON translations are rejected");
            Reject(() => Parse(request, json.Substring(0, json.Length - 1)), "truncated JSON is rejected instead of partly cached");
            Reject(() => Parse(request, "first|id|||line one\nline two"), "legacy line output is rejected instead of silently truncated");
            return passed;
        }
    }
}
