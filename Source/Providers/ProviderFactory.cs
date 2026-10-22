namespace RuntimeTranslator
{
    public static class ProviderFactory
    {
        public static ITranslationProvider Create(RuntimeTranslatorSettings settings)
        {
            if (settings.ProviderKind == ProviderKind.DeepSeek)
            {
                RuntimeTranslatorSettings deepSeek = settings.Snapshot();
                deepSeek.OpenAiBaseUrl = settings.DeepSeekBaseUrl;
                deepSeek.OpenAiModel = settings.DeepSeekModel;
                deepSeek.OpenAiApiKey = settings.DeepSeekApiKey;
                deepSeek.OpenAiRules = settings.DeepSeekRules;
                return new OpenAiCompatibleProvider(deepSeek, "DeepSeek");
            }
            if (settings.ProviderKind == ProviderKind.Claude) return new ClaudeProvider(settings);
            if (settings.ProviderKind == ProviderKind.LibreTranslate)
            {
                return new LibreTranslateProvider(settings);
            }
            if (settings.ProviderKind == ProviderKind.LaraTranslate)
            {
                return new LaraTranslateProvider(settings);
            }

            return new OpenAiCompatibleProvider(settings);
        }
    }
}
