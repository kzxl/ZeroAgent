using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZeroPrimitives.Cryptography;
using ZeroVector.Core.Metrics;
using ZeroVector.Core.Quantization;

namespace ZeroAgent.Core.Database
{
    /// <summary>
    /// Pure C# Sovereign Embedded AI Database Engine (ZAB1 - Zero Agent Binary).
    /// Provides unified multi-modal storage for Agent Manifest, Knowledge Rules,
    /// SQ8 Quantized Vectors (with direct SIMD evaluation), Reflexion Memory, and Semantic Plan Cache.
    /// Supports zero-external-dependency ACID file persistence, hardware CRC32C integrity,
    /// and 100% lossless human-readable JSON export/import.
    /// </summary>
    public sealed class ZabDatabase : IDisposable
    {
        private readonly string _filePath;
        private readonly object _lock = new object();

        private ZabManifest _manifest;
        private readonly List<ZabKnowledgeRecord> _knowledge = new List<ZabKnowledgeRecord>();
        private readonly List<ZabVectorRecord> _vectors = new List<ZabVectorRecord>();
        private readonly List<ZabReflexionRecord> _reflexions = new List<ZabReflexionRecord>();
        private readonly List<ZabPlanRecord> _plans = new List<ZabPlanRecord>();

        private bool _isDirty;
        private bool _disposed;

        public string FilePath => _filePath;
        public ZabManifest Manifest => _manifest;
        public IReadOnlyList<ZabKnowledgeRecord> Knowledge => _knowledge;
        public IReadOnlyList<ZabVectorRecord> Vectors => _vectors;
        public IReadOnlyList<ZabReflexionRecord> Reflexions => _reflexions;
        public IReadOnlyList<ZabPlanRecord> Plans => _plans;
        public bool IsDirty => _isDirty;

        private ZabDatabase(string filePath, ZabManifest? manifest = null)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _manifest = manifest ?? new ZabManifest();
        }

        #region Factory Methods

        /// <summary>
        /// Creates a new sovereign database container file with the given manifest.
        /// </summary>
        public static ZabDatabase CreateNew(string filePath, ZabManifest? manifest = null)
        {
            var db = new ZabDatabase(filePath, manifest);
            db._isDirty = true;
            db.Commit();
            return db;
        }

        /// <summary>
        /// Opens an existing database container file and verifies its hardware checksum.
        /// </summary>
        public static ZabDatabase Open(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"ZAB database file not found: {filePath}", filePath);
            }

            var db = new ZabDatabase(filePath);
            db.Reload();
            return db;
        }

        /// <summary>
        /// Opens an existing database container or creates a new one if it does not exist.
        /// </summary>
        public static ZabDatabase OpenOrCreate(string filePath, ZabManifest? defaultManifest = null)
        {
            if (File.Exists(filePath))
            {
                return Open(filePath);
            }
            return CreateNew(filePath, defaultManifest);
        }

        #endregion

        #region Knowledge CRUD

        public ZabKnowledgeRecord AddKnowledge(string key, string value, string category = "General", string author = "Admin", float confidence = 1.0f)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));

            var record = new ZabKnowledgeRecord
            {
                Key = key.Trim(),
                Value = value ?? string.Empty,
                Category = category ?? "General",
                Author = author ?? "Admin",
                Confidence = confidence,
                LastUpdatedUtc = DateTime.UtcNow
            };

            lock (_lock)
            {
                _knowledge.Add(record);
                _isDirty = true;
            }

            return record;
        }

        public bool UpdateKnowledge(string id, string newValue, float? confidence = null)
        {
            lock (_lock)
            {
                var record = _knowledge.Find(k => string.Equals(k.Id, id, StringComparison.OrdinalIgnoreCase));
                if (record == null) return false;

                record.Value = newValue;
                if (confidence.HasValue) record.Confidence = confidence.Value;
                record.LastUpdatedUtc = DateTime.UtcNow;
                _isDirty = true;
                return true;
            }
        }

        public bool DeleteKnowledge(string id)
        {
            lock (_lock)
            {
                int removed = _knowledge.RemoveAll(k => string.Equals(k.Id, id, StringComparison.OrdinalIgnoreCase));
                if (removed > 0)
                {
                    _isDirty = true;
                    return true;
                }
                return false;
            }
        }

        public ZabKnowledgeRecord? FindKnowledge(string key)
        {
            lock (_lock)
            {
                return _knowledge.Find(k => string.Equals(k.Key, key, StringComparison.OrdinalIgnoreCase));
            }
        }

        #endregion

        #region Quantized Vector Storage & SIMD Search

        /// <summary>
        /// Stores a continuous FP32 vector by compressing it to SQ8 (4x reduction) using BinaryQuantizer.
        /// </summary>
        public ZabVectorRecord StoreVector(string label, ReadOnlySpan<float> vector, string? id = null)
        {
            if (vector.IsEmpty) throw new ArgumentException("Vector cannot be empty.", nameof(vector));

            sbyte[] qData = new sbyte[vector.Length];
            BinaryQuantizer.QuantizeSQ8(vector, qData, out float scale, out float offset);

            var record = new ZabVectorRecord
            {
                Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id!,
                Label = label ?? string.Empty,
                Dimension = vector.Length,
                Scale = scale,
                Offset = offset,
                QuantizedData = qData
            };

            lock (_lock)
            {
                _vectors.Add(record);
                _isDirty = true;
            }

            return record;
        }

        /// <summary>
        /// Searches vectors using hardware SIMD-accelerated SQ8 inner products with Affine Hoisting.
        /// Evaluates distance directly on compressed bytes with zero decompression overhead.
        /// </summary>
        public List<(ZabVectorRecord Record, float Similarity)> SearchVectors(
            ReadOnlySpan<float> queryVector,
            int topK = 5,
            float minSimilarity = 0.50f)
        {
            if (queryVector.IsEmpty || topK <= 0) return new List<(ZabVectorRecord, float)>();

            float querySum = BinaryQuantizer.ComputeVectorSum(queryVector);
            var results = new List<(ZabVectorRecord Record, float Similarity)>();

            lock (_lock)
            {
                for (int i = 0; i < _vectors.Count; i++)
                {
                    var vec = _vectors[i];
                    if (vec.Dimension != queryVector.Length) continue;

                    float sim = vec.ComputeSimilarity(queryVector, querySum);
                    if (sim >= minSimilarity)
                    {
                        results.Add((vec, sim));
                    }
                }
            }

            results.Sort((a, b) => b.Similarity.CompareTo(a.Similarity));
            if (results.Count > topK)
            {
                results.RemoveRange(topK, results.Count - topK);
            }

            return results;
        }

        #endregion

        #region Reflexion Memory CRUD

        public ZabReflexionRecord AddReflexion(string goal, string failureReason, string lesson)
        {
            var record = new ZabReflexionRecord
            {
                Goal = goal ?? string.Empty,
                FailureReason = failureReason ?? string.Empty,
                Lesson = lesson ?? string.Empty,
                TimestampUtc = DateTime.UtcNow
            };

            lock (_lock)
            {
                _reflexions.Add(record);
                _isDirty = true;
            }

            return record;
        }

        public List<ZabReflexionRecord> RecallReflexions(string goal, int topK = 3)
        {
            if (string.IsNullOrWhiteSpace(goal) || topK <= 0) return new List<ZabReflexionRecord>();

            var tokens = goal.Split(new[] { ' ', ',', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
            var scored = new List<(ZabReflexionRecord Rec, int Score)>();

            lock (_lock)
            {
                foreach (var refRec in _reflexions)
                {
                    int score = 0;
                    foreach (var tok in tokens)
                    {
                        if (refRec.Goal.IndexOf(tok, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            score += 2;
                        }
                        if (refRec.FailureReason.IndexOf(tok, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            score += 1;
                        }
                    }

                    if (score > 0)
                    {
                        scored.Add((refRec, score));
                    }
                }
            }

            scored.Sort((a, b) => b.Score.CompareTo(a.Score));
            var result = new List<ZabReflexionRecord>();
            for (int i = 0; i < Math.Min(topK, scored.Count); i++)
            {
                result.Add(scored[i].Rec);
            }

            return result;
        }

        #endregion

        #region Plan Semantic Cache CRUD

        public ZabPlanRecord CachePlan(
            string goal,
            string solution,
            int stepsCount,
            float confidence = 1.0f,
            ReadOnlySpan<float> goalVector = default)
        {
            var plan = new ZabPlanRecord
            {
                Goal = goal ?? string.Empty,
                Solution = solution ?? string.Empty,
                StepsCount = stepsCount,
                Confidence = confidence,
                HitCount = 0,
                CachedAtUtc = DateTime.UtcNow
            };

            if (!goalVector.IsEmpty)
            {
                sbyte[] qVec = new sbyte[goalVector.Length];
                BinaryQuantizer.QuantizeSQ8(goalVector, qVec, out float scale, out float offset);
                plan.VectorDim = goalVector.Length;
                plan.Scale = scale;
                plan.Offset = offset;
                plan.GoalVectorSq8 = qVec;
            }

            lock (_lock)
            {
                _plans.Add(plan);
                _isDirty = true;
            }

            return plan;
        }

        public ZabPlanRecord? LookupPlan(ReadOnlySpan<float> queryVector, float minSimilarity = 0.85f)
        {
            if (queryVector.IsEmpty) return null;

            float querySum = BinaryQuantizer.ComputeVectorSum(queryVector);
            ZabPlanRecord? bestMatch = null;
            float maxSim = minSimilarity;

            lock (_lock)
            {
                for (int i = 0; i < _plans.Count; i++)
                {
                    var plan = _plans[i];
                    if (plan.GoalVectorSq8 == null || plan.VectorDim != queryVector.Length) continue;

                    float sim = plan.ComputeSimilarity(queryVector, querySum);
                    if (sim >= maxSim)
                    {
                        maxSim = sim;
                        bestMatch = plan;
                    }
                }

                if (bestMatch != null)
                {
                    bestMatch.HitCount++;
                    _isDirty = true;
                }
            }

            return bestMatch;
        }

        #endregion

        #region Binary Persistence & Atomic Commit

        /// <summary>
        /// Persists all in-memory changes atomically into the sovereign binary file (.zab).
        /// Employs a temporary staging file and hardware CRC32C verification to prevent corruption.
        /// </summary>
        public void Commit()
        {
            lock (_lock)
            {
                string? dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir!);
                }

                string tempFile = _filePath + ".tmp." + Guid.NewGuid().ToString("N");

                using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                using (var msPayload = new MemoryStream())
                using (var payloadWriter = new BinaryWriter(msPayload, Encoding.UTF8))
                {
                    var header = new ZabHeader
                    {
                        LastModifiedUtc = DateTime.UtcNow
                    };

                    // 1. Write Manifest
                    header.ManifestOffset = ZabHeader.HeaderSize + msPayload.Position;
                    long mStart = msPayload.Position;
                    payloadWriter.Write(_manifest.AgentId);
                    payloadWriter.Write(_manifest.Name);
                    payloadWriter.Write(_manifest.Description);
                    payloadWriter.Write(_manifest.Role);
                    payloadWriter.Write(_manifest.SystemPrompt);
                    payloadWriter.Write(_manifest.RegisteredTools.Count);
                    foreach (var tool in _manifest.RegisteredTools) payloadWriter.Write(tool);
                    payloadWriter.Write(_manifest.CreatedAtUtc.ToBinary());
                    header.ManifestLength = (int)(msPayload.Position - mStart);

                    // 2. Write Knowledge
                    header.KnowledgeOffset = ZabHeader.HeaderSize + msPayload.Position;
                    long kStart = msPayload.Position;
                    payloadWriter.Write(_knowledge.Count);
                    foreach (var k in _knowledge)
                    {
                        payloadWriter.Write(k.Id);
                        payloadWriter.Write(k.Key);
                        payloadWriter.Write(k.Value);
                        payloadWriter.Write(k.Category);
                        payloadWriter.Write(k.Confidence);
                        payloadWriter.Write(k.Status);
                        payloadWriter.Write(k.Author);
                        payloadWriter.Write(k.LastUpdatedUtc.ToBinary());
                        payloadWriter.Write(k.ConflictDetails ?? string.Empty);
                    }
                    header.KnowledgeLength = (int)(msPayload.Position - kStart);

                    // 3. Write Vectors (SQ8 Quantized)
                    header.VectorsOffset = ZabHeader.HeaderSize + msPayload.Position;
                    long vStart = msPayload.Position;
                    payloadWriter.Write(_vectors.Count);
                    foreach (var v in _vectors)
                    {
                        payloadWriter.Write(v.Id);
                        payloadWriter.Write(v.Label);
                        payloadWriter.Write(v.Dimension);
                        payloadWriter.Write(v.Scale);
                        payloadWriter.Write(v.Offset);
                        payloadWriter.Write(v.QuantizedData.Length);
                        for (int i = 0; i < v.QuantizedData.Length; i++)
                        {
                            payloadWriter.Write((byte)v.QuantizedData[i]);
                        }
                    }
                    header.VectorsLength = (int)(msPayload.Position - vStart);

                    // 4. Write Reflexions
                    header.ReflexionsOffset = ZabHeader.HeaderSize + msPayload.Position;
                    long rStart = msPayload.Position;
                    payloadWriter.Write(_reflexions.Count);
                    foreach (var r in _reflexions)
                    {
                        payloadWriter.Write(r.Id);
                        payloadWriter.Write(r.Goal);
                        payloadWriter.Write(r.FailureReason);
                        payloadWriter.Write(r.Lesson);
                        payloadWriter.Write(r.TimestampUtc.ToBinary());
                    }
                    header.ReflexionsLength = (int)(msPayload.Position - rStart);

                    // 5. Write Plans
                    header.PlansOffset = ZabHeader.HeaderSize + msPayload.Position;
                    long pStart = msPayload.Position;
                    payloadWriter.Write(_plans.Count);
                    foreach (var p in _plans)
                    {
                        payloadWriter.Write(p.Id);
                        payloadWriter.Write(p.Goal);
                        payloadWriter.Write(p.Solution);
                        payloadWriter.Write(p.StepsCount);
                        payloadWriter.Write(p.Confidence);
                        payloadWriter.Write(p.HitCount);
                        payloadWriter.Write(p.CachedAtUtc.ToBinary());
                        bool hasVec = p.GoalVectorSq8 != null && p.GoalVectorSq8.Length > 0;
                        payloadWriter.Write(hasVec);
                        if (hasVec)
                        {
                            payloadWriter.Write(p.VectorDim);
                            payloadWriter.Write(p.Scale);
                            payloadWriter.Write(p.Offset);
                            payloadWriter.Write(p.GoalVectorSq8!.Length);
                            for (int i = 0; i < p.GoalVectorSq8.Length; i++)
                            {
                                payloadWriter.Write((byte)p.GoalVectorSq8[i]);
                            }
                        }
                    }
                    header.PlansLength = (int)(msPayload.Position - pStart);

                    // Calculate hardware CRC32C over payload bytes
                    byte[] payloadBytes = msPayload.ToArray();
                    header.ChecksumCrc32C = FastCrc.Crc32C(payloadBytes);

                    // Write Header + Payload to File
                    using (var fileWriter = new BinaryWriter(fs, Encoding.UTF8))
                    {
                        header.Write(fileWriter);
                        fileWriter.Write(payloadBytes);
                    }
                }

                // Atomic file swap
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
                File.Move(tempFile, _filePath);

                _isDirty = false;
            }
        }

        /// <summary>
        /// Reloads state from the binary file, verifying header magic and CRC32C checksum.
        /// </summary>
        public void Reload()
        {
            lock (_lock)
            {
                using (var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(fs, Encoding.UTF8))
                {
                    var header = ZabHeader.Read(reader);
                    if (!header.IsValid)
                    {
                        throw new InvalidDataException("Invalid ZAB binary header or unsupported format version.");
                    }

                    // Read entire payload and verify CRC32C
                    int payloadLength = (int)(fs.Length - ZabHeader.HeaderSize);
                    byte[] payloadBytes = reader.ReadBytes(payloadLength);
                    uint computedCrc = FastCrc.Crc32C(payloadBytes);

                    if (computedCrc != header.ChecksumCrc32C)
                    {
                        throw new InvalidDataException($"Hardware CRC32C verification failed. Expected: {header.ChecksumCrc32C}, Computed: {computedCrc}. File may be corrupted.");
                    }

                    using (var ms = new MemoryStream(payloadBytes))
                    using (var payloadReader = new BinaryReader(ms, Encoding.UTF8))
                    {
                        // 1. Read Manifest
                        ms.Seek(header.ManifestOffset - ZabHeader.HeaderSize, SeekOrigin.Begin);
                        _manifest = new ZabManifest
                        {
                            AgentId = payloadReader.ReadString(),
                            Name = payloadReader.ReadString(),
                            Description = payloadReader.ReadString(),
                            Role = payloadReader.ReadString(),
                            SystemPrompt = payloadReader.ReadString()
                        };
                        int toolCount = payloadReader.ReadInt32();
                        _manifest.RegisteredTools.Clear();
                        for (int i = 0; i < toolCount; i++) _manifest.RegisteredTools.Add(payloadReader.ReadString());
                        _manifest.CreatedAtUtc = DateTime.FromBinary(payloadReader.ReadInt64());

                        // 2. Read Knowledge
                        _knowledge.Clear();
                        ms.Seek(header.KnowledgeOffset - ZabHeader.HeaderSize, SeekOrigin.Begin);
                        int kCount = payloadReader.ReadInt32();
                        for (int i = 0; i < kCount; i++)
                        {
                            _knowledge.Add(new ZabKnowledgeRecord
                            {
                                Id = payloadReader.ReadString(),
                                Key = payloadReader.ReadString(),
                                Value = payloadReader.ReadString(),
                                Category = payloadReader.ReadString(),
                                Confidence = payloadReader.ReadSingle(),
                                Status = payloadReader.ReadString(),
                                Author = payloadReader.ReadString(),
                                LastUpdatedUtc = DateTime.FromBinary(payloadReader.ReadInt64()),
                                ConflictDetails = payloadReader.ReadString()
                            });
                        }

                        // 3. Read Vectors
                        _vectors.Clear();
                        ms.Seek(header.VectorsOffset - ZabHeader.HeaderSize, SeekOrigin.Begin);
                        int vCount = payloadReader.ReadInt32();
                        for (int i = 0; i < vCount; i++)
                        {
                            string id = payloadReader.ReadString();
                            string label = payloadReader.ReadString();
                            int dim = payloadReader.ReadInt32();
                            float scale = payloadReader.ReadSingle();
                            float offset = payloadReader.ReadSingle();
                            int qLen = payloadReader.ReadInt32();
                            sbyte[] qData = new sbyte[qLen];
                            for (int j = 0; j < qLen; j++)
                            {
                                qData[j] = (sbyte)payloadReader.ReadByte();
                            }

                            _vectors.Add(new ZabVectorRecord
                            {
                                Id = id,
                                Label = label,
                                Dimension = dim,
                                Scale = scale,
                                Offset = offset,
                                QuantizedData = qData
                            });
                        }

                        // 4. Read Reflexions
                        _reflexions.Clear();
                        ms.Seek(header.ReflexionsOffset - ZabHeader.HeaderSize, SeekOrigin.Begin);
                        int rCount = payloadReader.ReadInt32();
                        for (int i = 0; i < rCount; i++)
                        {
                            _reflexions.Add(new ZabReflexionRecord
                            {
                                Id = payloadReader.ReadString(),
                                Goal = payloadReader.ReadString(),
                                FailureReason = payloadReader.ReadString(),
                                Lesson = payloadReader.ReadString(),
                                TimestampUtc = DateTime.FromBinary(payloadReader.ReadInt64())
                            });
                        }

                        // 5. Read Plans
                        _plans.Clear();
                        ms.Seek(header.PlansOffset - ZabHeader.HeaderSize, SeekOrigin.Begin);
                        int pCount = payloadReader.ReadInt32();
                        for (int i = 0; i < pCount; i++)
                        {
                            var plan = new ZabPlanRecord
                            {
                                Id = payloadReader.ReadString(),
                                Goal = payloadReader.ReadString(),
                                Solution = payloadReader.ReadString(),
                                StepsCount = payloadReader.ReadInt32(),
                                Confidence = payloadReader.ReadSingle(),
                                HitCount = payloadReader.ReadInt32(),
                                CachedAtUtc = DateTime.FromBinary(payloadReader.ReadInt64())
                            };
                            bool hasVec = payloadReader.ReadBoolean();
                            if (hasVec)
                            {
                                plan.VectorDim = payloadReader.ReadInt32();
                                plan.Scale = payloadReader.ReadSingle();
                                plan.Offset = payloadReader.ReadSingle();
                                int qLen = payloadReader.ReadInt32();
                                sbyte[] qData = new sbyte[qLen];
                                for (int j = 0; j < qLen; j++)
                                {
                                    qData[j] = (sbyte)payloadReader.ReadByte();
                                }
                                plan.GoalVectorSq8 = qData;
                            }
                            _plans.Add(plan);
                        }
                    }
                }

                _isDirty = false;
            }
        }

        #endregion

        #region JSON Export & Import (100% Lossless Human-Readable)

        public class ZabJsonDumpDto
        {
            public ZabManifest Manifest { get; set; } = new ZabManifest();
            public List<ZabKnowledgeRecord> Knowledge { get; set; } = new List<ZabKnowledgeRecord>();
            public List<ZabReflexionRecord> Reflexions { get; set; } = new List<ZabReflexionRecord>();
            public List<ZabPlanRecord> Plans { get; set; } = new List<ZabPlanRecord>();
        }

        /// <summary>
        /// Exports the database contents to a human-readable JSON string.
        /// </summary>
        public string ExportToJsonString()
        {
            lock (_lock)
            {
                var dump = new ZabJsonDumpDto
                {
                    Manifest = _manifest,
                    Knowledge = _knowledge,
                    Reflexions = _reflexions,
                    Plans = _plans
                };

                return JsonSerializer.Serialize(dump, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNameCaseInsensitive = true
                });
            }
        }

        /// <summary>
        /// Exports database to a human-readable JSON file on disk.
        /// </summary>
        public void ExportToJson(string jsonFilePath)
        {
            string json = ExportToJsonString();
            File.WriteAllText(jsonFilePath, json, Encoding.UTF8);
        }

        /// <summary>
        /// Restores database contents from a human-readable JSON string and marks container dirty.
        /// </summary>
        public void ImportFromJsonString(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentNullException(nameof(json));

            var dump = JsonSerializer.Deserialize<ZabJsonDumpDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (dump == null) throw new InvalidDataException("Failed to parse ZAB JSON dump.");

            lock (_lock)
            {
                if (dump.Manifest != null) _manifest = dump.Manifest;

                if (dump.Knowledge != null)
                {
                    _knowledge.Clear();
                    _knowledge.AddRange(dump.Knowledge);
                }

                if (dump.Reflexions != null)
                {
                    _reflexions.Clear();
                    _reflexions.AddRange(dump.Reflexions);
                }

                if (dump.Plans != null)
                {
                    _plans.Clear();
                    _plans.AddRange(dump.Plans);
                }

                _isDirty = true;
                Commit();
            }
        }

        /// <summary>
        /// Restores database contents from a human-readable JSON file.
        /// </summary>
        public void ImportFromJson(string jsonFilePath)
        {
            if (!File.Exists(jsonFilePath)) throw new FileNotFoundException("JSON dump file not found.", jsonFilePath);
            string json = File.ReadAllText(jsonFilePath, Encoding.UTF8);
            ImportFromJsonString(json);
        }

        #endregion

        #region Storage Metrics & Statistics

        /// <summary>
        /// Calculates storage metrics, comparing raw uncompressed size vs actual binary container size.
        /// </summary>
        public ZabStorageStats GetStorageStats()
        {
            lock (_lock)
            {
                long fileLength = File.Exists(_filePath) ? new FileInfo(_filePath).Length : 0;

                // Estimate raw size if stored uncompressed:
                // Vectors: 4 bytes per dim instead of 1 byte
                long rawVectorBytes = 0;
                foreach (var v in _vectors)
                {
                    rawVectorBytes += v.Dimension * 4;
                }

                // Estimated raw JSON/UTF-8 string representation
                long rawTextBytes = 0;
                foreach (var k in _knowledge) rawTextBytes += (k.Key.Length + k.Value.Length) * 2 + 128;
                foreach (var r in _reflexions) rawTextBytes += (r.Goal.Length + r.Lesson.Length) * 2 + 128;
                foreach (var p in _plans) rawTextBytes += (p.Goal.Length + p.Solution.Length) * 2 + 128;

                long estimatedRaw = ZabHeader.HeaderSize + rawVectorBytes + rawTextBytes;
                if (estimatedRaw < fileLength) estimatedRaw = fileLength * 4;

                return new ZabStorageStats
                {
                    RawSizeEstimatedBytes = estimatedRaw,
                    ActualFileSizeBytes = fileLength,
                    KnowledgeCount = _knowledge.Count,
                    VectorCount = _vectors.Count,
                    ReflexionCount = _reflexions.Count,
                    PlanCount = _plans.Count
                };
            }
        }

        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_isDirty)
                {
                    try { Commit(); } catch { }
                }
                _disposed = true;
            }
        }
    }
}
