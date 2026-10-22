using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RuntimeTranslator.Tests
{
    [DataContract] public sealed class HttpChat { [DataMember] public List<HttpChatMessage> messages; }
    [DataContract] public sealed class HttpChatMessage { [DataMember] public string content; }

    public static class ProviderHttpChecks
    {
        public static int Run(string folder)
        {
            var previous = LoadedModManager.RunningMods.ToList();
            TcpListener portPicker = new TcpListener(IPAddress.Loopback, 0); portPicker.Start();
            int port = ((IPEndPoint)portPicker.LocalEndpoint).Port; portPicker.Stop();
            string endpoint = "http://127.0.0.1:" + port + "/";
            int phase = 0, requests = 0;
            bool rateLimited = false;
            List<string> texts = new List<string>();
            using (HttpListener listener = new HttpListener())
            {
                listener.Prefixes.Add(endpoint); listener.Start();
                Task server = Task.Run(async () =>
                {
                    while (listener.IsListening)
                    {
                        HttpListenerContext context;
                        try { context = await listener.GetContextAsync().ConfigureAwait(false); }
                        catch (HttpListenerException) { break; }
                        catch (ObjectDisposedException) { break; }
                        try
                        {
                            string body;
                            using (StreamReader reader = new StreamReader(context.Request.InputStream)) body = await reader.ReadToEndAsync().ConfigureAwait(false);
                            var chat = JsonHttp.Deserialize<HttpChat>(body);
                            string prompt = chat.messages.Last().content;
                            var items = JsonHttp.Deserialize<List<PromptItem>>(prompt.Substring(prompt.IndexOf('[')));
                            string output;
                            lock (texts) { requests++; texts.AddRange(items.Select(item => item.text)); }
                            if (context.Request.Headers["Authorization"] != "Bearer local-test-key" || context.Request.Url.AbsolutePath != "/v1/chat/completions")
                            { context.Response.StatusCode = 401; output = "bad request metadata"; }
                            else if (phase == 0 && !rateLimited)
                            { rateLimited = true; context.Response.StatusCode = 429; context.Response.Headers["Retry-After"] = "0"; output = "rate limited"; }
                            else if (phase == 0 && items.Any(item => item.text == "beta label"))
                            { context.Response.StatusCode = 400; output = "permanent test failure"; }
                            else if (phase == 2 && items.Count > 1)
                            { context.Response.StatusCode = 413; output = "batch too large"; }
                            else
                            {
                                var translations = items.Select(item => new PromptItem { id = item.id, text = "translated " + item.text }).ToList();
                                output = "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":" + JsonHttp.Serialize(JsonHttp.Serialize(translations)) + "}}]}";
                            }
                            byte[] bytes = Encoding.UTF8.GetBytes(output);
                            context.Response.ContentType = "application/json"; context.Response.ContentLength64 = bytes.Length;
                            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                        }
                        finally { context.Response.Close(); }
                    }
                });
                try
                {
                    GenFilePaths.ConfigFolderPath = folder;
                    LoadedModManager.RunningMods.Clear();
                    LoadedModManager.RunningMods.Add(new ModContentPack
                    {
                        PackageIdPlayerFacing = "http.test", Name = "HTTP Test",
                        AllDefs = new List<Def> { new Def { defName = "Alpha", label = "alpha label" }, new Def { defName = "Beta", label = "beta label" } }
                    });
                    var settings = new RuntimeTranslatorSettings
                    {
                        OpenAiBaseUrl = endpoint + "v1", OpenAiApiKey = "local-test-key", BatchSize = 1,
                        SelectedPackageIds = new List<string> { "http.test" }, ApplyRuntimeTranslations = false, ExportLanguageFiles = false
                    };
                    TranslationManager.Initialize(settings); TranslationManager.Start(); PerformanceChecks.FinishScan();
                    Assert(TranslationManager.Status.Completed == 1 && TranslationManager.Status.Failed == 1 && requests == 3,
                        "real HTTP 429 retries while a permanent error becomes one failed entry");
                    phase = 1;
                    lock (texts) texts.Clear();
                    TranslationManager.RetryFailed(); PerformanceChecks.FinishScan();
                    Assert(TranslationManager.Status.Completed == 1 && TranslationManager.Status.Failed == 0 && requests == 4 && texts.SequenceEqual(new[] { "beta label" }),
                        "failed-only retry sends just the failed source through the real HTTP provider");
                    phase = 2; requests = 0; settings.BatchSize = 50; settings.SkipCompletedTranslations = false;
                    TranslationManager.Start(); PerformanceChecks.FinishScan();
                    Assert(TranslationManager.Status.Completed == 2 && TranslationManager.Status.Failed == 0 && requests == 3,
                        "real HTTP 413 triggers two smaller successful requests");
                    return 3;
                }
                finally
                {
                    listener.Stop(); server.GetAwaiter().GetResult();
                    LoadedModManager.RunningMods.Clear(); LoadedModManager.RunningMods.AddRange(previous);
                }
            }
        }
        private static void Assert(bool condition, string name)
        { if (!condition) throw new Exception("FAILED: " + name); Console.WriteLine("PASS: " + name); }
    }
}
