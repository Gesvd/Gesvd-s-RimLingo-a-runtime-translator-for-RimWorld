using System;
using System.Collections.Generic;

namespace RuntimeTranslator
{
    public enum TranslationState
    {
        Idle,
        Scanning,
        Running,
        Paused,
        Cancelling,
        Completed,
        Failed,
        Cancelled
    }

    public sealed class TranslationStatus
    {
        public TranslationState State = TranslationState.Idle;
        public int Total;
        public int Completed;
        public int Failed;
        public int Skipped;
        public string CurrentItem = string.Empty;
        public string LastError = string.Empty;
    }

    public sealed class TranslationEntry
    {
        public string Id;
        public string PackageId;
        public string ModName;
        public string DefTypeName;
        public string DefName;
        public string FieldName;
        public string SourceLanguage;
        public string TargetLanguage;
        public string SourceText;
        public string SourceHash;
        public string TranslatedText;

        public string ExportFileName
        {
            get
            {
                return FileNameSanitizer.Sanitize(DefTypeName ?? "UnknownDef") + ".xml";
            }
        }
    }

    public sealed class TranslationBatchRequest
    {
        public List<TranslationEntry> Entries = new List<TranslationEntry>();
        public string SourceLanguage;
        public string TargetLanguage;
    }

    public sealed class TranslationBatchResult
    {
        public Dictionary<string, string> Translations = new Dictionary<string, string>();
    }

    public interface ITranslationProvider
    {
        string DisplayName { get; }
        System.Threading.Tasks.Task<TranslationBatchResult> TranslateBatchAsync(
            TranslationBatchRequest request,
            System.Threading.CancellationToken cancellationToken);
    }

    public sealed class TranslationCacheRecord
    {
        public string Id;
        public string PackageId;
        public string ModName;
        public string DefTypeName;
        public string DefName;
        public string FieldName;
        public string SourceLanguage;
        public string TargetLanguage;
        public string SourceHash;
        public string SourceText;
        public string TranslatedText;
        public string Provider;
        public long UpdatedUtcTicks;

        public static TranslationCacheRecord FromEntry(TranslationEntry entry, string provider)
        {
            return new TranslationCacheRecord
            {
                Id = entry.Id,
                PackageId = entry.PackageId,
                ModName = entry.ModName,
                DefTypeName = entry.DefTypeName,
                DefName = entry.DefName,
                FieldName = entry.FieldName,
                SourceLanguage = entry.SourceLanguage,
                TargetLanguage = entry.TargetLanguage,
                SourceHash = entry.SourceHash,
                SourceText = entry.SourceText,
                TranslatedText = entry.TranslatedText,
                Provider = provider,
                UpdatedUtcTicks = DateTime.UtcNow.Ticks
            };
        }

        public TranslationEntry ToEntry()
        {
            return new TranslationEntry
            {
                Id = Id,
                PackageId = PackageId,
                ModName = ModName,
                DefTypeName = DefTypeName,
                DefName = DefName,
                FieldName = FieldName,
                SourceLanguage = SourceLanguage,
                TargetLanguage = TargetLanguage,
                SourceHash = SourceHash,
                SourceText = SourceText,
                TranslatedText = TranslatedText
            };
        }
    }
}
