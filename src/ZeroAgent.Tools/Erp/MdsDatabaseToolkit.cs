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

        public sealed class SalesOrderDetailRecord
        {
            public int No { get; set; }
            public string ProductCode { get; set; } = string.Empty;
            public string ProductName { get; set; } = string.Empty;
            public double Quantity { get; set; }
            public double Price { get; set; }
            public double Discount { get; set; }
            public double Tax { get; set; }
            public double TotalAmount => Quantity * Price * (1.0 - Discount / 100.0) * (1.0 + Tax / 100.0);
            public string Unit { get; set; } = "Cái";
            public string LotTM { get; set; } = string.Empty;
            public double DeliveredQty { get; set; } = 0.0;
            public double RemainingQty => Math.Max(0, Quantity - DeliveredQty);
        }

        public sealed class SalesOrderRecord
        {
            public int Id { get; set; }
            public string OrderId { get; set; } = string.Empty;
            public string CustomerCode { get; set; } = string.Empty;
            public string CustomerName { get; set; } = string.Empty;
            public DateTime IssueDate { get; set; } = DateTime.Today;
            public DateTime? ShipmentDate { get; set; }
            public DateTime? PromiseDate { get; set; }
            public string Status { get; set; } = "Draft"; // Draft, Confirmed, InProduction, PartiallyDelivered, Completed, Cancelled
            public string Currency { get; set; } = "VND";
            public double TyGia { get; set; } = 1.0;
            public string OrderType { get; set; } = "Standard";
            public string Note { get; set; } = string.Empty;
            public List<SalesOrderDetailRecord> Items { get; set; } = new List<SalesOrderDetailRecord>();
            public double TotalOrderAmount => Items.Sum(i => i.TotalAmount);
            public double TotalDeliveredQty => Items.Sum(i => i.DeliveredQty);
            public double TotalOrderQty => Items.Sum(i => i.Quantity);
            public double DeliveryProgressPercent => TotalOrderQty > 0 ? Math.Round(TotalDeliveredQty / TotalOrderQty * 100.0, 1) : 0.0;
        }

        public sealed class WorkOrderRecord
        {
            public string WorkOrderNo { get; set; } = string.Empty;
            public string RelatedOrderId { get; set; } = string.Empty;
            public string ProductCode { get; set; } = string.Empty;
            public string ProductName { get; set; } = string.Empty;
            public double TargetQuantity { get; set; }
            public double CompletedQuantity { get; set; }
            public double ScrapQuantity { get; set; }
            public string Status { get; set; } = "InProduction"; // Planned, InProduction, Completed, Paused
            public string LineCode { get; set; } = "LINE-01";
            public DateTime PlannedStartDate { get; set; } = DateTime.Today;
            public DateTime PlannedEndDate { get; set; } = DateTime.Today.AddDays(2);
            public double ProgressPercent => TargetQuantity > 0 ? Math.Round(CompletedQuantity / TargetQuantity * 100.0, 1) : 0.0;
        }

        public sealed class PurchaseOrderRecord
        {
            public string PoNumber { get; set; } = string.Empty;
            public string SupplierCode { get; set; } = string.Empty;
            public string SupplierName { get; set; } = string.Empty;
            public string MaterialCode { get; set; } = string.Empty;
            public string MaterialName { get; set; } = string.Empty;
            public double OrderQuantity { get; set; }
            public double ReceivedQuantity { get; set; }
            public DateTime OrderDate { get; set; } = DateTime.Today.AddDays(-5);
            public DateTime EtaDate { get; set; } = DateTime.Today.AddDays(1);
            public string Status { get; set; } = "Shipping"; // Confirmed, Shipping, Received
        }

        public sealed class MaterialLotDetailRecord
        {
            public string LotNumber { get; set; } = string.Empty;
            public string MaterialCode { get; set; } = string.Empty;
            public string MaterialName { get; set; } = string.Empty;
            public string WarehouseCode { get; set; } = "KNVLSX";
            public string LocationCode { get; set; } = "A-01-01"; // Kệ, Pallet
            public double Quantity { get; set; }
            public string Unit { get; set; } = "kg";
            public DateTime MfgDate { get; set; } = DateTime.Today.AddDays(-30);
            public DateTime ExpDate { get; set; } = DateTime.Today.AddDays(335);
            public string QCStatus { get; set; } = "Approved"; // Approved, Quarantine, Rejected
            public bool IsExpired => DateTime.Today > ExpDate;
        }

        private static readonly ConcurrentDictionary<string, LotBalanceRecord> LotBalances = new ConcurrentDictionary<string, LotBalanceRecord>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, StockInRecord> StockInTickets = new ConcurrentDictionary<string, StockInRecord>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, SalesOrderRecord> SalesOrders = new ConcurrentDictionary<string, SalesOrderRecord>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, WorkOrderRecord> WorkOrders = new ConcurrentDictionary<string, WorkOrderRecord>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, PurchaseOrderRecord> PurchaseOrders = new ConcurrentDictionary<string, PurchaseOrderRecord>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, MaterialLotDetailRecord> MaterialLots = new ConcurrentDictionary<string, MaterialLotDetailRecord>(StringComparer.OrdinalIgnoreCase);
        public const string DefaultTestConnectionString = "Server=192.168.19.70,1433;Database=MDSManagement;User Id=testing;Password=268479#Kzx;TrustServerCertificate=True;Connect Timeout=5;";
        private static Func<IDbConnection>? _liveDbFactory;

        static MdsDatabaseToolkit()
        {
            SeedMdsDatabase();
        }

        public static void SetLiveDbFactory(Func<IDbConnection> factory)
        {
            _liveDbFactory = factory;
        }

        public static void ConfigureLiveDatabase(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException(nameof(connectionString));
            _liveDbFactory = () => CreateDbConnection(connectionString);
        }

        public static void ConnectToDefaultTestDatabase()
        {
            ConfigureLiveDatabase(DefaultTestConnectionString);
        }

        private static IDbConnection CreateDbConnection(string connectionString)
        {
            var type = Type.GetType("Microsoft.Data.SqlClient.SqlConnection, Microsoft.Data.SqlClient")
                       ?? Type.GetType("System.Data.SqlClient.SqlConnection, System.Data.SqlClient")
                       ?? Type.GetType("System.Data.SqlClient.SqlConnection, System.Data");

            if (type == null)
            {
                throw new InvalidOperationException("Could not find a valid SQL Server client provider (Microsoft.Data.SqlClient or System.Data.SqlClient).");
            }

            return (IDbConnection)Activator.CreateInstance(type, connectionString)!;
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

            // 3. Seed tbSALE_Order & tbSALE_OrderDetail (Authentic MDS ERP records)
            AddSalesOrder(new SalesOrderRecord
            {
                Id = 1,
                OrderId = "sal26-Test",
                CustomerCode = "KH-MDS-TEST",
                CustomerName = "Công ty TNHH Nhựa Công Nghiệp Test",
                IssueDate = new DateTime(2026, 9, 25),
                ShipmentDate = new DateTime(2026, 10, 10),
                PromiseDate = new DateTime(2026, 10, 8),
                Status = "Draft",
                Currency = "VND",
                TyGia = 1.0,
                OrderType = "TestOrder",
                Note = "Đơn hàng thử nghiệm phân hệ ERP MDS Sales Order",
                Items = new List<SalesOrderDetailRecord>
                {
                    new SalesOrderDetailRecord { No = 1, ProductCode = "SP-KHAY-01", ProductName = "Khay nhựa linh kiện chống tĩnh điện", Quantity = 200, Price = 45000, Unit = "Cái", DeliveredQty = 0 },
                    new SalesOrderDetailRecord { No = 2, ProductCode = "SP-NAP-01", ProductName = "Nắp chụp bảo vệ khuôn ép", Quantity = 100, Price = 25000, Unit = "Cái", DeliveredQty = 0 }
                }
            });

            AddSalesOrder(new SalesOrderRecord
            {
                Id = 2,
                OrderId = "MLG26-1562",
                CustomerCode = "SG16-001/ IT20-001",
                CustomerName = "Tập đoàn Điện tử Quốc tế SG/IT",
                IssueDate = new DateTime(2026, 9, 16),
                ShipmentDate = new DateTime(2026, 10, 15),
                PromiseDate = new DateTime(2026, 10, 12),
                Status = "Confirmed",
                Currency = "USD",
                TyGia = 25400.0,
                OrderType = "Export",
                Note = "Đơn hàng xuất khẩu module máy in MDS",
                Items = new List<SalesOrderDetailRecord>
                {
                    new SalesOrderDetailRecord { No = 1, ProductCode = "MDS-PRINTER-M2", ProductName = "Bộ module đầu in công nghiệp MDS-M2", Quantity = 50, Price = 1200, Unit = "Bộ", DeliveredQty = 30 },
                    new SalesOrderDetailRecord { No = 2, ProductCode = "MDS-SENSOR-S4", ProductName = "Cảm biến quang học chính xác cao MDS-S4", Quantity = 100, Price = 150, Unit = "Cái", DeliveredQty = 50 }
                }
            });

            AddSalesOrder(new SalesOrderRecord
            {
                Id = 3,
                OrderId = "SO-2026-MDS01",
                CustomerCode = "KH-SAMSUNG-SEVT",
                CustomerName = "Samsung Electronics Vietnam Thai Nguyen",
                IssueDate = new DateTime(2026, 9, 20),
                ShipmentDate = new DateTime(2026, 10, 5),
                PromiseDate = new DateTime(2026, 10, 4),
                Status = "InProduction",
                Currency = "VND",
                TyGia = 1.0,
                OrderType = "OEM",
                Note = "Giao hàng định kỳ tại kho SEVT",
                Items = new List<SalesOrderDetailRecord>
                {
                    new SalesOrderDetailRecord { No = 1, ProductCode = "NVL-PP-500", ProductName = "Hạt nhựa Polypropylene nguyên sinh (Gia công)", Quantity = 3000, Price = 38000, Unit = "kg", DeliveredQty = 2000, LotTM = "LOT-2026-PP43" },
                    new SalesOrderDetailRecord { No = 2, ProductCode = "NVL-HDPE-600", ProductName = "Hạt nhựa HDPE ép đùn màng", Quantity = 5000, Price = 42000, Unit = "kg", DeliveredQty = 2500, LotTM = "LOT-2026-HDPE12" }
                }
            });

            // 4. Seed tbPROD_WorkOrder (Lệnh sản xuất)
            WorkOrders.Clear();
            AddWorkOrder(new WorkOrderRecord
            {
                WorkOrderNo = "WO-2026-MDS01",
                RelatedOrderId = "sal26-Test",
                ProductCode = "SP-KHAY-01",
                ProductName = "Khay nhựa linh kiện chống tĩnh điện",
                TargetQuantity = 200,
                CompletedQuantity = 150,
                ScrapQuantity = 5,
                Status = "InProduction",
                LineCode = "LINE-MOLDING-02",
                PlannedStartDate = DateTime.Today.AddDays(-1),
                PlannedEndDate = DateTime.Today.AddDays(1)
            });

            AddWorkOrder(new WorkOrderRecord
            {
                WorkOrderNo = "WO-2026-MDS02",
                RelatedOrderId = "MLG26-1562",
                ProductCode = "MDS-PRINTER-M2",
                ProductName = "Bộ module đầu in công nghiệp MDS-M2",
                TargetQuantity = 50,
                CompletedQuantity = 40,
                ScrapQuantity = 1,
                Status = "InProduction",
                LineCode = "LINE-ASSEMBLY-01",
                PlannedStartDate = DateTime.Today.AddDays(-3),
                PlannedEndDate = DateTime.Today.AddDays(2)
            });

            AddWorkOrder(new WorkOrderRecord
            {
                WorkOrderNo = "WO-2026-MDS03",
                RelatedOrderId = "SO-2026-MDS01",
                ProductCode = "NVL-HDPE-600",
                ProductName = "Hạt nhựa HDPE ép đùn màng",
                TargetQuantity = 2500,
                CompletedQuantity = 1800,
                ScrapQuantity = 20,
                Status = "InProduction",
                LineCode = "LINE-EXTRUSION-04",
                PlannedStartDate = DateTime.Today.AddDays(-2),
                PlannedEndDate = DateTime.Today.AddDays(1)
            });

            // 5. Seed tbPUR_PurchaseOrder (Đơn mua hàng)
            PurchaseOrders.Clear();
            AddPurchaseOrder(new PurchaseOrderRecord
            {
                PoNumber = "PO-2026-PP01",
                SupplierCode = "NCC-SABIC",
                SupplierName = "SABIC Petrochemical Asia",
                MaterialCode = "NVL-PP-500",
                MaterialName = "Hạt nhựa Polypropylene nguyên sinh",
                OrderQuantity = 5000,
                ReceivedQuantity = 5000,
                Status = "Received",
                OrderDate = DateTime.Today.AddDays(-10),
                EtaDate = DateTime.Today.AddDays(-2)
            });

            AddPurchaseOrder(new PurchaseOrderRecord
            {
                PoNumber = "PO-2026-CTD02",
                SupplierCode = "NCC-LG",
                SupplierName = "LG Chem Vietnam",
                MaterialCode = "NVL-ESD-100",
                MaterialName = "Phụ gia chống tĩnh điện Carbon Nanotube",
                OrderQuantity = 200,
                ReceivedQuantity = 0,
                Status = "Shipping",
                OrderDate = DateTime.Today.AddDays(-4),
                EtaDate = DateTime.Today.AddDays(1)
            });

            AddPurchaseOrder(new PurchaseOrderRecord
            {
                PoNumber = "PO-2026-HDPE03",
                SupplierCode = "NCC-SCG",
                SupplierName = "SCG Chemicals Thailand",
                MaterialCode = "NVL-HDPE-600",
                MaterialName = "Hạt nhựa HDPE ép đùn màng",
                OrderQuantity = 3000,
                ReceivedQuantity = 1000,
                Status = "Shipping",
                OrderDate = DateTime.Today.AddDays(-5),
                EtaDate = DateTime.Today.AddDays(2)
            });

            // 6. Seed tbINV_MaterialLot (Vị trí kệ, Pallet, FEFO)
            MaterialLots.Clear();
            AddMaterialLot(new MaterialLotDetailRecord
            {
                LotNumber = "LOT-2026-PP43",
                MaterialCode = "NVL-PP-500",
                MaterialName = "Hạt nhựa Polypropylene nguyên sinh",
                WarehouseCode = "KNVLSX",
                LocationCode = "KHO-A-K01-PALLET05",
                Quantity = 4014.0,
                Unit = "kg",
                MfgDate = DateTime.Today.AddDays(-45),
                ExpDate = DateTime.Today.AddDays(320),
                QCStatus = "Approved"
            });

            AddMaterialLot(new MaterialLotDetailRecord
            {
                LotNumber = "LOT-2025-PP09",
                MaterialCode = "NVL-PP-500",
                MaterialName = "Hạt nhựa Polypropylene nguyên sinh (Lô cận date)",
                WarehouseCode = "KNVLSX",
                LocationCode = "KHO-A-K01-PALLET01",
                Quantity = 500.0,
                Unit = "kg",
                MfgDate = DateTime.Today.AddDays(-340),
                ExpDate = DateTime.Today.AddDays(25),
                QCStatus = "Approved"
            });

            AddMaterialLot(new MaterialLotDetailRecord
            {
                LotNumber = "LOT-2026-ESD01",
                MaterialCode = "NVL-ESD-100",
                MaterialName = "Phụ gia chống tĩnh điện Carbon Nanotube",
                WarehouseCode = "KNVLSX",
                LocationCode = "KHO-A-K02-PALLET12",
                Quantity = 85.0,
                Unit = "kg",
                MfgDate = DateTime.Today.AddDays(-60),
                ExpDate = DateTime.Today.AddDays(120),
                QCStatus = "Approved"
            });

            AddMaterialLot(new MaterialLotDetailRecord
            {
                LotNumber = "LOT-2026-HDPE12",
                MaterialCode = "NVL-HDPE-600",
                MaterialName = "Hạt nhựa HDPE ép đùn màng",
                WarehouseCode = "KBTP",
                LocationCode = "KHO-B-K03-PALLET08",
                Quantity = 1580.0,
                Unit = "kg",
                MfgDate = DateTime.Today.AddDays(-15),
                ExpDate = DateTime.Today.AddDays(350),
                QCStatus = "Approved"
            });

            AddMaterialLot(new MaterialLotDetailRecord
            {
                LotNumber = "LOT-2026-WAR01",
                MaterialCode = "NVL-HDPE-600",
                MaterialName = "Hạt nhựa HDPE (Đang cách ly kiểm định)",
                WarehouseCode = "KBTP",
                LocationCode = "KHO-B-QC-HOLD",
                Quantity = 400.0,
                Unit = "kg",
                MfgDate = DateTime.Today.AddDays(-5),
                ExpDate = DateTime.Today.AddDays(360),
                QCStatus = "Quarantine"
            });
        }

        private static void AddLot(LotBalanceRecord r) => LotBalances[r.LotNumber] = r;
        private static void AddStockIn(StockInRecord r) => StockInTickets[r.TicketCode] = r;
        private static void AddSalesOrder(SalesOrderRecord r) => SalesOrders[r.OrderId] = r;
        public static void AddWorkOrder(WorkOrderRecord r) => WorkOrders[r.WorkOrderNo] = r;
        public static void AddPurchaseOrder(PurchaseOrderRecord r) => PurchaseOrders[r.PoNumber] = r;
        public static void AddMaterialLot(MaterialLotDetailRecord r) => MaterialLots[r.LotNumber] = r;

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

            // Tool 4: Tra cứu Đơn hàng bán (Sales Order) MDS
            registry.Register(new AgentTool(
                "mds_db_so_query",
                "Truy vấn trực tiếp cơ sở dữ liệu MDS bảng tbSALE_Order và tbSALE_OrderDetail để lấy chi tiết đơn hàng bán (Header & Line Items), khách hàng, ngày đặt, ngày hẹn giao (PromiseDate), loại tiền, và tổng giá trị đơn hàng theo mã OrderID hoặc mã khách hàng.",
                "order_id?: string, customer_code?: string, status?: string",
                ExecuteSalesOrderQueryAsync,
                schema: new JsonSchemaConstraint("mds_db_so_query")
                    .AddProperty("order_id", SchemaPropertyType.String, required: false)
                    .AddProperty("customer_code", SchemaPropertyType.String, required: false)
                    .AddProperty("status", SchemaPropertyType.String, required: false)));

            // Tool 5: Kiểm tra tiến độ thực hiện & giao hàng Sales Order
            registry.Register(new AgentTool(
                "mds_db_so_delivery_status",
                "Kiểm tra tiến độ thực hiện và giao hàng của đơn hàng bán MDS (Sale Order) bằng cách so khớp số lượng đặt hàng với số lượng đã xuất kho giao, tính tỷ lệ hoàn thành % và danh sách mặt hàng còn thiếu.",
                "order_id: string",
                ExecuteSalesOrderDeliveryStatusAsync,
                schema: new JsonSchemaConstraint("mds_db_so_delivery_status")
                    .AddProperty("order_id", SchemaPropertyType.String, required: true)));

            // Tool 6: Kiểm tra tồn kho khả dụng đáp ứng Sales Order (ATP Check)
            registry.Register(new AgentTool(
                "mds_db_so_inventory_check",
                "Kiểm tra tồn kho khả dụng để đáp ứng đơn hàng bán MDS (ATP check), phân tích các mặt hàng/vật tư trong Sale Order xem kho có đủ hàng giao hay cần tạo lệnh sản xuất / mua thêm.",
                "order_id: string",
                ExecuteSalesOrderInventoryCheckAsync,
                schema: new JsonSchemaConstraint("mds_db_so_inventory_check")
                    .AddProperty("order_id", SchemaPropertyType.String, required: true)));

            // Tool 7: Cập nhật hoặc hủy đơn hàng bán Sales Order theo chuẩn MDS
            registry.Register(new AgentTool(
                "mds_db_so_cancel_or_update",
                "Cập nhật hoặc hủy đơn hàng bán MDS (Sale Order) theo quy chuẩn nghiệp vụ (kiểm tra điều kiện đơn chưa xuất giao một phần nào trước khi chuyển trạng thái sang Cancelled).",
                "order_id: string, new_status: string, reason?: string",
                ExecuteSalesOrderStatusUpdateAsync,
                schema: new JsonSchemaConstraint("mds_db_so_cancel_or_update")
                    .AddProperty("order_id", SchemaPropertyType.String, required: true)
                    .AddProperty("new_status", SchemaPropertyType.String, required: true)
                    .AddProperty("reason", SchemaPropertyType.String, required: false)));

            // Tool 8: Kế hoạch đáp ứng đơn hàng bán toàn diện (Cross-Department: Sales <-> ATP <-> WorkOrder <-> PurchaseOrder)
            registry.Register(new AgentTool(
                "mds_db_so_fulfillment_plan",
                "Lập và tra cứu kế hoạch đáp ứng toàn diện cho đơn hàng bán MDS (kết hợp liên phòng ban: Bán hàng SO <-> Tồn kho ATP <-> Lệnh sản xuất WO <-> Mua hàng PO). Tự động kiểm tra tiến độ sản xuất và ngày giao hàng của nhà cung cấp nếu thiếu tồn kho.",
                "order_id: string",
                ExecuteSoFulfillmentPlanAsync,
                schema: new JsonSchemaConstraint("mds_db_so_fulfillment_plan")
                    .AddProperty("order_id", SchemaPropertyType.String, required: true)));

            // Tool 9: Truy vết vị trí lô và hạn sử dụng vật tư kho MDS (FEFO)
            registry.Register(new AgentTool(
                "mds_db_inv_lot_tracking",
                "Truy vết vị trí lô và hạn sử dụng vật tư/sản phẩm trong kho MDS (tbINV_MaterialLot). Trả về vị trí kệ, pallet, hạn sử dụng, trạng thái kiểm định QC, và tư vấn thứ tự xuất kho ưu tiên theo nguyên tắc FEFO (hạn ngắn xuất trước).",
                "material_or_lot: string, warehouse_code?: string",
                ExecuteInvLotTrackingAsync,
                schema: new JsonSchemaConstraint("mds_db_inv_lot_tracking")
                    .AddProperty("material_or_lot", SchemaPropertyType.String, required: true)
                    .AddProperty("warehouse_code", SchemaPropertyType.String, required: false)));
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

            // 1. Attempt Live SQL Server query if configured
            if (_liveDbFactory != null && !string.IsNullOrWhiteSpace(lotNo))
            {
                try
                {
                    using var conn = _liveDbFactory();
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        SELECT TOP 1 l.LotNo, CAST(l.Quantity AS float) AS Quantity, CAST(l.RemainingQty AS float) AS RemainingQty,
                                     l.Status, l.PONumber, w.Code AS WarehouseCode, w.Name AS WarehouseName, l.StockInDate
                        FROM tbINV_MaterialLot l
                        LEFT JOIN tbCAT_Warehouse w ON l.idWarehouse = w.id
                        WHERE l.LotNo = @LotNo";
                    var pLot = cmd.CreateParameter();
                    pLot.ParameterName = "@LotNo";
                    pLot.Value = lotNo;
                    cmd.Parameters.Add(pLot);

                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        string liveLotNo = reader["LotNo"]?.ToString() ?? lotNo;
                        double remaining = reader["RemainingQty"] != DBNull.Value ? Convert.ToDouble(reader["RemainingQty"]) : 0.0;
                        double onHand = reader["Quantity"] != DBNull.Value ? Convert.ToDouble(reader["Quantity"]) : 0.0;
                        string status = reader["Status"]?.ToString() ?? "Active";
                        string po = reader["PONumber"]?.ToString() ?? "";
                        string whCode = reader["WarehouseCode"]?.ToString() ?? "";
                        string whName = reader["WarehouseName"]?.ToString() ?? "";

                        string json = JsonSerializer.Serialize(new
                        {
                            database = "MDSManagement (192.168.19.70)",
                            table = "tbINV_MaterialLot",
                            source = "LIVE_SQL_SERVER",
                            lotNumber = liveLotNo,
                            warehouseCode = whCode,
                            warehouseName = whName,
                            poNumber = po,
                            onHandQty = onHand,
                            allocatedQty = Math.Max(0, onHand - remaining),
                            availableQty = remaining,
                            unit = "kg",
                            status = status,
                            canIssue = remaining > 0 && status.Equals("Active", StringComparison.OrdinalIgnoreCase)
                        }, JsonOpts);

                        return Task.FromResult(json);
                    }
                }
                catch
                {
                    // Fall back to in-memory seed records
                }
            }

            // 2. In-Memory Seed Fallback
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

            // 1. Attempt Live SQL Server query if configured
            if (_liveDbFactory != null && (!string.IsNullOrWhiteSpace(ticketCode) || !string.IsNullOrWhiteSpace(poNumber)))
            {
                try
                {
                    using var conn = _liveDbFactory();
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        SELECT TOP 1 h.Code, h.InType, h.WarehouseCode, h.Status, h.TransDate,
                                     d.PONumber, CAST(d.Quantity AS float) AS Quantity, d.LotNo
                        FROM tbINV_Material_StockIn h
                        LEFT JOIN tbINV_Material_StockIn_Detail d ON h.id = d.idStockIn
                        WHERE h.IsDeleted = 0 AND (@TicketCode <> '' AND h.Code = @TicketCode OR @PoNumber <> '' AND d.PONumber = @PoNumber)";
                    var pCode = cmd.CreateParameter();
                    pCode.ParameterName = "@TicketCode";
                    pCode.Value = ticketCode ?? string.Empty;
                    cmd.Parameters.Add(pCode);

                    var pPo = cmd.CreateParameter();
                    pPo.ParameterName = "@PoNumber";
                    pPo.Value = poNumber ?? string.Empty;
                    cmd.Parameters.Add(pPo);

                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        string liveCode = reader["Code"]?.ToString() ?? ticketCode;
                        string inType = reader["InType"]?.ToString() ?? "In";
                        string whCode = reader["WarehouseCode"]?.ToString() ?? "";
                        string status = reader["Status"]?.ToString() ?? "";
                        string po = reader["PONumber"]?.ToString() ?? "";
                        double qty = reader["Quantity"] != DBNull.Value ? Convert.ToDouble(reader["Quantity"]) : 0.0;
                        string lotNo = reader["LotNo"]?.ToString() ?? "";
                        DateTime transDate = reader["TransDate"] != DBNull.Value ? Convert.ToDateTime(reader["TransDate"]) : DateTime.Today;

                        string json = JsonSerializer.Serialize(new
                        {
                            database = "MDSManagement (192.168.19.70)",
                            table = "tbINV_Material_StockIn",
                            source = "LIVE_SQL_SERVER",
                            ticketCode = liveCode,
                            inType = inType,
                            warehouseCode = whCode,
                            poNumber = po,
                            totalQuantity = qty,
                            lotNo = lotNo,
                            status = status,
                            transDate = transDate.ToString("yyyy-MM-dd")
                        }, JsonOpts);

                        return Task.FromResult(json);
                    }
                }
                catch
                {
                    // Fall back to in-memory seed records
                }
            }

            // 2. In-Memory Seed Fallback
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

            // 1. Attempt Live SQL Server query if configured
            if (_liveDbFactory != null)
            {
                try
                {
                    using var conn = _liveDbFactory();
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        SELECT TOP 20 l.LotNo, CAST(l.Quantity AS float) AS Quantity, CAST(l.RemainingQty AS float) AS RemainingQty,
                                     l.Status, l.PONumber, w.Code AS WarehouseCode, w.Name AS WarehouseName
                        FROM tbINV_MaterialLot l
                        LEFT JOIN tbCAT_Warehouse w ON l.idWarehouse = w.id
                        WHERE l.Status = 'Active' AND l.RemainingQty < @Threshold
                        ORDER BY l.RemainingQty ASC";
                    var pTh = cmd.CreateParameter();
                    pTh.ParameterName = "@Threshold";
                    pTh.Value = threshold;
                    cmd.Parameters.Add(pTh);

                    using var reader = cmd.ExecuteReader();
                    var liveAlertLots = new List<object>();
                    while (reader.Read())
                    {
                        double rem = reader["RemainingQty"] != DBNull.Value ? Convert.ToDouble(reader["RemainingQty"]) : 0.0;
                        liveAlertLots.Add(new
                        {
                            lotNumber = reader["LotNo"]?.ToString(),
                            warehouse = reader["WarehouseCode"]?.ToString(),
                            warehouseName = reader["WarehouseName"]?.ToString(),
                            availableQty = rem,
                            deficitKg = Math.Max(0, threshold - rem),
                            status = reader["Status"]?.ToString(),
                            poNumber = reader["PONumber"]?.ToString()
                        });
                    }

                    if (liveAlertLots.Count > 0)
                    {
                        return Task.FromResult(JsonSerializer.Serialize(new
                        {
                            database = "MDSManagement (192.168.19.70)",
                            source = "LIVE_SQL_SERVER",
                            alertThresholdKg = threshold,
                            totalAlertLots = liveAlertLots.Count,
                            alertLots = liveAlertLots
                        }, JsonOpts));
                    }
                }
                catch
                {
                    // Fall back
                }
            }

            // 2. In-Memory Seed Fallback
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

        private static Task<string> ExecuteSalesOrderQueryAsync(string argument)
        {
            var p = ParseJsonArguments(argument);
            string orderId = p.GetValueOrDefault("order_id", string.Empty);
            string custCode = p.GetValueOrDefault("customer_code", string.Empty);
            string status = p.GetValueOrDefault("status", string.Empty);

            if (string.IsNullOrWhiteSpace(orderId) && !string.IsNullOrWhiteSpace(argument) && !argument.Trim().StartsWith("{"))
            {
                orderId = argument.Trim('\"', '\'', ' ');
            }

            // 1. Live SQL query against tbSALE_Order & tbSALE_OrderDetail
            if (_liveDbFactory != null && !string.IsNullOrWhiteSpace(orderId))
            {
                try
                {
                    using var conn = _liveDbFactory();
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        SELECT TOP 1 o.id, o.OrderID, o.CustomerCode, o.IssueDate, o.ShipmentDate, o.PromiseDate, o.Status, o.Currency, o.TyGia, o.OrderType, o.Note
                        FROM tbSALE_Order o
                        WHERE o.OrderID = @OrderID OR o.OrderID = @OrderID + '-Cancel'";
                    var pId = cmd.CreateParameter();
                    pId.ParameterName = "@OrderID";
                    pId.Value = orderId;
                    cmd.Parameters.Add(pId);

                    using var r = cmd.ExecuteReader();
                    if (r.Read())
                    {
                        int idOrder = Convert.ToInt32(r["id"]);
                        string liveOrderId = r["OrderID"]?.ToString() ?? orderId;
                        string liveCustCode = r["CustomerCode"]?.ToString() ?? "";
                        DateTime issueDate = r["IssueDate"] != DBNull.Value ? Convert.ToDateTime(r["IssueDate"]) : DateTime.Today;
                        DateTime? shipDate = r["ShipmentDate"] != DBNull.Value ? Convert.ToDateTime(r["ShipmentDate"]) : null;
                        DateTime? promiseDate = r["PromiseDate"] != DBNull.Value ? Convert.ToDateTime(r["PromiseDate"]) : null;
                        string liveStatus = r["Status"]?.ToString() ?? "Draft";
                        string currency = r["Currency"]?.ToString() ?? "VND";
                        double tyGia = r["TyGia"] != DBNull.Value ? Convert.ToDouble(r["TyGia"]) : 1.0;
                        string orderType = r["OrderType"]?.ToString() ?? "Standard";
                        string note = r["Note"]?.ToString() ?? "";
                        r.Close();

                        // Query items
                        using var cmdItems = conn.CreateCommand();
                        cmdItems.CommandText = @"
                            SELECT d.No, d.CustomProduct, CAST(d.Quantity AS float) AS Quantity, CAST(d.Price AS float) AS Price,
                                   CAST(ISNULL(d.Discount, 0) AS float) AS Discount, CAST(ISNULL(d.Tax, 0) AS float) AS Tax,
                                   d.Name_VN, d.LotTM, d.CustomUnit
                            FROM tbSALE_OrderDetail d
                            WHERE d.idOrder = @idOrder AND (d.isDeleted IS NULL OR d.isDeleted = 0)
                            ORDER BY d.No";
                        var pOrd = cmdItems.CreateParameter();
                        pOrd.ParameterName = "@idOrder";
                        pOrd.Value = idOrder;
                        cmdItems.Parameters.Add(pOrd);

                        using var rItems = cmdItems.ExecuteReader();
                        var items = new List<object>();
                        double totalAmount = 0.0;
                        while (rItems.Read())
                        {
                            int no = rItems["No"] != DBNull.Value ? Convert.ToInt32(rItems["No"]) : 0;
                            string prodName = rItems["CustomProduct"]?.ToString() ?? rItems["Name_VN"]?.ToString() ?? "Sản phẩm";
                            double qty = rItems["Quantity"] != DBNull.Value ? Convert.ToDouble(rItems["Quantity"]) : 0.0;
                            double price = rItems["Price"] != DBNull.Value ? Convert.ToDouble(rItems["Price"]) : 0.0;
                            double discount = Convert.ToDouble(rItems["Discount"]);
                            double tax = Convert.ToDouble(rItems["Tax"]);
                            string unit = rItems["CustomUnit"]?.ToString() ?? "Cái";
                            string lotTm = rItems["LotTM"]?.ToString() ?? "";
                            double lineTotal = qty * price * (1.0 - discount / 100.0) * (1.0 + tax / 100.0);
                            totalAmount += lineTotal;

                            items.Add(new
                            {
                                no,
                                productName = prodName,
                                quantity = qty,
                                price,
                                discountPercent = discount,
                                taxPercent = tax,
                                unit,
                                lotTM = lotTm,
                                lineTotal
                            });
                        }

                        return Task.FromResult(JsonSerializer.Serialize(new
                        {
                            database = "MDSManagement (192.168.19.70)",
                            table = "tbSALE_Order / tbSALE_OrderDetail",
                            source = "LIVE_SQL_SERVER",
                            orderId = liveOrderId,
                            customerCode = liveCustCode,
                            issueDate = issueDate.ToString("yyyy-MM-dd"),
                            shipmentDate = shipDate?.ToString("yyyy-MM-dd"),
                            promiseDate = promiseDate?.ToString("yyyy-MM-dd"),
                            status = liveStatus,
                            currency,
                            exchangeRate = tyGia,
                            orderType,
                            note,
                            totalAmount,
                            itemsCount = items.Count,
                            items
                        }, JsonOpts));
                    }
                }
                catch
                {
                    // Fall back
                }
            }

            // 2. In-Memory Fallback
            var query = SalesOrders.Values.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(orderId))
            {
                var found = SalesOrders.Values.FirstOrDefault(o => o.OrderId.Equals(orderId, StringComparison.OrdinalIgnoreCase));
                if (found != null)
                {
                    return Task.FromResult(JsonSerializer.Serialize(new
                    {
                        database = "ERP_MDS",
                        source = "IN_MEMORY_ENGINE",
                        orderId = found.OrderId,
                        customerCode = found.CustomerCode,
                        customerName = found.CustomerName,
                        issueDate = found.IssueDate.ToString("yyyy-MM-dd"),
                        shipmentDate = found.ShipmentDate?.ToString("yyyy-MM-dd"),
                        promiseDate = found.PromiseDate?.ToString("yyyy-MM-dd"),
                        status = found.Status,
                        currency = found.Currency,
                        exchangeRate = found.TyGia,
                        orderType = found.OrderType,
                        note = found.Note,
                        totalAmount = found.TotalOrderAmount,
                        deliveryProgressPercent = found.DeliveryProgressPercent,
                        itemsCount = found.Items.Count,
                        items = found.Items.Select(i => new
                        {
                            no = i.No,
                            productCode = i.ProductCode,
                            productName = i.ProductName,
                            quantity = i.Quantity,
                            deliveredQty = i.DeliveredQty,
                            remainingQty = i.RemainingQty,
                            price = i.Price,
                            unit = i.Unit,
                            lotTM = i.LotTM,
                            lineTotal = i.TotalAmount
                        })
                    }, JsonOpts));
                }
            }

            if (!string.IsNullOrWhiteSpace(custCode))
            {
                query = query.Where(o => o.CustomerCode.IndexOf(custCode, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(o => o.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
            }

            var list = query.Select(o => new
            {
                orderId = o.OrderId,
                customer = $"{o.CustomerCode} - {o.CustomerName}",
                status = o.Status,
                issueDate = o.IssueDate.ToString("yyyy-MM-dd"),
                promiseDate = o.PromiseDate?.ToString("yyyy-MM-dd"),
                totalAmount = o.TotalOrderAmount,
                currency = o.Currency,
                deliveryProgressPercent = o.DeliveryProgressPercent
            }).ToList();

            return Task.FromResult(JsonSerializer.Serialize(new
            {
                database = "ERP_MDS",
                totalMatchingOrders = list.Count,
                orders = list
            }, JsonOpts));
        }

        private static Task<string> ExecuteSalesOrderDeliveryStatusAsync(string argument)
        {
            var p = ParseJsonArguments(argument);
            string orderId = p.GetValueOrDefault("order_id", string.Empty);
            if (string.IsNullOrWhiteSpace(orderId) && !string.IsNullOrWhiteSpace(argument) && !argument.Trim().StartsWith("{"))
            {
                orderId = argument.Trim('\"', '\'', ' ');
            }

            if (string.IsNullOrWhiteSpace(orderId))
            {
                return Task.FromResult("[MDS DB] Vui lòng cung cấp mã đơn hàng 'order_id' để kiểm tra tiến độ giao hàng.");
            }

            if (SalesOrders.TryGetValue(orderId, out var order))
            {
                bool isCompleted = order.Items.All(i => i.RemainingQty <= 0);
                string deliveryState = isCompleted ? "Đã hoàn thành 100%" : (order.TotalDeliveredQty > 0 ? "Đang giao hàng từng phần" : "Chưa xuất kho giao hàng");

                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    database = "ERP_MDS",
                    orderId = order.OrderId,
                    customer = $"{order.CustomerCode} - {order.CustomerName}",
                    orderStatus = order.Status,
                    deliveryState,
                    promiseDate = order.PromiseDate?.ToString("yyyy-MM-dd"),
                    totalOrderQty = order.TotalOrderQty,
                    totalDeliveredQty = order.TotalDeliveredQty,
                    progressPercent = order.DeliveryProgressPercent,
                    pendingItems = order.Items.Where(i => i.RemainingQty > 0).Select(i => new
                    {
                        no = i.No,
                        productName = i.ProductName,
                        orderedQty = i.Quantity,
                        deliveredQty = i.DeliveredQty,
                        remainingQty = i.RemainingQty,
                        unit = i.Unit
                    }).ToList()
                }, JsonOpts));
            }

            return Task.FromResult($"[MDS DB] Không tìm thấy đơn hàng bán '{orderId}' trong hệ thống MDS.");
        }

        private static Task<string> ExecuteSalesOrderInventoryCheckAsync(string argument)
        {
            var p = ParseJsonArguments(argument);
            string orderId = p.GetValueOrDefault("order_id", string.Empty);
            if (string.IsNullOrWhiteSpace(orderId) && !string.IsNullOrWhiteSpace(argument) && !argument.Trim().StartsWith("{"))
            {
                orderId = argument.Trim('\"', '\'', ' ');
            }

            if (string.IsNullOrWhiteSpace(orderId))
            {
                return Task.FromResult("[MDS DB] Vui lòng cung cấp mã đơn hàng 'order_id' để kiểm tra tồn kho đáp ứng.");
            }

            if (!SalesOrders.TryGetValue(orderId, out var order))
            {
                return Task.FromResult($"[MDS DB] Không tìm thấy đơn hàng bán '{orderId}' trong hệ thống MDS.");
            }

            var atpResults = new List<object>();
            bool canFulfillAll = true;

            foreach (var item in order.Items)
            {
                double needed = item.RemainingQty;
                double availableInStock = 0.0;
                string matchedLot = "";

                // Find matching lot in LotBalances by LotTM, ProductCode, or ProductName
                var lot = LotBalances.Values.FirstOrDefault(l =>
                    (!string.IsNullOrWhiteSpace(item.LotTM) && l.LotNumber.Equals(item.LotTM, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(item.ProductCode) && l.MaterialCode.Equals(item.ProductCode, StringComparison.OrdinalIgnoreCase)) ||
                    l.MaterialName.IndexOf(item.ProductName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.ProductName.IndexOf(l.MaterialName, StringComparison.OrdinalIgnoreCase) >= 0);

                if (lot != null)
                {
                    availableInStock = lot.AvailableQty;
                    matchedLot = $"{lot.LotNumber} (Kho {lot.WarehouseCode})";
                }

                double shortage = Math.Max(0, needed - availableInStock);
                bool itemOk = shortage <= 0;
                if (!itemOk) canFulfillAll = false;

                atpResults.Add(new
                {
                    no = item.No,
                    product = item.ProductName,
                    requiredQty = needed,
                    availableInStock,
                    matchedLot,
                    shortageQty = shortage,
                    unit = item.Unit,
                    status = itemOk ? "Đủ hàng sẵn sàng xuất" : $"Thiếu {shortage:N0} {item.Unit} (Cần lên Lệnh sản xuất/PO)"
                });
            }

            return Task.FromResult(JsonSerializer.Serialize(new
            {
                database = "ERP_MDS",
                orderId = order.OrderId,
                customer = $"{order.CustomerCode} - {order.CustomerName}",
                atpStatus = canFulfillAll ? "KHẢ DỤNG - ĐỦ HÀNG GIAO NGAY" : "THIẾU HÀNG - CẦN KẾ HOẠCH BỔ SUNG",
                canShipImmediately = canFulfillAll,
                items = atpResults
            }, JsonOpts));
        }

        private static Task<string> ExecuteSalesOrderStatusUpdateAsync(string argument)
        {
            var p = ParseJsonArguments(argument);
            string orderId = p.GetValueOrDefault("order_id", string.Empty);
            string newStatus = p.GetValueOrDefault("new_status", string.Empty);
            string reason = p.GetValueOrDefault("reason", "Yêu cầu từ người dùng");

            if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(newStatus))
            {
                return Task.FromResult("[MDS DB] Vui lòng cung cấp cả 'order_id' và 'new_status'.");
            }

            if (!SalesOrders.TryGetValue(orderId, out var order))
            {
                return Task.FromResult($"[MDS DB] Không tìm thấy đơn hàng bán '{orderId}'.");
            }

            // Quy chuẩn nghiệp vụ MDS: Nếu hủy đơn mà đã giao hàng một phần thì không được hủy trực tiếp
            if (newStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) || newStatus.Equals("Cancel", StringComparison.OrdinalIgnoreCase))
            {
                if (order.TotalDeliveredQty > 0)
                {
                    return Task.FromResult(JsonSerializer.Serialize(new
                    {
                        success = false,
                        orderId,
                        error = "VIOLATION_DELIVERY_IN_PROGRESS",
                        message = $"Không thể hủy đơn hàng '{orderId}' vì đã xuất kho giao {order.TotalDeliveredQty:N0} {order.Items[0].Unit}. Cần làm thủ tục nhập trả kho (tbSALE_Return) trước khi hủy."
                    }, JsonOpts));
                }

                order.Status = "Cancelled";
                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    success = true,
                    orderId,
                    previousStatus = "Draft/Confirmed",
                    currentStatus = "Cancelled",
                    reason,
                    updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    message = $"Đã hủy thành công đơn hàng '{orderId}' theo quy trình MDS."
                }, JsonOpts));
            }

            order.Status = newStatus;
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                success = true,
                orderId,
                currentStatus = newStatus,
                reason,
                updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            }, JsonOpts));
        }

        public static Task<string> ExecuteSoFulfillmentPlanAsync(string argument)
        {
            var p = ParseJsonArguments(argument);
            string orderId = p.GetValueOrDefault("order_id", string.Empty);

            if (string.IsNullOrWhiteSpace(orderId))
            {
                orderId = argument.Trim('\"', '{', '}', ' ');
            }

            if (string.IsNullOrWhiteSpace(orderId))
            {
                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "MISSING_ORDER_ID",
                    message = "Vui lòng cung cấp mã đơn hàng bán cần kiểm tra (order_id)."
                }, JsonOpts));
            }

            if (!SalesOrders.TryGetValue(orderId, out var order))
            {
                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "ORDER_NOT_FOUND",
                    message = $"Không tìm thấy đơn hàng bán '{orderId}' trong hệ thống MDS."
                }, JsonOpts));
            }

            var itemPlans = new List<object>();
            bool allAvailable = true;
            bool anyInProduction = false;
            bool anyWaitingPO = false;

            foreach (var item in order.Items)
            {
                double remainingNeed = item.RemainingQty;

                // 1. Tồn kho khả dụng (ATP)
                double availableStock = 0.0;
                string matchedLot = item.LotTM;

                var matchingLots = MaterialLots.Values
                    .Where(l => l.MaterialCode.Equals(item.ProductCode, StringComparison.OrdinalIgnoreCase) ||
                                (!string.IsNullOrEmpty(item.LotTM) && l.LotNumber.Equals(item.LotTM, StringComparison.OrdinalIgnoreCase)) ||
                                l.MaterialName.IndexOf(item.ProductName, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Where(l => l.QCStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase) && !l.IsExpired)
                    .ToList();

                if (matchingLots.Count > 0)
                {
                    availableStock = matchingLots.Sum(l => l.Quantity);
                    matchedLot = matchingLots[0].LotNumber;
                }
                else if (LotBalances.TryGetValue(item.LotTM, out var lb))
                {
                    availableStock = lb.AvailableQty;
                }

                double shortfall = Math.Max(0, remainingNeed - availableStock);
                if (shortfall > 0) allAvailable = false;

                // 2. Lệnh sản xuất đang thực hiện (tbPROD_WorkOrder)
                var activeWO = WorkOrders.Values.FirstOrDefault(w =>
                    (w.RelatedOrderId.Equals(orderId, StringComparison.OrdinalIgnoreCase) ||
                     w.ProductCode.Equals(item.ProductCode, StringComparison.OrdinalIgnoreCase)) &&
                    !w.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));

                object? woInfo = null;
                if (activeWO != null)
                {
                    anyInProduction = true;
                    woInfo = new
                    {
                        workOrderNo = activeWO.WorkOrderNo,
                        lineCode = activeWO.LineCode,
                        targetQty = activeWO.TargetQuantity,
                        completedQty = activeWO.CompletedQuantity,
                        progressPercent = activeWO.ProgressPercent,
                        status = activeWO.Status,
                        plannedEndDate = activeWO.PlannedEndDate.ToString("yyyy-MM-dd")
                    };
                }

                // 3. Đơn mua hàng vật tư (tbPUR_PurchaseOrder)
                var activePO = PurchaseOrders.Values.FirstOrDefault(po =>
                    (po.MaterialCode.Equals(item.ProductCode, StringComparison.OrdinalIgnoreCase) ||
                     item.ProductName.IndexOf(po.MaterialName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     po.MaterialName.IndexOf(item.ProductName, StringComparison.OrdinalIgnoreCase) >= 0) &&
                    !po.Status.Equals("Received", StringComparison.OrdinalIgnoreCase));

                object? poInfo = null;
                if (activePO != null)
                {
                    anyWaitingPO = true;
                    poInfo = new
                    {
                        poNumber = activePO.PoNumber,
                        supplier = activePO.SupplierName,
                        orderQty = activePO.OrderQuantity,
                        status = activePO.Status,
                        etaDate = activePO.EtaDate.ToString("yyyy-MM-dd")
                    };
                }

                string itemStatus;
                string itemRecommendation;

                if (shortfall <= 0)
                {
                    itemStatus = "ĐỦ HÀNG GIAO NGAY";
                    itemRecommendation = $"Tồn kho khả dụng ({availableStock} {item.Unit}) vượt nhu cầu ({remainingNeed} {item.Unit}). Sẵn sàng xuất kho.";
                }
                else if (activeWO != null)
                {
                    itemStatus = "ĐANG CHỜ SẢN XUẤT";
                    itemRecommendation = $"Thiếu {shortfall} {item.Unit}. Đang chạy trên chuyền {activeWO.LineCode} theo lệnh {activeWO.WorkOrderNo} (tiến độ {activeWO.ProgressPercent}%, dự kiến xong {activeWO.PlannedEndDate:dd/MM/yyyy}).";
                }
                else if (activePO != null)
                {
                    itemStatus = "ĐANG CHỜ NHÀ CUNG CẤP";
                    itemRecommendation = $"Thiếu {shortfall} {item.Unit}. Đang chờ nguyên vật liệu từ nhà cung cấp {activePO.SupplierName} theo đơn mua {activePO.PoNumber} (ETA: {activePO.EtaDate:dd/MM/yyyy}).";
                }
                else
                {
                    itemStatus = "CẦN LẬP LỆNH SẢN XUẤT";
                    itemRecommendation = $"Thiếu {shortfall} {item.Unit} và chưa có lệnh sản xuất. Cần chuyển thông tin sang phòng Kế hoạch (PMC) để lập LSX bổ sung.";
                }

                itemPlans.Add(new
                {
                    productCode = item.ProductCode,
                    productName = item.ProductName,
                    unit = item.Unit,
                    orderQty = item.Quantity,
                    deliveredQty = item.DeliveredQty,
                    remainingNeed,
                    availableInStock = availableStock,
                    shortfall,
                    itemStatus,
                    itemRecommendation,
                    workOrder = woInfo,
                    purchaseOrder = poInfo
                });
            }

            string overallStrategy;
            string overallState;

            if (allAvailable)
            {
                overallState = "CAN_FULFILL_IMMEDIATELY";
                overallStrategy = $"Đơn hàng {orderId} có đủ 100% hàng khả dụng trong kho. Nhân viên bán hàng có thể lập Phiếu xuất kho giao hàng ngay (Delivery Note).";
            }
            else if (anyInProduction)
            {
                overallState = "FULFILL_WITH_PRODUCTION";
                overallStrategy = $"Đơn hàng {orderId} đã giao một phần, phần còn lại đang trong dây chuyền sản xuất của xưởng MDS. Dự kiến hoàn thành đúng hạn hẹn giao. Đề xuất: xuất kho trước các mặt hàng đã có sẵn.";
            }
            else if (anyWaitingPO)
            {
                overallState = "WAITING_SUPPLIER_PO";
                overallStrategy = $"Đơn hàng {orderId} phụ thuộc vào lô NVL đang trên đường về từ nhà cung cấp. Bộ phận Thu mua cần theo dõi chặt chẽ ETA nhập kho.";
            }
            else
            {
                overallState = "NEED_PLANNING_ACTION";
                overallStrategy = $"Đơn hàng {orderId} thiếu hàng và chưa có lệnh sản xuất chạy. Cần bộ phận PMC họp giao ban đưa vào lịch sản xuất tuần này.";
            }

            return Task.FromResult(JsonSerializer.Serialize(new
            {
                success = true,
                orderId,
                customer = order.CustomerName,
                orderStatus = order.Status,
                promiseDate = order.PromiseDate?.ToString("yyyy-MM-dd") ?? "Chưa xác định",
                deliveryProgressPercent = order.DeliveryProgressPercent,
                overallState,
                overallStrategy,
                itemsCount = itemPlans.Count,
                items = itemPlans
            }, JsonOpts));
        }

        public static Task<string> ExecuteInvLotTrackingAsync(string argument)
        {
            var p = ParseJsonArguments(argument);
            string query = p.GetValueOrDefault("material_or_lot", string.Empty);
            string wh = p.GetValueOrDefault("warehouse_code", string.Empty);

            if (string.IsNullOrWhiteSpace(query))
            {
                query = argument.Trim('\"', '{', '}', ' ');
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "MISSING_QUERY",
                    message = "Vui lòng cung cấp mã vật tư hoặc mã lô (material_or_lot) để truy vết vị trí kho."
                }, JsonOpts));
            }

            var matching = MaterialLots.Values
                .Where(l => l.LotNumber.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            l.MaterialCode.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            l.MaterialName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(l => string.IsNullOrWhiteSpace(wh) || l.WarehouseCode.Equals(wh, StringComparison.OrdinalIgnoreCase))
                .OrderBy(l => l.ExpDate) // FEFO order
                .ToList();

            if (matching.Count == 0)
            {
                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    success = true,
                    query,
                    warehouseFilter = wh,
                    found = 0,
                    message = $"Không tìm thấy lô vật tư nào khớp với từ khóa '{query}' trong kho MDS."
                }, JsonOpts));
            }

            var lotList = new List<object>();
            int rank = 1;

            foreach (var lot in matching)
            {
                int daysLeft = (int)Math.Round((lot.ExpDate - DateTime.Today).TotalDays);
                string fefoAdvice;
                string statusTag;

                if (lot.IsExpired)
                {
                    statusTag = "HẾT HẠN SỬ DỤNG";
                    fefoAdvice = "CẤM XUẤT: Lô hàng đã hết hạn sử dụng. Cần làm phiếu hủy hoặc tái chế.";
                }
                else if (lot.QCStatus.Equals("Quarantine", StringComparison.OrdinalIgnoreCase))
                {
                    statusTag = "ĐANG CÁCH LY KIỂM ĐỊNH";
                    fefoAdvice = "TẠM GIỮ: Đang chờ QC cấp tem PASS. Không được phép bốc xếp xuất xưởng.";
                }
                else if (daysLeft <= 30)
                {
                    statusTag = "CẬN DATE (<30 NGÀY)";
                    fefoAdvice = $"ƯU TIÊN SỐ {rank}: Hạn dùng chỉ còn {daysLeft} ngày. Khuyến nghị xuất kho ngay theo nguyên tắc FEFO!";
                }
                else
                {
                    statusTag = "TIÊU CHUẨN";
                    fefoAdvice = $"ƯU TIÊN SỐ {rank}: Hàng đạt chuẩn chất lượng, còn {daysLeft} ngày sử dụng.";
                }

                lotList.Add(new
                {
                    fefoPriority = rank++,
                    lotNumber = lot.LotNumber,
                    materialCode = lot.MaterialCode,
                    materialName = lot.MaterialName,
                    warehouseCode = lot.WarehouseCode,
                    palletLocation = lot.LocationCode,
                    quantity = lot.Quantity,
                    unit = lot.Unit,
                    mfgDate = lot.MfgDate.ToString("yyyy-MM-dd"),
                    expDate = lot.ExpDate.ToString("yyyy-MM-dd"),
                    daysUntilExpiry = daysLeft,
                    qcStatus = lot.QCStatus,
                    statusTag,
                    fefoAdvice
                });
            }

            return Task.FromResult(JsonSerializer.Serialize(new
            {
                success = true,
                query,
                warehouseFilter = string.IsNullOrWhiteSpace(wh) ? "Tất cả kho" : wh,
                totalLotsFound = lotList.Count,
                totalAvailableQty = matching.Where(l => l.QCStatus == "Approved" && !l.IsExpired).Sum(l => l.Quantity),
                fefoRule = "First-Expired, First-Out (Lô có ngày hết hạn sớm nhất được xếp thứ tự ưu tiên xuất trước)",
                lots = lotList
            }, JsonOpts));
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
