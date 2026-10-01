using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using ZeroAgent.Core.Database;
using ZeroPrimitives.Cryptography;
using ZeroVector.Core.Metrics;
using ZeroVector.Core.Quantization;

namespace ZeroAgent.Tests
{
    public class ZabDatabaseTests : IDisposable
    {
        private readonly string _tempDir;

        public ZabDatabaseTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "ZabTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch { }
        }

        [Fact]
        public void ZabDatabase_CreatesAndReopensWithHeaderIntegrity()
        {
            string dbPath = Path.Combine(_tempDir, "agent_qc.zab");

            var manifest = new ZabManifest
            {
                AgentId = "qc_agent_001",
                Name = "MDS Inspection Assistant",
                Role = "QualityController",
                SystemPrompt = "Analyze telemetry and guide machine repairs.",
                RegisteredTools = new List<string> { "query_spindle", "calibrate_valve" }
            };

            using (var db = ZabDatabase.CreateNew(dbPath, manifest))
            {
                Assert.NotNull(db);
                Assert.True(File.Exists(dbPath));
                Assert.Equal("qc_agent_001", db.Manifest.AgentId);
                Assert.Equal("MDS Inspection Assistant", db.Manifest.Name);
            }

            // Re-open from disk in a fresh instance
            using (var dbReopened = ZabDatabase.Open(dbPath))
            {
                Assert.Equal("qc_agent_001", dbReopened.Manifest.AgentId);
                Assert.Equal("MDS Inspection Assistant", dbReopened.Manifest.Name);
                Assert.Equal(2, dbReopened.Manifest.RegisteredTools.Count);
                Assert.Contains("query_spindle", dbReopened.Manifest.RegisteredTools);
            }
        }

        [Fact]
        public void ZabDatabase_KnowledgeCRUD_PersistsAndMutatesInPlace()
        {
            string dbPath = Path.Combine(_tempDir, "knowledge_test.zab");

            using (var db = ZabDatabase.CreateNew(dbPath))
            {
                // Add Knowledge rules
                var rule1 = db.AddKnowledge("spindle", "TRUC_CHINH_CNC", "Hardware", author: "Engineer_Nam");
                var rule2 = db.AddKnowledge("plc_port", "502", "Network", author: "Admin");
                db.Commit();

                Assert.Equal(2, db.Knowledge.Count);
                Assert.Equal("TRUC_CHINH_CNC", rule1.Value);

                // Update rule 1
                bool updated = db.UpdateKnowledge(rule1.Id, "TRUC_CHINH_CNC_HIWIN", confidence: 0.99f);
                Assert.True(updated);

                // Delete rule 2
                bool deleted = db.DeleteKnowledge(rule2.Id);
                Assert.True(deleted);

                db.Commit();
            }

            // Re-open and verify persistence of mutations
            using (var dbReopened = ZabDatabase.Open(dbPath))
            {
                Assert.Single(dbReopened.Knowledge);
                var rule = dbReopened.FindKnowledge("spindle");
                Assert.NotNull(rule);
                Assert.Equal("TRUC_CHINH_CNC_HIWIN", rule.Value);
                Assert.Equal(0.99f, rule.Confidence);

                var missingRule = dbReopened.FindKnowledge("plc_port");
                Assert.Null(missingRule);
            }
        }

        [Fact]
        public void ZabDatabase_StoresSQ8Vectors_AndPerformsSIMDSearch()
        {
            string dbPath = Path.Combine(_tempDir, "vectors_test.zab");

            int dim = 64;
            float[] vecSpindle = new float[dim];
            float[] vecBearing = new float[dim];
            float[] vecSalary = new float[dim];

            // Setup synthetic orthogonal/similar vectors
            for (int i = 0; i < dim; i++)
            {
                vecSpindle[i] = (float)Math.Sin(i * 0.1);
                vecBearing[i] = (float)Math.Sin(i * 0.1 + 0.05); // Very close to spindle
                vecSalary[i] = (float)Math.Cos(i * 0.5);          // Far from spindle
            }

            VectorMetrics.NormalizeL2(vecSpindle);
            VectorMetrics.NormalizeL2(vecBearing);
            VectorMetrics.NormalizeL2(vecSalary);

            using (var db = ZabDatabase.CreateNew(dbPath))
            {
                var recSpindle = db.StoreVector("Spindle Temperature Inspection", vecSpindle);
                var recBearing = db.StoreVector("Bearing Vibration Analysis", vecBearing);
                var recSalary = db.StoreVector("Employee Payroll and Bonus", vecSalary);
                db.Commit();

                // Assert SQ8 compression: each float32 is compacted to a 1-byte sbyte
                Assert.Equal(dim, recSpindle.QuantizedData.Length);

                // Verify Dequantize restores continuous floats
                float[] restored = recSpindle.Dequantize();
                Assert.Equal(dim, restored.Length);
                float cosineSelf = VectorMetrics.CosineSimilarity(vecSpindle, restored);
                Assert.True(cosineSelf >= 0.99f, $"Expected close dequantization similarity, got {cosineSelf}");

                // Perform SIMD vector search using uncompressed query
                var matches = db.SearchVectors(vecSpindle, topK: 2, minSimilarity: 0.80f);
                Assert.True(matches.Count >= 1);
                Assert.Equal("Spindle Temperature Inspection", matches[0].Record.Label);
                Assert.True(matches[0].Similarity >= 0.95f);
            }

            // Re-open and verify vector persistence
            using (var dbReopened = ZabDatabase.Open(dbPath))
            {
                Assert.Equal(3, dbReopened.Vectors.Count);
                var matches = dbReopened.SearchVectors(vecBearing, topK: 1, minSimilarity: 0.80f);
                Assert.Single(matches);
                Assert.Equal("Bearing Vibration Analysis", matches[0].Record.Label);
            }
        }

        [Fact]
        public void ZabDatabase_ReflexionAndPlanCache_StoreAndLookup()
        {
            string dbPath = Path.Combine(_tempDir, "cognitive_test.zab");

            int dim = 32;
            float[] queryVec = new float[dim];
            for (int i = 0; i < dim; i++) queryVec[i] = (float)Math.Sin(i * 0.2);
            VectorMetrics.NormalizeL2(queryVec);

            using (var db = ZabDatabase.CreateNew(dbPath))
            {
                // Reflexion Episode
                db.AddReflexion(
                    goal: "Doc sensor ap suat khi nen",
                    failureReason: "Ket noi cong COM3 bi timeout",
                    lesson: "Kiem tra baudrate 9600 va cong COM4");

                // Plan Cache
                db.CachePlan(
                    goal: "Quy trinh do ap suat",
                    solution: "Step 1: Check COM4. Step 2: Read Modbus reg 30001.",
                    stepsCount: 2,
                    confidence: 0.98f,
                    goalVector: queryVec);

                db.Commit();
            }

            // Re-open and test recall
            using (var dbReopened = ZabDatabase.Open(dbPath))
            {
                Assert.Single(dbReopened.Reflexions);
                var recalled = dbReopened.RecallReflexions("ap suat sensor khi nen", topK: 1);
                Assert.Single(recalled);
                Assert.Contains("COM4", recalled[0].Lesson);

                // Semantic plan lookup
                Assert.Single(dbReopened.Plans);
                var plan = dbReopened.LookupPlan(queryVec, minSimilarity: 0.90f);
                Assert.NotNull(plan);
                Assert.Contains("Modbus reg 30001", plan.Solution);
                Assert.Equal(1, plan.HitCount);
            }
        }

        [Fact]
        public void ZabDatabase_DetectsHardwareCRC32CCorruption()
        {
            string dbPath = Path.Combine(_tempDir, "crc_test.zab");

            using (var db = ZabDatabase.CreateNew(dbPath))
            {
                db.AddKnowledge("machine_id", "CNC_MILL_01");
                db.Commit();
            }

            // Tamper with payload byte in the file
            byte[] fileBytes = File.ReadAllBytes(dbPath);
            Assert.True(fileBytes.Length > ZabHeader.HeaderSize);

            // Invert bits in payload area
            fileBytes[ZabHeader.HeaderSize + 2] ^= 0xFF;
            File.WriteAllBytes(dbPath, fileBytes);

            // Opening corrupted file must throw InvalidDataException
            var ex = Assert.Throws<InvalidDataException>(() => ZabDatabase.Open(dbPath));
            Assert.Contains("CRC32C verification failed", ex.Message);
        }

        [Fact]
        public void ZabDatabase_ExportAndImportJson_LosslessRoundTrip()
        {
            string dbPath = Path.Combine(_tempDir, "json_roundtrip.zab");
            string jsonPath = Path.Combine(_tempDir, "dump.json");

            using (var db = ZabDatabase.CreateNew(dbPath))
            {
                db.AddKnowledge("bearing", "VONG_BI_6205", "Mechanical");
                db.AddReflexion("Thay the vong bi", "Chua ngat dien", "Phai ngat cau dao truoc khi thao vo");
                db.Commit();

                // Export to JSON
                db.ExportToJson(jsonPath);
                Assert.True(File.Exists(jsonPath));
            }

            // Inspect and edit the JSON externally (simulating human curation)
            string jsonText = File.ReadAllText(jsonPath);
            Assert.Contains("VONG_BI_6205", jsonText);
            Assert.Contains("Chua ngat dien", jsonText);

            string modifiedJson = jsonText.Replace("VONG_BI_6205", "VONG_BI_SKF_6205_2RS");
            File.WriteAllText(jsonPath, modifiedJson);

            // Import back into a new database container
            string restoredDbPath = Path.Combine(_tempDir, "restored.zab");
            using (var restoredDb = ZabDatabase.CreateNew(restoredDbPath))
            {
                restoredDb.ImportFromJson(jsonPath);
            }

            // Verify the binary database container now has the updated rule
            using (var dbReopened = ZabDatabase.Open(restoredDbPath))
            {
                var rule = dbReopened.FindKnowledge("bearing");
                Assert.NotNull(rule);
                Assert.Equal("VONG_BI_SKF_6205_2RS", rule.Value);
                Assert.Single(dbReopened.Reflexions);
            }
        }

        [Fact]
        public void ZabDatabase_StorageStats_ReportsCompressionMetrics()
        {
            string dbPath = Path.Combine(_tempDir, "stats_test.zab");

            using (var db = ZabDatabase.CreateNew(dbPath))
            {
                for (int i = 0; i < 50; i++)
                {
                    db.AddKnowledge($"sensor_{i}", $"Telemetry parameter sensor index {i} in line A");
                }

                int dim = 128;
                float[] dummyVec = new float[dim];
                for (int i = 0; i < 20; i++)
                {
                    db.StoreVector($"Vector_{i}", dummyVec);
                }

                db.Commit();

                var stats = db.GetStorageStats();
                Assert.Equal(50, stats.KnowledgeCount);
                Assert.Equal(20, stats.VectorCount);
                Assert.True(stats.ActualFileSizeBytes > 0);
                Assert.True(stats.CompressionRatio >= 1.0);
            }
        }

        [Fact]
        public void ZabDatabase_WalAppendAndReplay_RecoversUncheckpointedMutations()
        {
            string dbPath = Path.Combine(_tempDir, "wal_recovery_test.zab");
            string walPath = dbPath + "-wal";

            // 1. Create database and baseline snapshot
            using (var db = ZabDatabase.CreateNew(dbPath))
            {
                db.AddKnowledge("init_key", "init_val");
                db.Commit(); // Wal is now 0 bytes
            }

            // 2. Add mutations to WAL without calling Commit()
            using (var db = ZabDatabase.Open(dbPath))
            {
                db.Wal.AutoCheckpointThreshold = 10000; // Prevent auto-checkpoint
                db.AutoCheckpointOnDispose = false;     // Simulate sudden process crash / power cut
                db.AddKnowledge("wal_key_1", "wal_val_1");
                db.AddKnowledge("wal_key_2", "wal_val_2");
            }

            Assert.True(File.Exists(walPath));
            Assert.True(new FileInfo(walPath).Length > 0);

            // 3. Re-open and verify WAL replay restores uncheckpointed mutations
            using (var dbReopened = ZabDatabase.Open(dbPath))
            {
                Assert.Equal(3, dbReopened.Knowledge.Count);
                Assert.NotNull(dbReopened.FindKnowledge("init_key"));
                Assert.NotNull(dbReopened.FindKnowledge("wal_key_1"));
                Assert.NotNull(dbReopened.FindKnowledge("wal_key_2"));
                Assert.Equal("wal_val_2", dbReopened.FindKnowledge("wal_key_2")!.Value);
            }
        }

        [Fact]
        public void ZabDatabase_WalTornWrite_TruncatesCorruptedTailAndRecoversValidFrames()
        {
            string dbPath = Path.Combine(_tempDir, "torn_write_test.zab");
            string walPath = dbPath + "-wal";

            using (var dbInit = ZabDatabase.CreateNew(dbPath))
            {
                // Baseline snapshot created
            }

            using (var db = ZabDatabase.Open(dbPath))
            {
                db.Wal.AutoCheckpointThreshold = 10000;
                db.AutoCheckpointOnDispose = false; // Simulate sudden crash mid-execution
                db.AddKnowledge("valid_1", "val_1");
                db.AddKnowledge("valid_2", "val_2");
            }

            Assert.True(File.Exists(walPath));
            long validWalLength = new FileInfo(walPath).Length;
            Assert.True(validWalLength > 0);

            // Simulate sudden power outage during 3rd write -> append 15 bytes of truncated corrupted garbage
            byte[] garbage = new byte[] { 0x5A, 0x57, 0x41, 0x4C, 0x01, 0xFF, 0x00, 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0 };
            using (var fs = new FileStream(walPath, FileMode.Append, FileAccess.Write))
            {
                fs.Write(garbage, 0, garbage.Length);
            }

            Assert.Equal(validWalLength + garbage.Length, new FileInfo(walPath).Length);

            // Re-open: engine must detect torn write, truncate garbage back to validWalLength, and restore valid_1 & valid_2
            using (var dbReopened = ZabDatabase.Open(dbPath))
            {
                Assert.Equal(2, dbReopened.Knowledge.Count);
                Assert.NotNull(dbReopened.FindKnowledge("valid_1"));
                Assert.NotNull(dbReopened.FindKnowledge("valid_2"));
            }

            // Verify WAL was truncated safely back to valid frames length
            Assert.Equal(validWalLength, new FileInfo(walPath).Length);
        }

        [Fact]
        public void ZabDatabase_Checkpoint_CompactsWalToZeroBytes()
        {
            string dbPath = Path.Combine(_tempDir, "checkpoint_test.zab");
            string walPath = dbPath + "-wal";

            using (var db = ZabDatabase.CreateNew(dbPath))
            {
                db.Wal.AutoCheckpointThreshold = 10000;
                db.AddKnowledge("k1", "v1");
                db.AddKnowledge("k2", "v2");
                Assert.True(new FileInfo(walPath).Length > 0);

                // Execute explicit Checkpoint
                db.Checkpoint();

                // Wal file must be compacted to 0 bytes
                Assert.Equal(0, new FileInfo(walPath).Length);
            }

            // Re-open: all checkpointed data intact in baseline .zab
            using (var dbReopened = ZabDatabase.Open(dbPath))
            {
                Assert.Equal(2, dbReopened.Knowledge.Count);
            }
        }
    }
}
