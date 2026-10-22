using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace RuntimeTranslator
{
    public static class BundledLibreTranslate
    {
        private static readonly SemaphoreSlim startGate = new SemaphoreSlim(1, 1);
        private static readonly object processLock = new object();
        private static Process process;
        private static string root;
        private static Dictionary<string, HashSet<string>> pairs;
        private static string data;
        private static string[] enabledPackRoots = new string[0];
        private static string endpoint;
        private static string identity;
        private static volatile string state = "Stopped";
        public static string Status { get { return state; } }

        public static void Initialize(string modRoot, string dataFolder)
        {
            root = Path.Combine(modRoot, "LocalEngine");
            data = dataFolder;
            enabledPackRoots = Verse.LoadedModManager.RunningMods.Where(IsLanguagePack)
                .Select(pack => pack.RootDir).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            pairs = null;
        }

        internal static bool IsLanguagePack(Verse.ModContentPack pack)
        {
            return pack != null && (pack.PackageIdPlayerFacing ?? "").StartsWith("gesvd.rimlingo.language.", StringComparison.OrdinalIgnoreCase);
        }

        public static bool CanTranslate(string source, string target)
        {
            string from = LanguageCatalog.Code(source), to = LanguageCatalog.Code(target);
            if (from == to || from == "auto") return false;
            if (pairs == null)
            {
                Dictionary<string, HashSet<string>> found = new Dictionary<string, HashSet<string>>();
                List<string> folders = new List<string> { Path.Combine(root ?? "", "models", "packages") };
                folders.AddRange(enabledPackRoots.Select(folder => Path.Combine(folder, "models", "packages")));
                foreach (string folder in folders.Where(Directory.Exists))
                foreach (string model in Directory.GetDirectories(folder))
                {
                    string metadata = Path.Combine(model, "metadata.json");
                    if (!File.Exists(metadata) || !File.Exists(Path.Combine(model, "model", "model.bin"))) continue;
                    try
                    {
                        ModelMetadata item = JsonHttp.Deserialize<ModelMetadata>(File.ReadAllText(metadata));
                        if (string.IsNullOrEmpty(item?.from_code) || string.IsNullOrEmpty(item.to_code)) continue;
                        if (!found.TryGetValue(item.from_code, out HashSet<string> targets)) found[item.from_code] = targets = new HashSet<string>();
                        targets.Add(item.to_code);
                    }
                    catch (Exception ex) { Verse.Log.Warning("[RuntimeTranslator] Could not read model metadata: " + ex.Message); }
                }
                pairs = found;
            }
            Queue<string> pending = new Queue<string>();
            HashSet<string> seen = new HashSet<string> { from };
            pending.Enqueue(from);
            while (pending.Count > 0)
            {
                if (!pairs.TryGetValue(pending.Dequeue(), out HashSet<string> targets)) continue;
                foreach (string candidate in targets)
                {
                    if (candidate == to) return true;
                    if (seen.Add(candidate)) pending.Enqueue(candidate);
                }
            }
            return false;
        }

        [DataContract]
        private sealed class ModelMetadata
        {
            [DataMember] public string from_code { get; set; }
            [DataMember] public string to_code { get; set; }
        }

        private static string Quote(string value)
        {
            if (value.Contains("\"") || value.EndsWith("\\", StringComparison.Ordinal)) throw new ArgumentException("Invalid local engine path.");
            return "\"" + value + "\"";
        }

        public static async Task<string> GetEndpointAsync(CancellationToken token)
        {
            await startGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                lock (processLock)
                {
                    if (TranslationLifetime.IsStopping) throw new OperationCanceledException(TranslationLifetime.Token);
                    if (process == null || process.HasExited)
                    {
                        string python = Path.Combine(root ?? string.Empty, "runtime", "python.exe");
                        if (!File.Exists(python) || !File.Exists(Path.Combine(root ?? string.Empty, "READY.json")))
                            throw new InvalidOperationException("Bundled LibreTranslate is not installed. See LocalEngine/README.md.");
                        Directory.CreateDirectory(data);
                        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
                        listener.Start();
                        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                        listener.Stop();
                        endpoint = "http://127.0.0.1:" + port;
                        identity = Guid.NewGuid().ToString("N");
                        ProcessStartInfo info = new ProcessStartInfo(python,
                            "-u " + Quote(Path.Combine(root, "serve.py")) + " --port " + port +
                            " --parent-pid " + Process.GetCurrentProcess().Id + " --identity " + identity + " --data " + Quote(data) +
                            string.Concat(enabledPackRoots.Select(folder => " --pack " + Quote(folder))))
                        {
                            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                        };
                        process?.Dispose();
                        process = Process.Start(info);
                        state = "Starting local engine";
                    }
                }
                using (HttpClient client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) })
                {
                    Stopwatch timer = Stopwatch.StartNew();
                    while (timer.Elapsed < TimeSpan.FromMinutes(2))
                    {
                        token.ThrowIfCancellationRequested();
                        if (TranslationLifetime.IsStopping) throw new OperationCanceledException(TranslationLifetime.Token);
                        lock (processLock)
                        {
                            if (process == null || process.HasExited) throw new InvalidOperationException("Local engine exited. See " + Path.Combine(data, "engine.log"));
                        }
                        try
                        {
                            using (HttpResponseMessage response = await client.GetAsync(endpoint + "/runtime-translator/health", token).ConfigureAwait(false))
                            {
                                if (response.IsSuccessStatusCode && (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim() == identity)
                                {
                                    state = "Ready (offline language packs)";
                                    return endpoint;
                                }
                            }
                        }
                        catch (HttpRequestException) { }
                        catch (TaskCanceledException) when (!token.IsCancellationRequested) { }
                        await Task.Delay(500, token).ConfigureAwait(false);
                    }
                }
                throw new TimeoutException("Local engine startup timed out. See " + Path.Combine(data, "engine.log"));
            }
            catch (Exception ex) { state = ex is OperationCanceledException ? "Stopped / cancelled" : "Local engine error: " + ex.Message; throw; }
            finally { startGate.Release(); }
        }

        public static void Stop()
        {
            Process owned;
            lock (processLock) { owned = process; process = null; state = "Stopped"; }
            if (owned == null) return;
            try { if (!owned.HasExited) owned.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally { owned.Dispose(); }
        }
    }
}