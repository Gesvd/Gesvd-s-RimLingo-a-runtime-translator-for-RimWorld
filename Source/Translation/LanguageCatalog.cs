using System;
using System.Collections.Generic;
using System.Linq;

namespace RuntimeTranslator
{
    public static class LanguageCatalog
    {
        public static readonly string[] Values = { "English", "ChineseSimplified", "ChineseTraditional", "Japanese", "Korean", "Russian", "German", "French", "Spanish", "Portuguese" };
        private static readonly string[] Codes = { "en", "zh", "zt", "ja", "ko", "ru", "de", "fr", "es", "pt" };
        public static string Code(string language)
        {
            string key = (language ?? "").Trim().ToLowerInvariant();
            if (key == "auto") return "auto";
            if (new[] { "chinese", "zh-cn", "zh-hans" }.Contains(key)) return "zh";
            if (new[] { "zh-tw", "zh-hant" }.Contains(key)) return "zt";
            for (int i = 0; i < Values.Length; i++)
                if (key == Values[i].ToLowerInvariant() || key == Codes[i]) return Codes[i];
            return key;
        }
        public static string Canonical(string language)
        {
            string code = Code(language);
            if (code == "auto") return "Auto";
            int index = Array.IndexOf(Codes, code);
            return index < 0 ? language : Values[index];
        }
        public static bool IsSupported(string language, bool source)
        {
            string code = Code(language);
            return (source && code == "auto") || Codes.Contains(code);
        }
    }
}
