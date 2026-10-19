using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using Verse;

namespace RuntimeTranslator
{
    public static class LanguageFileExporter
    {
        public static void Export(IEnumerable<TranslationCacheRecord> records, string targetLanguage, CancellationToken token = default(CancellationToken))
        {
            try
            {
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(targetLanguage) || targetLanguage == "." || targetLanguage == ".." ||
                    FileNameSanitizer.Sanitize(targetLanguage) != targetLanguage || targetLanguage.Contains("/") || targetLanguage.Contains("\\"))
                {
                    throw new ArgumentException("Target language must be a single folder name.");
                }
                string root = Path.Combine(GenFilePaths.ConfigFolderPath, "RuntimeTranslator", "GeneratedLanguages", targetLanguage);
                HashSet<string> writtenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (IGrouping<string, TranslationCacheRecord> modGroup in records
                    .Where(record => string.Equals(record.TargetLanguage, targetLanguage, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(record => new { record.PackageId, record.DefTypeName, record.DefName, record.FieldName })
                    .Select(group => group.OrderByDescending(record => record.UpdatedUtcTicks).First())
                    .GroupBy(record => record.PackageId ?? "UnknownMod"))
                {
                    foreach (IGrouping<string, TranslationCacheRecord> typeGroup in modGroup.GroupBy(record => record.DefTypeName ?? "UnknownDef"))
                    {
                        token.ThrowIfCancellationRequested();
                        string modRoot = Path.Combine(root, FileNameSanitizer.Sanitize(modGroup.Key));
                        string folder = typeGroup.Key == "Keyed" ? Path.Combine(modRoot, "Keyed")
                            : Path.Combine(modRoot, "DefInjected", FileNameSanitizer.Sanitize(typeGroup.Key));
                        Directory.CreateDirectory(folder);
                        string filePath = Path.Combine(folder, "RuntimeTranslator.xml");
                        WriteDefInjectedFile(filePath, typeGroup.Where(record => !string.IsNullOrWhiteSpace(record.TranslatedText)), token);
                        writtenFiles.Add(filePath);
                    }
                }

                // Remove only this exporter's files, including stale output from earlier exports.
                if (Directory.Exists(root))
                {
                    foreach (string modFolder in Directory.EnumerateDirectories(root))
                    {
                        token.ThrowIfCancellationRequested();
                        string keyedFile = Path.Combine(modFolder, "Keyed", "RuntimeTranslator.xml");
                        if (File.Exists(keyedFile) && !writtenFiles.Contains(keyedFile)) File.Delete(keyedFile);
                        string defFolder = Path.Combine(modFolder, "DefInjected");
                        if (!Directory.Exists(defFolder)) continue;
                        foreach (string typeFolder in Directory.EnumerateDirectories(defFolder))
                        {
                            string filePath = Path.Combine(typeFolder, "RuntimeTranslator.xml");
                            if (!writtenFiles.Contains(filePath)) File.Delete(filePath);
                        }
                    }
                }
                TranslationManager.Notify("Gesvd’s RimLingo exported language files to " + root, true);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Error("[RuntimeTranslator] Export failed: " + ex);
                TranslationManager.Notify("Gesvd’s RimLingo export failed: " + ex.Message, false);
            }
        }

        private static void WriteDefInjectedFile(string filePath, IEnumerable<TranslationCacheRecord> records, CancellationToken token)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            builder.AppendLine("<LanguageData>");
            foreach (TranslationCacheRecord record in records.OrderBy(record => record.DefName).ThenBy(record => record.FieldName))
            {
                token.ThrowIfCancellationRequested();
                string key = record.DefTypeName == "Keyed" ? record.DefName
                    : (record.DefName ?? "UnnamedDef") + "." + (record.FieldName ?? "label");
                string value = SecurityElement.Escape(record.TranslatedText ?? string.Empty);
                builder.AppendLine("  <" + key + ">" + value + "</" + key + ">");
            }
            builder.AppendLine("</LanguageData>");
            File.WriteAllText(filePath, builder.ToString(), Encoding.UTF8);
        }
    }
}
