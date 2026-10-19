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
        private readonly string cachePath;

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
                        records[Key(record.Id, record.SourceHash, record.Provider)] = record;
                    }
                }
            }
        }

        public bool Save()
        {
            string temporaryPath = cachePath + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                TranslationCacheDocument document = new TranslationCacheDocument
                {
                    Entries = records.Values.OrderBy(record => record.PackageId).ThenBy(record => record.Id).ToList()
                };
                XmlSerializer serializer = new XmlSerializer(typeof(TranslationCacheDocument));
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

        public void Put(TranslationEntry entry, string provider)
        {
            TranslationCacheRecord record = TranslationCacheRecord.FromEntry(entry, provider);
            records[Key(record.Id, record.SourceHash, provider)] = record;
        }

        public bool TryUpdate(string id, string sourceHash, string provider, string translatedText, out TranslationCacheRecord updated)
        {
            if (records.TryGetValue(Key(id, sourceHash, provider), out updated))
            {
                updated.TranslatedText = translatedText ?? string.Empty;
                updated.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
                return true;
            }

            return false;
        }

        private static string Key(string id, string sourceHash, string provider)
        {
            return (id ?? string.Empty) + "|" + (sourceHash ?? string.Empty) + "|" + (provider ?? string.Empty);
        }
    }
}
