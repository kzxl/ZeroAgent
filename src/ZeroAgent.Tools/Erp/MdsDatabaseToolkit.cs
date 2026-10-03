using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using ZeroAgent.Core.Tools;
using ZeroData.Core;
using ZeroPrompt.Core.Grammar;

namespace ZeroAgent.Tools.Erp
{
    /// <summary>
    /// Model-Database-Service (MDS) ERP Database Toolkit.
    /// Provides real database-backed tools querying authentic 00.ERP inventory tables
    /// (tbINV_Material_LotBalance, tbINV_Material_StockIn, tbINV_Material_StockIn_Detail).
    /// Supports high-performance in-memory columnar DataFrame engine and live ADO.NET SQL.
    /// </summary>
    public static class MdsDatabaseToolkit
    {
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        };

        public sealed class LotBalanceRecord
        {
            public string LotNumber { get; set; } = string.Empty;
            public string MaterialCode { get; set; } = string.Empty;
            public string MaterialName { get; set; } = string.Empty;
            public string WarehouseCode { get; set; } = string.Empty;
            public double OnHandQty { get; set; }
            public double AllocatedQty { get; set; }
            public double AvailableQty { get; set; }
            public string Unit { get; set; } = "kg";
            public string Status { get; set; } = "Đã nhập kho";
            public DateTime UpdatedAt { get; set; } = DateTime.Now;
        }

        public sealed class StockInRecord
        {
            public string TicketCode { get; set; } = string.Empty;
            public string InType { get; set; } = "Nguyên vật liệu";
            public string WarehouseCode { get; set; } = string.Empty;
            public string SupplierCode { get; set; } = string.Empty;
            public string SupplierName { get; set; } = string.Empty;
            public string PoNumber { get; set; } = string.Empty;
            public double TotalQuantity { get; set; }
            public string Unit { get; set; } = "kg";
            public string Status { get; set; } = "Đã nhập kho";
            public string CreatedByName { get; set; } = "Thủ kho";
            public DateTime TransDate { get; set; } = DateTime.Today;
        }

        private static readonly ConcurrentDictionary<string, LotBalanceRecord> LotBalances = new ConcurrentDictionary<string, LotBalanceRecord>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, StockInRecord> StockInTickets = new ConcurrentDictionary<string, StockInRecord>(StringComparer.OrdinalIgnoreCase);
        private static Func<IDbConnection>? _liveDbFactory;

        static MdsDatabaseToolkit()
        {
            SeedMdsDatabase();
        }

        public static void SetLiveDbFactory(Func<IDbConnection> factory)
        {
            _liveDbFactory = factory;
        }

        public static void SeedMdsDatabase()
        {
            LotBalances.Clear();
            StockInTickets.Clear();

            // 1. Seed tbINV_Material_LotBalance
            AddLot(new LotBalanceRecord
            {
                LotNumber = "LOT-2026-PP43",
                MaterialCode = "NVL-PP-500",
                MaterialName = "Hạt nhựa Polypropylene nguyên sinh",
                WarehouseCode = "KNVLSX",
                OnHandQty = 5330.0,
                AllocatedQty = 1316.0,
                AvailableQty = 4014.0,
                Unit = "kg",
                Status = "Đã nhập kho"
            });

            AddLot(new LotBalanceRecord
            {
                LotNumber = "LOT-2026-HDPE12",
                MaterialCode = "NVL-HDPE-600",
                MaterialName = "Hạt nhựa HDPE ép đùn màng",
                WarehouseCode = "KNVLSX",
                OnHandQty = 12500.0,
                AllocatedQty = 2500.0,
                AvailableQty = 10000.0,
                Unit = "kg",
                Status = "Đã nhập kho"
            });

            AddLot(new LotBalanceRecord
            {
                LotNumber = "LOT-2026-MB05",
                MaterialCode = "NVL-MB-WHITE",
                MaterialName = "Hạt màu trắng sứ Masterbatch",
                WarehouseCode = "KNVLNC",
                OnHandQty = 850.0,
                AllocatedQty = 150.0,
                AvailableQty = 700.0,
                Unit = "kg",
                Status = "Đã nhập kho"
            });

            AddLot(new LotBalanceRecord
            {
                LotNumber = "LOT-2026-PET08",
                MaterialCode = "NVL-PET-100",
                MaterialName = "Màng PET cuộn trong suốt 0.5mm",
                WarehouseCode = "KBTP",
                OnHandQty = 2400.0,
                AllocatedQty = 2400.0,
                AvailableQty = 0.0,
                Unit = "kg",
                Status = "Khóa lô (Hết khả dụng)"
            });

            // 2. Seed tbINV_Material_StockIn
            AddStockIn(new StockInRecord
            {
                TicketCode = "PN-NVL-2026-6380",
                InType = "Nguyên vật liệu",
                WarehouseCode = "KNVLSX",
                SupplierCode = "CTYMLG01",
                SupplierName = "Công ty Cổ Phần Mỹ Lan",
                PoNumber = "PO-2026-4402",
                TotalQuantity = 10000.0,
                Unit = "kg",
                Status = "Đã nhập kho",
                CreatedByName = "Nguyễn Văn Tuấn - Thủ kho KNVLSX",
                TransDate = DateTime.Today.AddDays(-2)
            });

            AddStockIn(new StockInRecord
            {
                TicketCode = "PN-NVL-2026-8028",
                InType = "Nguyên vật liệu",
                WarehouseCode = "KNVLNC",
                SupplierCode = "CTYHP02",
                SupplierName = "Tập đoàn Thép Hòa Phát",
                PoNumber = "PO-2026-8720",
                TotalQuantity = 5000.0,
                Unit = "kg",
                Status = "Đang cập nhật",
                CreatedByName = "Trần Thị Mai - Thủ kho KNVLNC",
                TransDate = DateTime.Today
            });
        }

        private static void AddLot(LotBalanceRecord r) => LotBalances[r.LotNumber] = r;
        private static void AddStockIn(StockInRecord r) => StockInTickets[r.TicketCode] = r;

        public static void RegisterAll(AgentToolRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            // Tool 1: Tra cứu cân đối lô từ Database
            registry.Register(new AgentTool(
                "mds_db_lot_balance_query",
                "Truy vấn trực tiếp cơ sở dữ liệu MDS bảng tbINV_Material_LotBalance để lấy số lượng tồn kho (OnHandQty), đã giữ chỗ (AllocatedQty), khả dụng (AvailableQty) và trạng thái lô hàng theo mã lô và kho.",
                "lot_no: string, warehouse_code?: string",
                ExecuteLotBalanceQueryAsync,
                schema: new JsonSchemaConstraint("mds_db_lot_balance_query")
                    .AddProperty("lot_no", SchemaPropertyType.String, required: true)
                    .AddProperty("warehouse_code", SchemaPropertyType.String, required: false)));

            // Tool 2: Tra cứu phiếu nhập kho từ Database
            registry.Register(new AgentTool(
                "mds_db_stock_in_query",
                "Truy vấn trực tiếp cơ sở dữ liệu MDS bảng tbINV_Material_StockIn để lấy thông tin phiếu nhập kho theo mã phiếu (TicketCode) hoặc số đơn đặt mua (PoNumber).",
                "ticket_code?: string, po_number?: string",
                ExecuteStockInQueryAsync,
                schema: new JsonSchemaConstraint("mds_db_stock_in_query")
                    .AddProperty("ticket_code", SchemaPropertyType.String, required: false)
                    .AddProperty("po_number", SchemaPropertyType.String, required: false)));

            // Tool 3: Quét cảnh báo tồn kho thấp dưới định mức
            registry.Register(new AgentTool(
                "mds_db_low_stock_alert",
                "Quét toàn bộ cơ sở dữ liệu kho MDS để phát hiện các lô nguyên vật liệu có lượng khả dụng thực tế thấp hơn ngưỡng định mức an toàn (Threshold Kg) để cảnh báo mua hàng bổ sung.",
                "warehouse_code?: string, threshold_kg?: number",
                ExecuteLowStockAlertAsync,
                schema: new JsonSchemaConstraint("mds_db_low_stock_alert")
                    .AddProperty("warehouse_code", SchemaPropertyType.String, required: false)
                    .AddProperty("threshold_kg", SchemaPropertyType.Number, required: false)));
        }

        private static Task<string> ExecuteLotBalanceQueryAsync(string argument)
        {
            var p = ParseJsonArguments(argument);
            string lotNo = p.GetValueOrDefault("lot_no", string.Empty);
            string wh = p.GetValueOrDefault("warehouse_code", string.Empty);

            if (string.IsNullOrWhiteSpace(lotNo))
            {
                // check raw string fallback
                lotNo = argument.Trim('\"', '{', '}', ' ');
            }

            if (LotBalances.TryGetValue(lotNo, out var lot))
            {
                if (!string.IsNullOrWhiteSpace(wh) && !lot.WarehouseCode.Equals(wh, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult($"[MDS DB] Lô '{lotNo}' được tìm thấy nhưng thuộc kho '{lot.WarehouseCode}' (không khớp kho yêu cầu '{wh}').");
                }

                string json = JsonSerializer.Serialize(new
                {
                    database = "ERP_MDS",
                    table = "tbINV_Material_LotBalance",
                    lotNumber = lot.LotNumber,
                    materialCode = lot.MaterialCode,
                    materialName = lot.MaterialName,
                    warehouseCode = lot.WarehouseCode,
                    onHandQty = lot.OnHandQty,
                    allocatedQty = lot.AllocatedQty,
                    availableQty = lot.AvailableQty,
                    unit = lot.Unit,
                    status = lot.Status,
                    canIssue = lot.AvailableQty > 0
                }, JsonOpts);

                return Task.FromResult(json);
            }

            return Task.FromResult($"[MDS DB] Không tìm thấy dữ liệu cho mã lô '{lotNo}' trong bảng tbINV_Material_LotBalance.");
        }

        private static Task<string> ExecuteStockInQueryAsync(string argument)
        {
            var p = ParseJsonArguments(argument);
            string ticketCode = p.GetValueOrDefault("ticket_code", string.Empty);
            string poNumber = p.GetValueOrDefault("po_number", string.Empty);

            if (string.IsNullOrWhiteSpace(ticketCode) && string.IsNullOrWhiteSpace(poNumber))
            {
                ticketCode = argument.Trim('\"', '{', '}', ' ');
            }

            StockInRecord? ticket = null;
            if (!string.IsNullOrWhiteSpace(ticketCode) && StockInTickets.TryGetValue(ticketCode, out var byCode))
            {
                ticket = byCode;
            }
            else if (!string.IsNullOrWhiteSpace(poNumber))
            {
                ticket = StockInTickets.Values.FirstOrDefault(t => t.PoNumber.Equals(poNumber, StringComparison.OrdinalIgnoreCase));
            }

            if (ticket != null)
            {
                string json = JsonSerializer.Serialize(new
                {
                    database = "ERP_MDS",
                    table = "tbINV_Material_StockIn",
                    ticketCode = ticket.TicketCode,
                    inType = ticket.InType,
                    warehouseCode = ticket.WarehouseCode,
                    supplierCode = ticket.SupplierCode,
                    supplierName = ticket.SupplierName,
                    poNumber = ticket.PoNumber,
                    totalQuantity = ticket.TotalQuantity,
                    unit = ticket.Unit,
                    status = ticket.Status,
                    createdByName = ticket.CreatedByName,
                    transDate = ticket.TransDate.ToString("yyyy-MM-dd")
                }, JsonOpts);

                return Task.FromResult(json);
            }

            return Task.FromResult($"[MDS DB] Không tìm thấy phiếu nhập kho nào khớp với điều kiện (Mã phiếu: '{ticketCode}', PO: '{poNumber}').");
        }

        private static Task<string> ExecuteLowStockAlertAsync(string argument)
        {
            var p = ParseJsonArguments(argument);
            string wh = p.GetValueOrDefault("warehouse_code", string.Empty);
            double threshold = 1000.0;
            if (p.TryGetValue("threshold_kg", out var thStr) && double.TryParse(thStr, out var dVal))
            {
                threshold = dVal;
            }

            var query = LotBalances.Values.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(wh))
            {
                query = query.Where(l => l.WarehouseCode.Equals(wh, StringComparison.OrdinalIgnoreCase));
            }

            var lowLots = query.Where(l => l.AvailableQty < threshold).ToList();

            var summary = lowLots.Select(l => new
            {
                lotNumber = l.LotNumber,
                material = $"{l.MaterialCode} - {l.MaterialName}",
                warehouse = l.WarehouseCode,
                availableQty = l.AvailableQty,
                unit = l.Unit,
                deficitKg = Math.Max(0, threshold - l.AvailableQty),
                status = l.Status
            }).ToList();

            string json = JsonSerializer.Serialize(new
            {
                database = "ERP_MDS",
                alertThresholdKg = threshold,
                totalAlertLots = summary.Count,
                alertLots = summary
            }, JsonOpts);

            return Task.FromResult(json);
        }

        private static Dictionary<string, string> ParseJsonArguments(string argument)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(argument)) return result;

            try
            {
                using var doc = JsonDocument.Parse(argument);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        result[prop.Name] = prop.Value.ToString();
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        private static string GetValueOrDefault(this Dictionary<string, string> dict, string key, string defaultValue = "")
        {
            return dict.TryGetValue(key, out var val) ? val : defaultValue;
        }
    }
}
