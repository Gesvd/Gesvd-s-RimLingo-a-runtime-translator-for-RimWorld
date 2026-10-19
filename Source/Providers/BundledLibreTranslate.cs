using System;
using System.Diagnostics;
using System.IO;
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
        private static string data;
        private static string endpoint;
        private static string identity;
        private static volatile string state = "Stopped";
        public static string Status { get { return state; } }

        public static void Initialize(string modRoot, string dataFolder)
        {
            root = Path.Combine(modRoot, "LocalEngine");
            data = dataFolder;
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
                            " --parent-pid " + Process.GetCurrentProcess().Id + " --identity " + identity + " --data " + Quote(data))
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
                                    state = "Ready (offline English to Chinese)";
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