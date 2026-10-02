using System;
using System.Collections.Generic;
using System.Text;

namespace ZeroAgent.Dialog.Learning
{
    /// <summary>
    /// Synthetic and domain-grounded Vietnamese ERP corpus generator.
    /// Synthesizes high-density enterprise texts including Master Data catalogues,
    /// Standard Operating Procedures (SOP), multi-tier transactional records,
    /// and realistic operator dialogues across 5 operational business domains.
    /// </summary>
    public static class ErpCorpusGenerator
    {
        private static readonly string[] SkuPrefixes = { "SKU-STEEL", "SKU-PLASTIC", "VT-SERVO", "VT-BEARING", "NVL-COTTON", "NVL-ALUM", "SP-GEAR", "TP-PUMP" };
        private static readonly string[] ItemNames = 
        {
            "Thép tấm cuộn cán nóng SS400", "Hạt nhựa nguyên sinh PP", "Động cơ servo 750W 3000rpm", 
            "Vòng bi bạc đạn công nghiệp SKF", "Vải sợi cotton 100% dệt thoi", "Phôi nhôm định hình 6063", 
            "Hộp giảm tốc bánh răng hành tinh", "Bơm thủy lực piston cao áp 250 bar",
            "Bulong thép không gỉ inox 304 M8", "Sơn tĩnh điện màu xám công nghiệp",
            "Cảm biến nhiệt độ RTD PT100", "Biến tần điều khiển tốc độ động cơ 5.5kW"
        };

        private static readonly string[] Warehouses = 
        { 
            "Kho Tổng", "Kho Nguyên Vật Liệu", "Kho Thành Phẩm", "Kho Phụ Tùng & Thiết Bị", 
            "Kho Ngoại Quan Hải Phòng", "Kho KCN Sóng Thần", "Kho Lạnh Bảo Quản", "Kho Phế Liệu Tái Chế" 
        };

        private static readonly string[] Customers = 
        { 
            "Công ty Cổ phần Cơ khí Chế tạo Alpha", "Tập đoàn Dệt may Việt Thắng", "Nhà máy Sản xuất Ô tô VinaAuto", 
            "Công ty TNHH Nhựa Công nghiệp Tiến Phát", "Tổng công ty Xây dựng Công trình Giao thông" 
        };

        private static readonly string[] Suppliers = 
        { 
            "Tập đoàn Thép Hòa Phát", "Công ty Cung ứng Vật tư Thiết bị Công nghiệp Sao Mai", 
            "Đại lý Phân phối Vòng bi SKF Việt Nam", "Công ty Hóa chất & Sơn Á Đông", "Nhà máy Nhôm Đông Á" 
        };

        private static readonly string[] Units = { "cái", "bộ", "kg", "tấn", "mét", "cuộn", "thùng", "hộp", "chiếc", "lít" };

        /// <summary>
        /// Generates a comprehensive, domain-rich collection of Vietnamese ERP corpus documents
        /// suitable for training specialized BPE tokenizers and domain expert classifiers.
        /// </summary>
        public static List<string> GenerateCorpus(int approximateDocumentCount = 3000)
        {
            var corpus = new List<string>(approximateDocumentCount + 200);

            // 1. SOP Manuals & Business Rules
            corpus.AddRange(GenerateSopManuals());

            // 2. Master Data Catalogues
            corpus.AddRange(GenerateMasterDataCatalog());

            // 3. Operational Dialogue & Query Patterns
            int dialogueCount = Math.Max(1000, approximateDocumentCount - corpus.Count);
            corpus.AddRange(GenerateOperationalDialogues(dialogueCount));

            return corpus;
        }

        private static List<string> GenerateSopManuals()
        {
            return new List<string>
            {
                "Quy trình quản lý kho vận theo chuẩn FIFO: Mọi lô hàng nguyên vật liệu nhập trước bắt buộc phải được xuất dùng trước. Thủ kho kiểm tra phiếu nhập kho, số serial lô hàng, vị trí kệ lưu trữ trước khi ký xác nhận bàn giao cho phân xưởng sản xuất.",
                "Quy định an toàn tồn kho: Mức tồn kho tối thiểu của thép cuộn là 50 tấn, mức an toàn là 120 tấn. Khi số lượng tồn thực tế xuống dưới ngưỡng tối thiểu, hệ thống tự động phát cảnh báo và tạo phiếu đề xuất mua hàng gửi phòng cung ứng.",
                "Quy trình kiểm soát đơn hàng bán SO: Phòng kinh doanh tiếp nhận báo giá từ khách hàng, kiểm tra hạn mức tín dụng và công nợ hiện tại. Nếu công nợ quá hạn trên 30 ngày, hệ thống khóa quyền tạo đơn bán mới cho đến khi kế toán xác nhận phiếu thu tiền mặt.",
                "Hướng dẫn lập đơn mua hàng PO: Nhân viên mua hàng so sánh báo giá từ ít nhất ba nhà cung cấp đạt chuẩn ISO 9001. Đơn mua hàng PO phải ghi rõ điều khoản thanh toán, thuế VAT, địa điểm giao nhận tại kho tổng và thời gian bảo hành kỹ thuật.",
                "Quy trình phát hành lệnh sản xuất MO: Quản đốc xưởng căn cứ vào định mức vật tư BOM (Bill of Materials) để xuất kho nguyên liệu cho từng công đoạn. Kiểm tra năng suất chuyền máy CNC, máy ép dập thủy lực và lập biên bản kiểm tra chất lượng QC tại nguồn.",
                "Quản lý sổ quỹ tiền mặt và tài khoản ngân hàng: Kế toán thanh toán đối chiếu số dư sổ quỹ tiền mặt mỗi ngày lúc 17h00. Mọi khoản chi trên 20 triệu đồng bắt buộc phải chuyển khoản qua ngân hàng Vietcombank hoặc Techcombank có hóa đơn điện tử hợp lệ.",
                "Bảo trì thiết bị nhà xưởng: Máy gia công CNC-01 yêu cầu kiểm tra độ rung vòng bi trục chính định kỳ 500 giờ làm việc. Cảm biến nhiệt độ cảnh báo nguy hiểm khi động cơ vượt ngưỡng 85°C hoặc áp suất dầu bôi trơn giảm dưới 30 PSI.",
                "Xử lý chênh lệch kiểm kê: Khi phát hiện sai lệch giữa số lượng tồn sổ sách và số lượng tồn thực tế tại kho, thủ kho lập biên bản kiểm kê, truy vết thẻ kho, lịch sử phiếu nhập xuất trong vòng 30 ngày gần nhất để làm rõ nguyên nhân."
            };
        }

        private static List<string> GenerateMasterDataCatalog()
        {
            var list = new List<string>();
            var rand = new Random(42);

            for (int i = 0; i < ItemNames.Length; i++)
            {
                string sku = $"{SkuPrefixes[i % SkuPrefixes.Length]}-{i + 1:D3}";
                string name = ItemNames[i];
                string wh = Warehouses[i % Warehouses.Length];
                string unit = Units[i % Units.Length];
                int stock = (i + 1) * 75;
                int minStock = (i + 1) * 20;

                list.Add($"Mặt hàng: {name} | Mã SKU: {sku} | Đơn vị tính: {unit} | Vị trí lưu kho: {wh} kệ A-{(i + 1):D2} | Tồn kho hiện thời: {stock} {unit} | Định mức an toàn: {minStock} {unit}.");
                list.Add($"Thẻ kho điện tử mã {sku}: Ngày hôm nay nhập kho 100 {unit} từ nhà cung cấp {Suppliers[i % Suppliers.Length]}, xuất kho 40 {unit} cho lệnh sản xuất MO-{(i + 101):D3}. Tồn cuối kỳ: {stock} {unit}.");
            }

            return list;
        }

        private static List<string> GenerateOperationalDialogues(int count)
        {
            var list = new List<string>(count);
            var rand = new Random(101);

            for (int i = 0; i < count; i++)
            {
                int domain = i % 5;
                string sku = $"{SkuPrefixes[rand.Next(SkuPrefixes.Length)]}-{rand.Next(1, 99):D2}";
                string wh = Warehouses[rand.Next(Warehouses.Length)];
                string cust = Customers[rand.Next(Customers.Length)];
                string supp = Suppliers[rand.Next(Suppliers.Length)];
                string unit = Units[rand.Next(Units.Length)];
                int qty = rand.Next(10, 500);

                switch (domain)
                {
                    case 0: // Inventory
                        list.Add($"Người vận hành: Kiểm tra tồn kho của mã {sku} tại {wh} xem còn bao nhiêu {unit} khả dụng?");
                        list.Add($"Trợ lý kho: Mã hàng {sku} hiện còn {qty} {unit} trong {wh}. Trong đó {Math.Max(5, qty - 20)} {unit} sẵn sàng xuất kho và 20 {unit} đang chờ đóng gói.");
                        list.Add($"Yêu cầu chuyển kho nội bộ: Điều chuyển {qty} {unit} {sku} từ {wh} sang {Warehouses[(rand.Next(1, Warehouses.Length)) % Warehouses.Length]}.");
                        break;

                    case 1: // Sales
                        string soNum = $"SO-2026-{rand.Next(100, 999)}";
                        list.Add($"Khách hàng hỏi: Tiến độ xử lý đơn hàng bán {soNum} của {cust} đã đến giai đoạn nào rồi?");
                        list.Add($"Trợ lý bán hàng: Đơn bán {soNum} đã hoàn thành khâu đóng gói 85%, dự kiến xe giao hàng sẽ xuất bến vào 16h00 hôm nay.");
                        list.Add($"Tra cứu công nợ khách hàng: {cust} hiện còn dư nợ {(rand.Next(50, 500) * 1000000):N0} VNĐ, trong đó có một hóa đơn quá hạn 10 ngày.");
                        break;

                    case 2: // Purchasing
                        string poNum = $"PO-2026-{rand.Next(100, 999)}";
                        list.Add($"Nhân viên mua hàng: Đơn đặt hàng {poNum} gửi cho {supp} đã được duyệt và giao hàng chưa?");
                        list.Add($"Trợ lý mua hàng: Đơn mua {poNum} trị giá {(rand.Next(20, 200) * 1000000):N0} VNĐ đã được giám đốc phê duyệt, nhà cung cấp hẹn giao tại kho tổng vào sáng mai.");
                        list.Add($"Lập phiếu đề xuất mua sắm: Cần mua gấp {qty} {unit} mã {sku} phục vụ sản xuất chuyền 1 do tồn kho đã chạm ngưỡng tối thiểu.");
                        break;

                    case 3: // Production
                        string moNum = $"MO-2026-{rand.Next(10, 99)}";
                        list.Add($"Quản đốc hỏi: Lệnh sản xuất {moNum} gia công chi tiết tại xưởng cơ khí đã đạt bao nhiêu % tiến độ?");
                        list.Add($"Trợ lý sản xuất: Lệnh {moNum} đã chạy được {rand.Next(40, 95)}% kế hoạch, tổng số lượng thành phẩm đạt chuẩn QC là {qty} cái.");
                        list.Add($"Định mức BOM: Chi tiết mã {sku} yêu cầu cấu thành từ 2.5 kg phôi thép và 4 con bulong M8.");
                        break;

                    case 4: // Finance
                        list.Add($"Kế toán trưởng: Báo cáo số dư tiền mặt trong sổ quỹ và tài khoản ngân hàng hôm nay.");
                        list.Add($"Trợ lý tài chính: Quỹ tiền mặt còn {(rand.Next(20, 80) * 1000000):N0} VNĐ, tài khoản Techcombank có {(rand.Next(500, 2000) * 1000000):N0} VNĐ, công nợ phải thu trong tuần là {(rand.Next(100, 600) * 1000000):N0} VNĐ.");
                        list.Add($"Hóa đơn giá trị gia tăng: Đã phát hành hóa đơn VAT số 00{rand.Next(1000, 9999)} cho đối tác {cust} với thuế suất 8%.");
                        break;
                }
            }

            return list;
        }
    }
}
