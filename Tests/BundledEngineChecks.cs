using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace RuntimeTranslator.Tests
{
    public static class BundledEngineChecks
    {
        private static int passed;
        private static void Check(bool valid, string name)
        {
            if (!valid) throw new Exception("FAILED: " + name);
            passed++;
            Console.WriteLine("PASS: " + name);
        }
        public static int Run(string root, string folder)
        {
            RuntimeTranslatorSettings settings = new RuntimeTranslatorSettings();
            Check(!settings.UseBundledLibreTranslate, "existing users keep their external provider configuration");
            settings.UseBundledLibreTranslate = true;
            RuntimeTranslatorSettings snapshot = settings.Snapshot();
            settings.UseBundledLibreTranslate = false;
            Check(snapshot.UseBundledLibreTranslate && new LibreTranslateProvider(snapshot).DisplayName != new LibreTranslateProvider(settings).DisplayName,
                "bundled engine choice is snapshotted and its cache provider identity is separate");
            bool rejected = false;
            try
            {
                new LibreTranslateProvider(snapshot).TranslateBatchAsync(new TranslationBatchRequest
                { SourceLanguage = "English", TargetLanguage = "Japanese" }, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "unsupported bundled language fails before starting a process");
            BundledLibreTranslate.Initialize(Path.Combine(folder, "missing-engine"), folder);
            rejected = false;
            try { BundledLibreTranslate.GetEndpointAsync(CancellationToken.None).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "source-only checkout reports missing runtime clearly");
            if (Environment.GetEnvironmentVariable("RT_TEST_BUNDLED") == "1")
            {
                BundledLibreTranslate.Initialize(root, Path.Combine(folder, "engine"));
                try
                {
                    TranslationBatchResult result = new LibreTranslateProvider(snapshot).TranslateBatchAsync(new TranslationBatchRequest
                    {
                        SourceLanguage = "English", TargetLanguage = "ChineseSimplified",
                        Entries = new List<TranslationEntry> { new TranslationEntry { Id = "offline", SourceText = "<b>The pawn needs food.</b> {0}" } }
                    }, CancellationToken.None).GetAwaiter().GetResult();
                    string translated = result.Translations["offline"];
                    Check(translated.Contains("小人") && translated.Contains("<b>") && translated.Contains("</b>") && translated.Contains("{0}") && !translated.Contains("__RT_TOKEN_"),
                        "real C# provider launches portable engine and restores glossary, tags and variables");
                    string endpoint = BundledLibreTranslate.GetEndpointAsync(CancellationToken.None).GetAwaiter().GetResult();
                    Check(BundledLibreTranslate.GetEndpointAsync(CancellationToken.None).GetAwaiter().GetResult() == endpoint,
                        "subsequent requests reuse the owned local process");
                }
                finally { BundledLibreTranslate.Stop(); BundledLibreTranslate.Stop(); }
                Check(BundledLibreTranslate.Status == "Stopped", "owned engine stop is safe to repeat");
            }
            return passed;
        }
    }
}
