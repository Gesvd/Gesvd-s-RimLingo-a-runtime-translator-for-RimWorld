namespace RuntimeTranslator
{
    public static class ProviderFactory
    {
        public static ITranslationProvider Create(RuntimeTranslatorSettings settings)
        {
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
