using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RuntimeTranslator
{
    public sealed class ProtectedText
    {
        public string Text;
        public Dictionary<string, string> Tokens = new Dictionary<string, string>();

        public string Restore(string translated)
        {
            HashSet<string> restored = new HashSet<string>();
            string result = Regex.Replace(translated ?? string.Empty, @"__RT_TOKEN_\d+__", match =>
            {
                if (!Tokens.TryGetValue(match.Value, out string original) || !restored.Add(match.Value))
                {
                    throw new InvalidOperationException("Translation contains an unknown or repeated placeholder: " + match.Value);
                }
                return original;
            });
            if (restored.Count != Tokens.Count)
            {
                throw new InvalidOperationException("Translation lost one or more protected placeholders.");
            }
            return result;
        }
    }

    public static class TextProtection
    {
        private static readonly Regex TokenRegex = new Regex(
            @"(__RT_TOKEN_\d+__|<[^>]+>|\{[^}]+\}|\[[A-Za-z0-9_]+\]|\\n|%\w+)",
            RegexOptions.Compiled);

        public static bool HasVisibleText(string text)
        {
            string plain = Regex.Replace(text ?? string.Empty, @"<[^>]*>", string.Empty);
            return plain.Any(character => !char.IsWhiteSpace(character) && !char.IsControl(character) &&
                char.GetUnicodeCategory(character) != System.Globalization.UnicodeCategory.Format);
        }

        internal static bool IsGlossarySource(string source)
        {
            return !string.IsNullOrWhiteSpace(source) && source.IndexOfAny("<>{}[]\\".ToCharArray()) < 0 && !TokenRegex.IsMatch(source);
        }

        public static ProtectedText Protect(string source, IEnumerable<GlossaryTerm> terms = null)
        {
            ProtectedText protectedText = new ProtectedText();
            Dictionary<string, string> translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (GlossaryTerm term in terms ?? Enumerable.Empty<GlossaryTerm>())
            {
                if (term != null && IsGlossarySource(term.Source) && !string.IsNullOrWhiteSpace(term.Translation))
                    translations[term.Source.Trim()] = term.Translation.Trim();
            }
            string pattern = TokenRegex.ToString();
            if (translations.Count > 0)
            {
                pattern += "|(?<![\\p{L}\\p{N}_])(?<term>" + string.Join("|", translations.Keys.OrderByDescending(word => word.Length)
                    .ThenBy(word => word, StringComparer.OrdinalIgnoreCase).Select(Regex.Escape)) + ")(?![\\p{L}\\p{N}_])";
            }
            int index = 0;
            protectedText.Text = Regex.Replace(source ?? string.Empty, pattern, match =>
            {
                string token = "__RT_TOKEN_" + index++ + "__";
                protectedText.Tokens[token] = match.Groups["term"].Success ? translations[match.Value] : match.Value;
                return token;
            }, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return protectedText;
        }
    }
}
