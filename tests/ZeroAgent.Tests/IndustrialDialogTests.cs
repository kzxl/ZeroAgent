using System;
using System.Threading.Tasks;
using Xunit;
using ZeroAgent.Dialog;
using ZeroAgent.Dialog.DST;
using ZeroAgent.Dialog.Memory;
using ZeroAgent.Tools.Safety;

namespace ZeroAgent.Tests
{
    public class IndustrialDialogTests
    {
        [Fact]
        public async Task IndustrialDialog_MultiTurn_SlotClarificationAndExecution()
        {
            var bot = IndustrialDialogFactory.CreateIndustrialBot();
            string sessionId = "session_test_01";

            // Turn 1: User asks without providing the machine ID
            var r1 = await bot.ChatAsync(sessionId, "Kiểm tra nhiệt độ giùm tôi");

            Assert.Equal(SessionState.CollectingSlots, r1.State);
            Assert.Contains("Bạn muốn kiểm tra nhiệt độ của thiết bị nào", r1.Text);
            Assert.False(r1.IsActionExecuted);

            // Turn 2: User provides machine ID "CNC-01"
            var r2 = await bot.ChatAsync(sessionId, "CNC-01");

            Assert.Equal(SessionState.Completed, r2.State);
            Assert.Contains("CNC-01", r2.Text);
            Assert.Contains("73.5°C", r2.Text);
            Assert.True(r2.IsActionExecuted);
        }

        [Fact]
        public async Task IndustrialDialog_AnaphoraResolution_PronounFollowUp()
        {
            var bot = IndustrialDialogFactory.CreateIndustrialBot();
            string sessionId = "session_test_02";

            // Turn 1: Establish subject CNC-01
            var r1 = await bot.ChatAsync(sessionId, "Kiểm tra nhiệt độ máy CNC-01");
            Assert.Equal(SessionState.Completed, r1.State);
            Assert.Contains("CNC-01", r1.Text);

            // Turn 2: Follow-up using pronoun "nó" without repeating CNC-01
            var r2 = await bot.ChatAsync(sessionId, "Nhiệt độ nó giờ sao?");

            // Working memory should resolve "nó" -> "CNC-01" and execute without asking again!
            Assert.Equal(SessionState.Completed, r2.State);
            Assert.Contains("CNC-01", r2.Text);
            Assert.True(r2.IsActionExecuted);
        }

        [Fact]
        public async Task IndustrialDialog_SemanticMemory_RetrievesSopWithoutLlm()
        {
            var bot = IndustrialDialogFactory.CreateIndustrialBot();
            string sessionId = "session_test_03";

            var resp = await bot.ChatAsync(sessionId, "Quy trình xử lý quá nhiệt Lò nung F-01");

            Assert.Contains("Quy trình xử lý quá nhiệt Lò nung F-01", resp.Text);
            Assert.Contains("van tuần hoàn làm mát C-2", resp.Text);
            Assert.Contains("1,200°C", resp.Text);
            Assert.Equal("KNOWLEDGE_RETRIEVAL", resp.IntentName);
        }

        [Fact]
        public async Task IndustrialDialog_EpisodicMemory_RecallsPastIncidents()
        {
            var bot = IndustrialDialogFactory.CreateIndustrialBot();
            string sessionId = "session_test_04";

            var resp = await bot.ChatAsync(sessionId, "Sự cố quá nhiệt máy CNC-01 trước đây xử lý sao?");

            Assert.Contains("Ghi nhận sự cố trước đây", resp.Text);
            Assert.Contains("kẹt cánh quạt làm mát số 3", resp.Text);
            Assert.Equal("HISTORICAL_EPISODE", resp.IntentName);
        }

        [Fact]
        public async Task IndustrialDialog_Rbac_BlocksUnauthorizedOperatorAndAllowsSupervisorWithHitl()
        {
            var safetyGate = new HitlSafetyGate();
            var bot = IndustrialDialogFactory.CreateIndustrialBot(safetyGate);
            string sessionId = "session_test_05";

            // 1. Regular Operator without STOP_MACHINE permission
            var operatorUser = new UserProfile("user_op_01", "Worker-Nam", UserRole.Operator);
            var r1 = await bot.ChatAsync(sessionId, "Dừng máy CNC-01", operatorUser);

            Assert.Equal(SessionState.ActionBlockedByPermission, r1.State);
            Assert.Contains("Truy cập bị từ chối", r1.Text);
            Assert.Contains("STOP_MACHINE", r1.Text);

            // 2. Supervisor with STOP_MACHINE permission -> Triggers HITL approval
            var supervisorUser = new UserProfile("user_sup_01", "Chief-Tuan", UserRole.Supervisor);
            var chatTask = bot.ChatAsync(sessionId, "Dừng máy CNC-01", supervisorUser);

            // Verify HITL gate intercepted the call
            await Task.Delay(50);
            var pending = safetyGate.PendingRequests;
            Assert.NotEmpty(pending);

            // Human Supervisor clicks Approve
            foreach (var req in pending)
            {
                safetyGate.Approve(req.RequestId, "Chief-Tuan");
            }

            var r2 = await chatTask;
            Assert.Equal(SessionState.Completed, r2.State);
            Assert.Contains("Lệnh dừng thiết bị CNC-01", r2.Text);
            Assert.True(r2.IsActionExecuted);
        }
    }
}
