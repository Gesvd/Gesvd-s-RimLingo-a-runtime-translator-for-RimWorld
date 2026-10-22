using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Verse;

namespace RuntimeTranslator
{
    [XmlRoot("RuntimeTranslatorCache")]
    public sealed class TranslationCacheDocument
    {
        [XmlElement("Entry")]
        public List<TranslationCacheRecord> Entries = new List<TranslationCacheRecord>();
    }

    public sealed class TranslationCacheStore
    {
        private readonly Dictionary<string, TranslationCacheRecord> records = new Dictionary<string, TranslationCacheRecord>();
        private readonly HashSet<string> manualIds = new HashSet<string>();
        private readonly string cachePath;
        private static readonly XmlSerializer serializer = new XmlSerializer(typeof(TranslationCacheDocument));
        public long Revision { get; private set; }
        private long savedRevision;
        public bool IsDirty { get { return Revision != savedRevision; } }
        internal void MarkSaved(long revision) { savedRevision = revision; }

        public TranslationCacheStore()
        {
            string folder = Path.Combine(GenFilePaths.ConfigFolderPath, "RuntimeTranslator");
            cachePath = Path.Combine(folder, "cache.xml");
        }

        public IReadOnlyCollection<TranslationCacheRecord> Records
        {
            get { return records.Values; }
        }

        public void Load()
        {
            records.Clear();
            manualIds.Clear();
            Revision++;
            savedRevision = Revision;
            if (!File.Exists(cachePath) && !File.Exists(cachePath + ".bak"))
            {
                return;
            }

            try
            {
                LoadFile(cachePath);
            }
            catch (Exception ex)
            {
                Log.Warning("[RuntimeTranslator] Could not load cache: " + ex.Message);
                try
                {
                    LoadFile(cachePath + ".bak");
                    Revision++;
                    Log.Warning("[RuntimeTranslator] Recovered translation cache from backup.");
                }
                catch (Exception backupError)
                {
                    Log.Warning("[RuntimeTranslator] Could not load cache backup: " + backupError.Message);
                }
            }
        }

        private void LoadFile(string path)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(TranslationCacheDocument));
            using (FileStream stream = File.OpenRead(path))
            {
                TranslationCacheDocument document = (TranslationCacheDocument)serializer.Deserialize(stream);
                foreach (TranslationCacheRecord record in document.Entries ?? new List<TranslationCacheRecord>())
                {
                    if (record != null && !string.IsNullOrWhiteSpace(record.Id))
                    {
                        if (record.PackageId == RuntimeTranslatorSettings.LegacyPackageId)
                        {
                            string prefix = RuntimeTranslatorSettings.LegacyPackageId + "|";
                            if (record.Id.StartsWith(prefix, StringComparison.Ordinal))
                                record.Id = RuntimeTranslatorSettings.PackageId + record.Id.Substring(prefix.Length - 1);
                            record.PackageId = RuntimeTranslatorSettings.PackageId;
                        }
                        string key = Key(record.Id, record.SourceHash, record.Provider);
                        if (!records.TryGetValue(key, out TranslationCacheRecord existing) || record.UpdatedUtcTicks >= existing.UpdatedUtcTicks)
                            records[key] = record;
                        if (record.IsManual) manualIds.Add(record.Id);
                    }
                }
            }
        }

        public bool Save()
        {
            long revision = Revision;
            bool saved = SaveSnapshot(records.Values.ToList());
            if (saved) MarkSaved(revision);
            return saved;
        }

        internal bool SaveSnapshot(List<TranslationCacheRecord> snapshot)
        {
            string temporaryPath = cachePath + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                TranslationCacheDocument document = new TranslationCacheDocument
                {
                    Entries = snapshot
                };
                using (FileStream stream = File.Create(temporaryPath))
                {
                    serializer.Serialize(stream, document);
                    stream.Flush(true);
                }
                if (File.Exists(cachePath))
                {
                    File.Replace(temporaryPath, cachePath, cachePath + ".bak");
                }
                else
                {
                    File.Move(temporaryPath, cachePath);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("[RuntimeTranslator] Could not save cache: " + ex.Message);
                return false;
            }
            finally
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception ex)
                {
                    Log.Warning("[RuntimeTranslator] Could not remove temporary cache: " + ex.Message);
                }
            }
        }

        public bool TryGet(TranslationEntry entry, string provider, out TranslationCacheRecord record)
        {
            return records.TryGetValue(Key(entry.Id, entry.SourceHash, provider), out record);
        }

        public bool Put(TranslationEntry entry, string provider, bool replaceExisting = false)
        {
            if (manualIds.Contains(entry.Id) && records.Values.Any(record => record.IsManual && record.Id == entry.Id && record.SourceText == entry.SourceText)) return false;
            if (replaceExisting)
                foreach (string key in records.Where(pair => pair.Value.Id == entry.Id && pair.Value.SourceText == entry.SourceText).Select(pair => pair.Key).ToList())
                    records.Remove(key);
            TranslationCacheRecord record = TranslationCacheRecord.FromEntry(entry, provider);
            records[Key(record.Id, record.SourceHash, provider)] = record;
            Revision++;
            return true;
        }

        public bool TryUpdate(string id, string sourceHash, string provider, string translatedText, out TranslationCacheRecord updated)
        {
            if (records.TryGetValue(Key(id, sourceHash, provider), out updated))
            {
                if (updated.TranslatedText == (translatedText ?? string.Empty)) return true;
                string source = updated.SourceText;
                foreach (TranslationCacheRecord other in records.Values.Where(record => record.Id == id && record.SourceText == source)) other.IsManual = false;
                updated.TranslatedText = translatedText ?? string.Empty;
                updated.IsManual = TextProtection.HasVisibleText(updated.TranslatedText);
                if (updated.IsManual) manualIds.Add(id);
                else if (!records.Values.Any(record => record.Id == id && record.IsManual)) manualIds.Remove(id);
                updated.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
                Revision++;
                return true;
            }

            return false;
        }

        public bool SetManual(string id, string sourceHash, string provider, bool locked)
        {
            if (!records.TryGetValue(Key(id, sourceHash, provider), out TranslationCacheRecord selected)) return false;
            foreach (TranslationCacheRecord record in records.Values.Where(record => record.Id == id && record.SourceText == selected.SourceText))
            {
                bool protect = locked && record == selected;
                if (record.IsManual == protect) continue;
                record.IsManual = protect;
                Revision++;
            }
            if (records.Values.Any(record => record.Id == id && record.IsManual)) manualIds.Add(id); else manualIds.Remove(id);
            return true;
        }

        private static string Key(string id, string sourceHash, string provider)
        {
            return (id ?? string.Empty) + "|" + (sourceHash ?? string.Empty) + "|" + (provider ?? string.Empty);
        }
    }
}
