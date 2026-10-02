using System;
using System.IO;
using System.Linq;
using Xunit;
using ZeroAgent.Core.Database;

namespace ZeroAgent.Tests
{
    public class ZabErpKnowledgeIngestionTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _zabPath;
        private readonly string _jsonPath;

        public ZabErpKnowledgeIngestionTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "zab_erp_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _zabPath = Path.Combine(_tempDir, "erp_sales_order.zab");
            _jsonPath = string.Empty;
            string? current = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                string candidate = Path.Combine(current, "data", "knowledge", "erp_sales_order_kb.json");
                if (File.Exists(candidate))
                {
                    _jsonPath = candidate;
                    break;
                }
                current = Directory.GetParent(current)?.FullName;
            }
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
        public void ImportErpSalesOrderKnowledge_PopulatesDatabaseAndIndexes()
        {
            Assert.True(File.Exists(_jsonPath), $"Expected knowledge file at: {_jsonPath}");

            // 1. Create container and import JSON
            using (var db = ZabDatabase.CreateNew(_zabPath))
            {
                db.ImportFromJson(_jsonPath);

                Assert.Equal("ZeroAgent-ERP", db.Manifest.Name);
                Assert.True(db.Knowledge.Count >= 20, $"Expected >= 20 knowledge items, got {db.Knowledge.Count}");
                Assert.True(db.Reflexions.Count >= 4, $"Expected >= 4 reflexions, got {db.Reflexions.Count}");
                Assert.True(db.Plans.Count >= 4, $"Expected >= 4 plans, got {db.Plans.Count}");

                // 2. Test specific business rules
                var draftRule = db.FindKnowledge("sales_order_status_draft");
                Assert.NotNull(draftRule);
                Assert.Contains("Bản nháp", draftRule.Value);

                var finalizedRule = db.FindKnowledge("sales_order_status_finalized");
                Assert.NotNull(finalizedRule);
                Assert.Contains("tbMISA_SyncQueues", finalizedRule.Value);

                var separateRule = db.FindKnowledge("sales_order_separate_rules");
                Assert.NotNull(separateRule);
                Assert.Contains("CancelAndSeparateOrderAsync", separateRule.Value);

                var faqEdit = db.FindKnowledge("faq_why_cannot_edit_order");
                Assert.NotNull(faqEdit);
                Assert.Contains("Finalized", faqEdit.Value);
            }

            // 3. Reopen binary .zab and verify persistence
            using (var reopenedDb = ZabDatabase.Open(_zabPath))
            {
                Assert.Equal("ZeroAgent-ERP", reopenedDb.Manifest.Name);
                Assert.True(reopenedDb.Knowledge.Count >= 20);

                // Build Bloom filter and sparse block index
                var bloom = ZabBloomFilter.Build(reopenedDb.Knowledge.Select(k => k.Key));
                Assert.True(bloom.MayContain("sales_order_status_finalized"));
                Assert.True(bloom.MayContain("sales_order_prefix_rules"));
                Assert.False(bloom.MayContain("non_existent_random_key_12345"));

                var billionIndex = reopenedDb.BuildBillionScaleIndex(recordsPerBlock: 4);
                Assert.NotNull(billionIndex);
            }
        }
    }
}
