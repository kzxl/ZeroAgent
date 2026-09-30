using System;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using ZeroAgent.Core.Tools;
using ZeroAgent.Dialog;
using ZeroAgent.Dialog.DST;
using ZeroAgent.Tools.Data;

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
    }
}
