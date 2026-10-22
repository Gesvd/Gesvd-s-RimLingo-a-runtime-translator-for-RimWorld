using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Xml.Linq;

namespace RuntimeTranslator.Tests
{
    public static class LanguageAndProviderChecks
    {
        private static int passed;
        private static void Check(bool valid, string name)
        {
            if (!valid) throw new Exception("FAILED: " + name);
            passed++; Console.WriteLine("PASS: " + name);
        }
        private static void Reject(Action action, string name)
        {
            bool rejected = false;
            try { action(); } catch (Exception) { rejected = true; }
            Check(rejected, name);
        }
        private static void Model(string root, string relative, string from, string to, bool weights = true)
        {
            string folder = Path.Combine(root, relative, "test-" + from + "-" + to);
            Directory.CreateDirectory(Path.Combine(folder, "model"));
            File.WriteAllText(Path.Combine(folder, "metadata.json"), "{\"from_code\":\"" + from + "\",\"to_code\":\"" + to + "\"}");
            if (weights) File.WriteAllText(Path.Combine(folder, "model", "model.bin"), "test fixture");
        }
        public static int Run(string folder)
        {
            List<Verse.ModContentPack> previousMods = Verse.LoadedModManager.RunningMods.ToList();
            Check(LanguageCatalog.Code("Russian") == "ru" && LanguageCatalog.Canonical("de") == "German" &&
                LanguageCatalog.Code("zh-TW") == "zt" && LanguageCatalog.Code("Portuguese") == "pt", "language dropdown identifiers map to provider codes and RimWorld folder names");
            Check(!LanguageCatalog.IsSupported("Englsh", true) && !LanguageCatalog.IsSupported("Auto", false) && LanguageCatalog.IsSupported("Auto", true),
                "language validation rejects misspellings and Auto as a target");
            Model(folder, "LocalEngine/models/packages", "en", "zh");
            Model(folder, "LanguagePacks/Japanese/models/packages", "ja", "en");
            Model(folder, "LanguagePacks/Japanese/models/packages", "en", "ja");
            Model(folder, "LanguagePacks/Korean/models/packages", "ko", "en", false);
            Verse.LoadedModManager.RunningMods.Add(new Verse.ModContentPack { RootDir = Path.Combine(folder, "LanguagePacks", "Japanese"), PackageIdPlayerFacing = "gesvd.rimlingo.language.japanese" });
            Verse.LoadedModManager.RunningMods.Add(new Verse.ModContentPack { RootDir = Path.Combine(folder, "LanguagePacks", "Korean"), PackageIdPlayerFacing = "gesvd.rimlingo.language.korean" });
            BundledLibreTranslate.Initialize(folder, Path.Combine(folder, "data"));
            Check(BundledLibreTranslate.CanTranslate("Japanese", "ChineseSimplified") && BundledLibreTranslate.CanTranslate("English", "Japanese"),
                "installed pack graph allows both direct and English-pivot offline routes");
            Check(!BundledLibreTranslate.CanTranslate("ChineseSimplified", "Japanese") && !BundledLibreTranslate.CanTranslate("Korean", "English") &&
                !BundledLibreTranslate.CanTranslate("Auto", "ChineseSimplified"), "missing reverse models, missing weights and offline Auto are not advertised");
            Model(folder, "LanguagePacks/ChineseSimplified/models/packages", "zh", "en");
            Verse.LoadedModManager.RunningMods.Add(new Verse.ModContentPack { RootDir = Path.Combine(folder, "LanguagePacks", "ChineseSimplified"), PackageIdPlayerFacing = "gesvd.rimlingo.language.chinesesimplified" });
            BundledLibreTranslate.Initialize(folder, Path.Combine(folder, "data"));
            Check(BundledLibreTranslate.CanTranslate("ChineseSimplified", "Japanese"), "reverse Chinese pack enables new pivot routes after restart");
            string workshopRoot = Path.Combine(folder, "Workshop files", "987654321");
            Model(workshopRoot, "models/packages", "de", "en");
            Model(workshopRoot, "models/packages", "en", "de");
            Verse.ModContentPack extension = new Verse.ModContentPack { RootDir = workshopRoot, PackageIdPlayerFacing = "gesvd.rimlingo.language.german" };
            BundledLibreTranslate.Initialize(folder, Path.Combine(folder, "data"));
            Check(!BundledLibreTranslate.CanTranslate("German", "ChineseSimplified"), "installed but disabled Workshop expansion is not loaded");
            Verse.LoadedModManager.RunningMods.Add(extension);
            BundledLibreTranslate.Initialize(folder, Path.Combine(folder, "data"));
            Check(BundledLibreTranslate.CanTranslate("German", "ChineseSimplified"), "enabled expansion is discovered through its numeric Workshop root with spaces");
            Verse.LoadedModManager.RunningMods.Remove(extension);
            BundledLibreTranslate.Initialize(folder, Path.Combine(folder, "data"));
            Check(!BundledLibreTranslate.CanTranslate("German", "ChineseSimplified"), "disabling an expansion removes its routes after initialization");
            string nestedRoot = Path.Combine(folder, "LanguagePacks", "Russian");
            Model(nestedRoot, "models/packages", "ru", "en");
            Directory.CreateDirectory(Path.Combine(nestedRoot, "About"));
            File.WriteAllText(Path.Combine(nestedRoot, "About", "About.xml"), "<ModMetaData><packageId>gesvd.rimlingo.language.russian</packageId></ModMetaData>");
            BundledLibreTranslate.Initialize(folder, Path.Combine(folder, "data"));
            Check(!BundledLibreTranslate.CanTranslate("Russian", "ChineseSimplified"), "unlisted nested expansion is not loaded even when its files are present");
            Check(!BundledLibreTranslate.IsLanguagePack(new Verse.ModContentPack { PackageIdPlayerFacing = "unrelated.mod" }), "unrelated mods are never treated as language expansions");
            RuntimeTranslatorSettings settings = new RuntimeTranslatorSettings { ProviderKind = ProviderKind.DeepSeek,
                DeepSeekApiKey = "deep-test-key", OpenAiApiKey = "openai-test-key", ClaudeApiKey = "claude-test-key" };
            ITranslationProvider deep = ProviderFactory.Create(settings);
            RuntimeTranslatorSettings mapped = (RuntimeTranslatorSettings)typeof(OpenAiCompatibleProvider).GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(deep);
            Check(deep.DisplayName == "DeepSeek" && mapped.OpenAiApiKey == "deep-test-key" && mapped.OpenAiBaseUrl == "https://api.deepseek.com" &&
                settings.OpenAiApiKey == "openai-test-key", "DeepSeek reuses the compatible adapter with independent snapshotted credentials");
            settings.ProviderKind = ProviderKind.Claude;
            Check(ProviderFactory.Create(settings) is ClaudeProvider && ProviderFactory.Create(settings).DisplayName == "Claude", "Claude selects its native Messages adapter and separate cache identity");
            RuntimeTranslatorSettings termSettings = GlossaryTranslator.ProviderSettings(settings);
            Check(termSettings.DeepSeekRules.Contains("standalone glossary") && termSettings.ClaudeRules.Contains("standalone glossary") && termSettings.GlossaryTerms.Count == 0,
                "new providers receive standalone glossary instructions without self-substitution");
            TranslationBatchRequest request = new TranslationBatchRequest { SourceLanguage = "English", TargetLanguage = "ChineseSimplified",
                Entries = new List<TranslationEntry> { new TranslationEntry { Id = "entry", SourceText = "Hello {0}\nWorld" } } };
            List<ProtectedText> protectedTexts = request.Entries.Select(entry => TextProtection.Protect(entry.SourceText)).ToList();
            using (var message = new ClaudeProvider(settings).BuildRequest(request, protectedTexts))
            {
                string body = message.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                Check(message.RequestUri.AbsoluteUri == "https://api.anthropic.com/v1/messages" && message.Headers.GetValues("x-api-key").Single() == "claude-test-key" &&
                    message.Headers.GetValues("anthropic-version").Single() == "2023-06-01" && !message.Headers.Contains("Authorization"), "Claude request uses the native endpoint and authentication headers");
                Check(body.Contains("max_tokens") && body.Contains("system") && body.Contains(settings.ClaudeModel) && body.Contains("__RT_TOKEN_0__") && !body.Contains("openai-test-key"),
                    "Claude payload preserves tokens and keeps API keys outside the JSON body");
            }
            string translated = "[{\"id\":\"entry\",\"text\":\"你好 __RT_TOKEN_0__\\n世界\"}]";
            Func<string, string, string> envelope = (stop, output) => "{\"stop_reason\":\"" + stop + "\",\"content\":[{\"type\":\"text\",\"text\":" + JsonHttp.Serialize(output) + "}]}";
            Check(ClaudeProvider.ReadResponse(request, protectedTexts, envelope("end_turn", translated)).Translations["entry"] == "你好 {0}\n世界",
                "Claude response restores placeholders and paragraph breaks through the shared JSON validator");
            Reject(() => ClaudeProvider.ReadResponse(request, protectedTexts, envelope("max_tokens", translated)), "truncated Claude output is rejected even if the partial JSON happens to be complete");
            Reject(() => ClaudeProvider.ReadResponse(request, protectedTexts, envelope("refusal", translated)), "Claude refusal retains original translations");
            Reject(() => ClaudeProvider.ReadResponse(request, protectedTexts, envelope("end_turn", translated.Replace("__RT_TOKEN_0__", "lost"))), "Claude output cannot lose protected placeholders");
            Reject(() => ClaudeProvider.ReadResponse(request, protectedTexts, envelope("end_turn", translated.Replace("entry", "unknown"))), "Claude output cannot invent translation IDs");
            settings.ClaudeApiKey = "";
            Reject(() => new ClaudeProvider(settings).BuildRequest(request, protectedTexts), "missing Claude key fails before making an HTTP request");
            Check((int)ProviderKind.OpenAiCompatible == 0 && (int)ProviderKind.LibreTranslate == 1 && (int)ProviderKind.LaraTranslate == 2,
                "adding providers preserves existing saved provider enum values");
            Verse.LoadedModManager.RunningMods.Clear();
            Verse.LoadedModManager.RunningMods.AddRange(previousMods);
            return passed;
        }
    }
}
