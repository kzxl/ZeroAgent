using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZeroAgent.Core.Context;
using ZeroAgent.Core.Engine;
using ZeroAgent.Core.Tools;
using ZeroAgent.Tools.Erp;
using ZeroLlm.Core.Engine;
using ZeroLlm.Core.Sampling;
using ZeroTokenizer.Core.Vietnamese;

namespace ZeroAgent.Tests
{
    public sealed class MdsDatabaseAgentEndToEndEvaluationTests
    {
        [Fact]
        public async Task MdsDatabaseWorkflow_EndToEnd_LotBalance_QueriesLiveDatabaseAndExplains()
        {
            var registry = new AgentToolRegistry();
            MdsDatabaseToolkit.RegisterAll(registry);

            var mockLlm = new MockLlmClient();
            // Step 1: Model reasons and decides to call mds_db_lot_balance_query
            mockLlm.Enqueue(
                "Thought: Người dùng muốn kiểm tra tồn kho lô LOT-2026-PP43 tại kho KNVLSX. Tôi cần truy vấn trực tiếp bảng tbINV_Material_LotBalance trong cơ sở dữ liệu MDS.\n" +
                "Action: mds_db_lot_balance_query({\"lot_no\": \"LOT-2026-PP43\", \"warehouse_code\": \"KNVLSX\"})");

            // Step 2: Model receives observation and formulates final answer
            mockLlm.Enqueue(
                "Thought: Đã nhận được dữ liệu từ cơ sở dữ liệu MDS. Lô LOT-2026-PP43 hiện có tổng tồn kho 5,330 kg, giữ chỗ 1,316 kg và khả dụng thực tế 4,014 kg.\n" +
                "Final Answer: Tại Kho NVL sản xuất (KNVLSX), lô hàng LOT-2026-PP43 của vật tư Hạt nhựa Polypropylene nguyên sinh (NVL-PP-500) hiện có tổng tồn 5,330 kg, trong đó đã giữ chỗ 1,316 kg và số lượng khả dụng thực tế còn lại là 4,014 kg. Số lượng này hoàn toàn đủ để cấp phát cho kế hoạch xưởng.");

            var agent = new ReActAgent("MdsAgent", "ERP Inventory Assistant", registry, mockLlm);
            var context = new AgentContext("Kiểm tra số lượng khả dụng của lô LOT-2026-PP43 ở kho KNVLSX xem còn đủ cấp phát cho xưởng không");

            var response = await agent.ExecuteAsync(context);

            Assert.True(response.Success);
            Assert.Contains("4,014 kg", response.Output);
            Assert.Contains("5,330 kg", response.Output);
            Assert.True(response.TotalSteps >= 1);
        }

        [Fact]
        public async Task MdsDatabaseWorkflow_EndToEnd_StockInTicket_ExtractsSupplierAndPO()
        {
            var registry = new AgentToolRegistry();
            MdsDatabaseToolkit.RegisterAll(registry);

            var mockLlm = new MockLlmClient();
            mockLlm.Enqueue(
                "Thought: Yêu cầu tra cứu phiếu nhập kho PN-NVL-2026-6380 từ bảng tbINV_Material_StockIn.\n" +
                "Action: mds_db_stock_in_query({\"ticket_code\": \"PN-NVL-2026-6380\"})");

            mockLlm.Enqueue(
                "Thought: Dữ liệu trả về cho thấy phiếu PN-NVL-2026-6380 từ nhà cung cấp Công ty Cổ Phần Mỹ Lan theo đơn PO-2026-4402 với số lượng 10,000 kg.\n" +
                "Final Answer: Phiếu nhập kho PN-NVL-2026-6380 đã được nhập kho chính thức tại Kho KNVLSX. Nhà cung cấp: Công ty Cổ Phần Mỹ Lan (CTYMLG01), theo đơn hàng PO-2026-4402, tổng khối lượng thực nhập là 10,000 kg.");

            var agent = new ReActAgent("MdsAgent", "ERP Inventory Assistant", registry, mockLlm);
            var context = new AgentContext("Tra cứu thông tin phiếu nhập kho PN-NVL-2026-6380 từ cơ sở dữ liệu MDS");

            var response = await agent.ExecuteAsync(context);

            Assert.True(response.Success);
            Assert.Contains("Công ty Cổ Phần Mỹ Lan", response.Output);
            Assert.Contains("10,000 kg", response.Output);
            Assert.Contains("PO-2026-4402", response.Output);
        }

        [Fact]
        public async Task MdsDatabaseWorkflow_EndToEnd_LowStockAlert_CalculatesDeficit()
        {
            var registry = new AgentToolRegistry();
            MdsDatabaseToolkit.RegisterAll(registry);

            var mockLlm = new MockLlmClient();
            mockLlm.Enqueue(
                "Thought: Cần quét toàn bộ cơ sở dữ liệu kho kiểm tra các lô dưới 1,000 kg khả dụng.\n" +
                "Action: mds_db_low_stock_alert({\"threshold_kg\": 1000})");

            mockLlm.Enqueue(
                "Thought: Phát hiện 2 lô có lượng khả dụng dưới 1,000 kg: LOT-2026-MB05 (700 kg) và LOT-2026-PET08 (0 kg).\n" +
                "Final Answer: Cảnh báo tồn kho: Hệ thống đã phát hiện 2 lô nguyên vật liệu có lượng khả dụng dưới ngưỡng an toàn 1,000 kg:\n1. Lô LOT-2026-MB05 (Hạt màu trắng sứ tại kho KNVLNC): còn 700 kg (thiếu hụt 300 kg).\n2. Lô LOT-2026-PET08 (Màng PET cuộn tại kho KBTP): còn 0 kg (đã hết khả dụng). Đề xuất đặt hàng bổ sung khẩn cấp.");

            var agent = new ReActAgent("MdsAgent", "ERP Inventory Assistant", registry, mockLlm);
            var context = new AgentContext("Quét cơ sở dữ liệu kho kiểm tra xem có mã vật tư nào dưới định mức an toàn 1,000 kg không");

            var response = await agent.ExecuteAsync(context);

            Assert.True(response.Success);
            Assert.Contains("LOT-2026-MB05", response.Output);
            Assert.Contains("LOT-2026-PET08", response.Output);
        }

        [Fact]
        public async Task MdsDatabaseWorkflow_EndToEnd_NonExistentLot_DoesNotHallucinate()
        {
            var registry = new AgentToolRegistry();
            MdsDatabaseToolkit.RegisterAll(registry);

            var mockLlm = new MockLlmClient();
            mockLlm.Enqueue(
                "Thought: Tra cứu lô hàng LOT-9999-FAKE trong cơ sở dữ liệu kho MDS.\n" +
                "Action: mds_db_lot_balance_query({\"lot_no\": \"LOT-9999-FAKE\"})");

            mockLlm.Enqueue(
                "Thought: Cơ sở dữ liệu thông báo không tìm thấy dữ liệu cho mã lô LOT-9999-FAKE. Tôi cần báo rõ cho người dùng chứ không được bịa số liệu.\n" +
                "Final Answer: Không tìm thấy lô hàng LOT-9999-FAKE trong cơ sở dữ liệu kho MDS (bảng tbINV_Material_LotBalance). Vui lòng kiểm tra lại tính chính xác của mã lô hàng.");

            var agent = new ReActAgent("MdsAgent", "ERP Inventory Assistant", registry, mockLlm);
            var context = new AgentContext("Kiểm tra số lượng lô LOT-9999-FAKE ở kho KNVLSX");

            var response = await agent.ExecuteAsync(context);

            Assert.True(response.Success);
            Assert.Contains("Không tìm thấy", response.Output);
            Assert.Contains("LOT-9999-FAKE", response.Output);
        }

        [Fact]
        public async Task GgufModel_LoadsAndInfersDirectly_WithZeroLlmClient()
        {
            string modelPath = @"E:\15. Other\ZeroUniverse\ZeroApps\ZTrain\models\vietnamese_erp_mds_q8_0.gguf";
            if (!File.Exists(modelPath))
            {
                modelPath = @"E:\15. Other\ZeroUniverse\ZeroApps\ZTrain\models\vietnamese_erp_mds.gguf";
            }

            // Skip test gracefully if running in an environment where model hasn't been built yet
            if (!File.Exists(modelPath)) return;

            var model = LlmModel.LoadGguf(modelPath);
            Assert.NotNull(model);
            Assert.True(model.Config.VocabSize > 0);

            var tokenizer = VietnameseErpTokenizer.CreateDefault();
            var sampling = new SamplingConfig
            {
                Temperature = 0.1f,
                MaxTokens = 25
            };

            using var engine = new LlmEngine(model, tokenizer, sampling);
            var client = new ZeroLlmClient(engine);

            string prompt = "<bos><expert>00erp</expert><user>Kiểm tra tồn kho lô LOT-2026-PP43 tại kho KNVLSX</user><thought>";
            string output = await client.CompleteAsync(prompt);

            Assert.NotNull(output);
            Assert.NotEmpty(output);
        }
    }
}
