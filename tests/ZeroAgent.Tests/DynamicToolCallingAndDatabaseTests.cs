using System;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using ZeroAgent.Core.Tools;
using ZeroAgent.Dialog;
using ZeroAgent.Dialog.DST;
using ZeroAgent.Tools.Data;
using ZeroAgent.Tools.Erp;

namespace ZeroAgent.Tests
{
    public class DynamicToolCallingAndDatabaseTests
    {
        [Fact]
        public async Task ToolCallModels_ExecuteCallAsync_MeasuresDurationAndStatus()
        {
            var registry = new AgentToolRegistry();
            registry.Register("echo_tool", "Echoes the argument", arg => Task.FromResult($"Echo: {arg}"));

            var request = new ToolCallRequest("echo_tool", "hello-world", "call_test_101");
            var response = await registry.ExecuteCallAsync(request);

            Assert.True(response.Success);
            Assert.Equal("call_test_101", response.CallId);
            Assert.Equal("echo_tool", response.ToolName);
            Assert.Equal("Echo: hello-world", response.Content);
            Assert.Null(response.Error);
            Assert.True(response.Duration.TotalMilliseconds >= 0.0);
        }

        [Fact]
        public void ToolSemanticRouter_RoutesUserUtteranceToCorrectTool()
        {
            var registry = new AgentToolRegistry();
            registry.Register("db_query_table", "Queries tabular records and columns from database", (string _) => "db_ok");
            registry.Register("query_tsdb_metric", "Queries continuous time-series telemetry sensor metrics like motor temperature", (string _) => "tsdb_ok");
            registry.Register("write_plc_coil", "Controls PLC relays, switches, and emergency coils", (string _) => "plc_ok");
            registry.Register("host_telemetry", "Reports CPU, RAM, and hardware system telemetry", (string _) => "host_ok");

            var router = new ToolSemanticRouter();
            router.RegisterRegistry(registry);

            // 1. Query about database tables (English keywords matching tool name & description)
            var dbMatches = router.Route("Cho tôi xem records table", topK: 1);
            Assert.NotEmpty(dbMatches);
            Assert.Equal("db_query_table", dbMatches[0].Tool.Name);

            // Test custom triggers for Vietnamese localization
            var customRouter = new ToolSemanticRouter();
            customRouter.RegisterTool(registry.Get("db_query_table")!, triggers: new[] { "dữ liệu", "bảng" });
            var vnMatches = customRouter.Route("Cho tôi xem dữ liệu bảng", topK: 1);
            Assert.NotEmpty(vnMatches);
            Assert.Equal("db_query_table", vnMatches[0].Tool.Name);

            // 2. Query about sensor temperature
            var tsdbMatches = router.Route("Kiểm tra sensor motor temperature", topK: 1);
            Assert.NotEmpty(tsdbMatches);
            Assert.Equal("query_tsdb_metric", tsdbMatches[0].Tool.Name);

            // 3. Query about PLC coil
            var plcMatches = router.Route("Bật coil relay plc", topK: 1);
            Assert.NotEmpty(plcMatches);
            Assert.Equal("write_plc_coil", plcMatches[0].Tool.Name);
        }

        [Fact]
        public async Task DynamicDatabaseQueryTool_ListTablesAndDescribeTable_Succeeds()
        {
            var registry = new AgentToolRegistry();
            DynamicDatabaseQueryTool.RegisterAll(registry);

            // 1. List Tables
            string tablesJson = await registry.ExecuteAsync("db_list_tables", "");
            Assert.Contains("factory_machines", tablesJson);

            // 2. Describe Table
            string describeJson = await registry.ExecuteAsync("db_describe_table", "factory_machines");
            Assert.Contains("machine_id", describeJson);
            Assert.Contains("efficiency", describeJson);
            Assert.Contains("defect_count", describeJson);
            Assert.Contains("embedding", describeJson);
        }

        [Fact]
        public async Task DynamicDatabaseQueryTool_QueryTableWithFilter_FiltersCorrectly()
        {
            var registry = new AgentToolRegistry();
            DynamicDatabaseQueryTool.RegisterAll(registry);

            string queryArgs = JsonSerializer.Serialize(new
            {
                tableName = "factory_machines",
                whereColumn = "status",
                whereValue = "RUNNING",
                limit = 10
            });

            string resultJson = await registry.ExecuteAsync("db_query_table", queryArgs);
            Assert.Contains("CNC-01", resultJson);
            Assert.Contains("ROBOT-ARM-01", resultJson);
            Assert.DoesNotContain("PRESS-03", resultJson); // PRESS-03 is CRITICAL_ERROR
            Assert.DoesNotContain("CONVEYOR-04", resultJson); // CONVEYOR-04 is IDLE
        }

        [Fact]
        public async Task DynamicDatabaseQueryTool_VectorSearchTable_ReturnsNearestNeighbors()
        {
            var registry = new AgentToolRegistry();
            DynamicDatabaseQueryTool.RegisterAll(registry);

            // Query vector matching row 0 (CNC-01 has 1.0 at index 0)
            float[] queryVec = new float[16];
            queryVec[0] = 1.0f;

            string searchArgs = JsonSerializer.Serialize(new
            {
                tableName = "factory_machines",
                columnName = "embedding",
                topK = 2,
                queryVector = queryVec
            });

            string resultJson = await registry.ExecuteAsync("db_vector_search", searchArgs);
            Assert.Contains("CNC-01", resultJson);
            Assert.Contains("score", resultJson);
        }

        [Fact]
        public async Task IndustrialDialog_DatabaseQueryIntent_EndToEndChat_ExecutesSuccessfully()
        {
            var bot = IndustrialDialogFactory.CreateIndustrialBot(enableNeuralClassifier: true);
            string sessionId = "session_db_query_01";

            var response = await bot.ChatAsync(sessionId, "Cho tôi xem bảng máy móc");

            Assert.Equal(SessionState.Completed, response.State);
            Assert.True(response.IsActionExecuted);
            Assert.Equal("QUERY_DATABASE_RECORDS", response.IntentName);
            Assert.Contains("factory_machines", response.Text);
            Assert.Contains("CNC-01", response.Text);
        }

        [Fact]
        public async Task ToolCallBatch_Execution_PreservesCallIds()
        {
            var registry = new AgentToolRegistry();
            DynamicDatabaseQueryTool.RegisterAll(registry);

            var requests = new[]
            {
                new ToolCallRequest("db_list_tables", "{}", "call_batch_1"),
                new ToolCallRequest("db_describe_table", "factory_machines", "call_batch_2")
            };

            var responses = await registry.ExecuteBatchAsync(requests);

            Assert.Equal(2, responses.Length);
            Assert.Equal("call_batch_1", responses[0].CallId);
            Assert.True(responses[0].Success);
            Assert.Equal("call_batch_2", responses[1].CallId);
            Assert.True(responses[1].Success);
        }

        [Fact]
        public void ToolCallParser_XmlTags_ParsesCorrectly()
        {
            string output = "<thought>Kiểm tra số dư lô hàng</thought><tool_call>{\"name\": \"mds_db_lot_balance_query\", \"arguments\": {\"lot_number\": \"14G24FIL002\"}}</tool_call>";
            bool ok = ToolCallParser.TryParseToolCall(output, out var req);

            Assert.True(ok);
            Assert.Equal("mds_db_lot_balance_query", req.ToolName);
            Assert.Contains("14G24FIL002", req.ArgumentsJson);
        }

        [Fact]
        public void ToolCallParser_SafeRepairJson_RepairsMalformedAndTruncatedJson()
        {
            // Truncated JSON without closing braces
            string truncated = "<tool_call>{\"name\": \"mds_db_lot_balance_query\", \"arguments\": {\"lot_number\": \"14G24FIL002\"";
            bool ok = ToolCallParser.TryParseToolCall(truncated, out var req);

            Assert.True(ok);
            Assert.Equal("mds_db_lot_balance_query", req.ToolName);
            Assert.Contains("14G24FIL002", req.ArgumentsJson);

            // Single quotes and trailing comma
            string malformed = "{'name': 'mds_db_low_stock_alert', 'arguments': {'threshold_kg': 50,}}";
            bool ok2 = ToolCallParser.TryParseJsonPayload(malformed, out var req2);

            Assert.True(ok2);
            Assert.Equal("mds_db_low_stock_alert", req2.ToolName);
            Assert.Contains("50", req2.ArgumentsJson);
        }

        [Fact]
        public async Task MdsDatabaseToolkit_SalesOrder_Query_ReturnsValidOrderAndItems()
        {
            var registry = new AgentToolRegistry();
            MdsDatabaseToolkit.RegisterAll(registry);

            var res = await registry.ExecuteCallAsync(new ToolCallRequest("mds_db_so_query", "{\"order_id\": \"sal26-Test\"}"));
            Assert.True(res.Success);
            Assert.Contains("sal26-Test", res.Content);
            Assert.Contains("KH-MDS-TEST", res.Content);
            Assert.Contains("SP-KHAY-01", res.Content);
            Assert.Contains("Draft", res.Content);
        }

        [Fact]
        public async Task MdsDatabaseToolkit_SalesOrder_DeliveryStatus_CalculatesAccuratePercentages()
        {
            var registry = new AgentToolRegistry();
            MdsDatabaseToolkit.RegisterAll(registry);

            // MLG26-1562 has 50 + 100 ordered, 30 + 50 delivered = 80 / 150 = 53.3%
            var res = await registry.ExecuteCallAsync(new ToolCallRequest("mds_db_so_delivery_status", "{\"order_id\": \"MLG26-1562\"}"));
            Assert.True(res.Success);
            Assert.Contains("MLG26-1562", res.Content);
            Assert.Contains("53.3", res.Content);
            Assert.Contains("Đang giao hàng từng phần", res.Content);
        }

        [Fact]
        public async Task MdsDatabaseToolkit_SalesOrder_InventoryCheck_EvaluatesAtpStatus()
        {
            var registry = new AgentToolRegistry();
            MdsDatabaseToolkit.RegisterAll(registry);

            // SO-2026-MDS01 requires 1000kg PP (rem) and 2500kg HDPE (rem).
            // Stock has 4014kg PP available and 10000kg HDPE available -> All fulfillable!
            var res = await registry.ExecuteCallAsync(new ToolCallRequest("mds_db_so_inventory_check", "{\"order_id\": \"SO-2026-MDS01\"}"));
            Assert.True(res.Success);
            Assert.Contains("SO-2026-MDS01", res.Content);
            Assert.Contains("KHẢ DỤNG - ĐỦ HÀNG GIAO NGAY", res.Content);
        }

        [Fact]
        public async Task MdsDatabaseToolkit_SalesOrder_Cancel_RejectsIfDeliveryInProgress_AndAllowsIfClean()
        {
            var registry = new AgentToolRegistry();
            MdsDatabaseToolkit.RegisterAll(registry);

            // 1. Rejects cancellation for MLG26-1562 because deliveredQty > 0
            var resReject = await registry.ExecuteCallAsync(new ToolCallRequest("mds_db_so_cancel_or_update", "{\"order_id\": \"MLG26-1562\", \"new_status\": \"Cancelled\"}"));
            Assert.True(resReject.Success);
            Assert.Contains("VIOLATION_DELIVERY_IN_PROGRESS", resReject.Content);
            Assert.Contains("Không thể hủy đơn hàng", resReject.Content);

            // 2. Allows cancellation for sal26-Test (deliveredQty == 0)
            var resAllow = await registry.ExecuteCallAsync(new ToolCallRequest("mds_db_so_cancel_or_update", "{\"order_id\": \"sal26-Test\", \"new_status\": \"Cancelled\"}"));
            Assert.True(resAllow.Success);
            Assert.Contains("\"success\":true", resAllow.Content);
            Assert.Contains("Cancelled", resAllow.Content);
        }
    }
}
