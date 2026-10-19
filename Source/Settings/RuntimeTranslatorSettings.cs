using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RuntimeTranslator
{
    public enum ProviderKind
    {
        OpenAiCompatible,
        LibreTranslate,
        LaraTranslate
    }

    public sealed class RuntimeTranslatorSettings : ModSettings
    {
        public string SourceLanguage = "English";
        public string TargetLanguage = "ChineseSimplified";
        public ProviderKind ProviderKind = ProviderKind.OpenAiCompatible;
        public string OpenAiBaseUrl = "https://api.openai.com/v1";
        public string OpenAiModel = "gpt-4.1-mini";
        public string OpenAiApiKey = string.Empty;
        public string OpenAiRules = "Translate RimWorld mod text naturally. Preserve placeholders, XML-like tags, variables, numbers, and game terms.";
        public string LibreTranslateUrl = "http://localhost:5000";
        public string LibreTranslateApiKey = string.Empty;
        public string LaraTranslateUrl = "http://127.0.0.1:8765";
        public string LaraTranslateModel = "text";
        public string LaraTranslateApiKey = string.Empty;
        public string LaraTranslateApiId = string.Empty;
        public string LaraTranslateRules = "Translate RimWorld mod text naturally. Preserve placeholders, XML-like tags, variables, numbers, and game terms.";
        public int BatchSize = 12;
        public bool SkipCompletedTranslations = true;
        public bool ApplyRuntimeTranslations = true;
        public bool ExportLanguageFiles = true;
        public List<string> SelectedPackageIds = new List<string>();
        public List<GlossaryTerm> GlossaryTerms = Glossary.Defaults();

        public RuntimeTranslatorSettings Snapshot()
        {
            RuntimeTranslatorSettings snapshot = (RuntimeTranslatorSettings)MemberwiseClone();
            snapshot.SelectedPackageIds = new List<string>(SelectedPackageIds ?? new List<string>());
            snapshot.GlossaryTerms = (GlossaryTerms ?? new List<GlossaryTerm>()).Where(term => term != null).Select(term => term.Clone()).ToList();
            return snapshot;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref SourceLanguage, "sourceLanguage", "English");
            Scribe_Values.Look(ref TargetLanguage, "targetLanguage", "ChineseSimplified");
            Scribe_Values.Look(ref ProviderKind, "providerKind", ProviderKind.OpenAiCompatible);
            Scribe_Values.Look(ref OpenAiBaseUrl, "openAiBaseUrl", "https://api.openai.com/v1");
            Scribe_Values.Look(ref OpenAiModel, "openAiModel", "gpt-4.1-mini");
            Scribe_Values.Look(ref OpenAiApiKey, "openAiApiKey", string.Empty);
            Scribe_Values.Look(ref OpenAiRules, "openAiRules", "Translate RimWorld mod text naturally. Preserve placeholders, XML-like tags, variables, numbers, and game terms.");
            Scribe_Values.Look(ref LibreTranslateUrl, "libreTranslateUrl", "http://localhost:5000");
            Scribe_Values.Look(ref LibreTranslateApiKey, "libreTranslateApiKey", string.Empty);
            Scribe_Values.Look(ref LaraTranslateUrl, "laraTranslateUrl", "http://127.0.0.1:8765");
            Scribe_Values.Look(ref LaraTranslateModel, "laraTranslateModel", "text");
            Scribe_Values.Look(ref LaraTranslateApiKey, "laraTranslateApiKey", string.Empty);
            Scribe_Values.Look(ref LaraTranslateApiId, "laraTranslateApiId", string.Empty);
            Scribe_Values.Look(ref LaraTranslateRules, "laraTranslateRules", "Translate RimWorld mod text naturally. Preserve placeholders, XML-like tags, variables, numbers, and game terms.");
            Scribe_Values.Look(ref BatchSize, "batchSize", 12);
            Scribe_Values.Look(ref SkipCompletedTranslations, "skipCompletedTranslations", true);
            Scribe_Values.Look(ref ApplyRuntimeTranslations, "applyRuntimeTranslations", true);
            Scribe_Values.Look(ref ExportLanguageFiles, "exportLanguageFiles", true);
            Scribe_Collections.Look(ref SelectedPackageIds, "selectedPackageIds", LookMode.Value);
            Scribe_Collections.Look(ref GlossaryTerms, "glossaryTerms", LookMode.Deep);
            if (GlossaryTerms == null) GlossaryTerms = Glossary.Defaults();
            if (SelectedPackageIds == null)
            {
                SelectedPackageIds = new List<string>();
            }
            if (LaraTranslateUrl == "https://api.laratranslate.com")
            {
                LaraTranslateUrl = "http://127.0.0.1:8765";
            }
        }
    }
}
