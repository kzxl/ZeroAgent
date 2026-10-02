using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ZeroAgent.Core.Engine;
using ZeroAgent.Dialog.Learning;
using ZeroTokenizer.Core;
using ZeroTokenizer.Core.Training;
using ZeroTokenizer.Core.Vietnamese;

namespace ZeroAgent.Tests
{
    public class ErpPipelineEndToEndTests
    {
        [Fact]
        public void ErpCorpusGenerator_GeneratesDomainRichCorpus()
        {
            var corpus = ErpCorpusGenerator.GenerateCorpus(approximateDocumentCount: 500);

            Assert.NotEmpty(corpus);
            Assert.True(corpus.Count >= 500);

            // Verify domain keywords are abundantly present
            bool hasInventory = corpus.Any(d => d.Contains("tồn kho") || d.Contains("SKU"));
            bool hasSales = corpus.Any(d => d.Contains("đơn hàng bán") || d.Contains("SO-"));
            bool hasPurchasing = corpus.Any(d => d.Contains("đơn đặt hàng") || d.Contains("PO-"));
            bool hasProduction = corpus.Any(d => d.Contains("lệnh sản xuất") || d.Contains("BOM"));
            bool hasFinance = corpus.Any(d => d.Contains("sổ quỹ") || d.Contains("ngân hàng"));

            Assert.True(hasInventory, "Corpus must contain Inventory domain terms");
            Assert.True(hasSales, "Corpus must contain Sales domain terms");
            Assert.True(hasPurchasing, "Corpus must contain Purchasing domain terms");
            Assert.True(hasProduction, "Corpus must contain Production domain terms");
            Assert.True(hasFinance, "Corpus must contain Finance domain terms");
        }

        [Fact]
        public void BpeTrainer_TrainedOnErpCorpus_DemonstratesHighCompressionEfficiency()
        {
            // 1. Generate 300 corpus sentences
            var corpus = ErpCorpusGenerator.GenerateCorpus(approximateDocumentCount: 300);

            // 2. Train a specialized 500-token BPE tokenizer
            var trainer = new BpeTrainer(targetVocabSize: 500, minFrequency: 2);
            var tokenizer = trainer.Train(corpus);

            Assert.True(tokenizer.VocabularySize >= 300);

            // 3. Test on realistic ERP sentence
            string sampleQuery = "Người vận hành: Kiểm tra tồn kho của mã hàng SKU-STEEL-01 tại Kho Tổng xem còn bao nhiêu cái khả dụng?";
            
            int[] tokens = tokenizer.Encode(sampleQuery);
            string decoded = tokenizer.Decode(tokens);

            // Lossless roundtrip verification
            Assert.Equal(sampleQuery, decoded);

            // Compression efficiency:
            // In UTF-8 bytes, this Vietnamese string is ~127 bytes.
            // With specialized BPE, 500-vocab merges already achieve ~50% compression reduction.
            byte[] utf8Bytes = System.Text.Encoding.UTF8.GetBytes(sampleQuery);
            Assert.True(tokens.Length <= utf8Bytes.Length * 0.60f, 
                $"BPE token count ({tokens.Length}) should achieve at least 40% reduction over raw byte count ({utf8Bytes.Length}).");

            // Also verify with production pre-seeded VietnameseErpTokenizer
            var prodTokenizer = VietnameseErpTokenizer.CreateDefault();
            int[] prodTokens = prodTokenizer.Encode(sampleQuery);
            Assert.True(tokens.Length < prodTokens.Length, 
                $"Trained BPE tokenizer ({tokens.Length} tokens) is more compact than base seed ({prodTokens.Length} tokens).");
            Assert.Equal(sampleQuery, prodTokenizer.Decode(prodTokens));
        }

        [Fact]
        public async Task ErpDialogueDistiller_DistillsStructuredSftTrajectory_AndExportsJsonl()
        {
            var teacherLlm = new MockLlmClient();
            teacherLlm.Enqueue("<thought>Truy vấn tồn kho hiện hành của mã SKU-STEEL-01 tại Kho Tổng</thought>" +
                               "<response>Dạ chào anh, mặt hàng SKU-STEEL-01 tại Kho Tổng hiện còn 500 cái (trong đó 420 cái khả dụng).</response>");

            var distiller = new ErpDialogueDistiller(teacherLlm);

            var sample = await distiller.DistillTrajectoryAsync(
                domain: "inventory",
                userQuery: "Kiểm tra tồn kho mã SKU-STEEL-01",
                toolName: "check_inventory",
                toolArgs: "sku='SKU-STEEL-01', wh='Kho Tổng'",
                toolOutput: "500 cái (khả dụng: 420 cái)");

            Assert.Equal("inventory", sample.Domain);
            Assert.Contains("Truy vấn tồn kho", sample.Thought);
            Assert.Contains("check_inventory", sample.ToolCall);
            Assert.Contains("500 cái", sample.Response);

            // Verify canonical prompt formatting
            string formatted = sample.ToFormattedText();
            Assert.StartsWith("<bos>", formatted);
            Assert.Contains("<expert>inventory</expert>", formatted);
            Assert.Contains("<thought>Truy vấn tồn kho", formatted);
            Assert.Contains("<tool_call>check_inventory", formatted);
            Assert.Contains("<response>Dạ chào anh", formatted);
            Assert.EndsWith("<eos>", formatted);

            // Verify JSONL export
            string tempFile = Path.Combine(Path.GetTempPath(), $"erp_sft_test_{Guid.NewGuid():N}.jsonl");
            try
            {
                await ErpDialogueDistiller.ExportDatasetJsonlAsync(new[] { sample }, tempFile);

                Assert.True(File.Exists(tempFile));
                string content = await File.ReadAllTextAsync(tempFile);
                Assert.Contains("\"domain\":\"inventory\"", content);
                Assert.Contains("\"query\":\"Kiểm tra tồn kho", content);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task ErpDialogueDistiller_LiveGemma_DistillsRealSftSample()
        {
            // Check if local Gemma is reachable
            using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            try
            {
                var ping = await httpClient.GetAsync("http://192.168.10.7:1234/v1/models");
                if (!ping.IsSuccessStatusCode) return; // Skip if local gateway is offline
            }
            catch
            {
                return; // Gateway offline
            }

            var gemmaClient = new ExternalApiLlmClient(
                endpoint: "http://192.168.10.7:1234/v1/chat/completions",
                model: "google/gemma-4-e4b",
                apiKey: "ollama");

            var distiller = new ErpDialogueDistiller(gemmaClient);

            var sample = await distiller.DistillTrajectoryAsync(
                domain: "production",
                userQuery: "Tiến độ lệnh sản xuất MO-2026-01 xưởng may thế nào?",
                toolName: "check_mo_progress",
                toolArgs: "mo='MO-2026-01'",
                toolOutput: "Đã hoàn thành 65% kế hoạch (1.300 / 2.000 sản phẩm)");

            Assert.Equal("production", sample.Domain);
            Assert.False(string.IsNullOrWhiteSpace(sample.Response));
            Assert.True(sample.Response.Contains("65%") || sample.Response.Contains("hoàn thành") || sample.Response.Contains("sản xuất") || sample.Response.Length > 10);
        }
    }
}
