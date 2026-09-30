using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ZeroAgent.Core.Tools;
using ZeroData.Core;
using ZeroPrompt.Core.Grammar;

namespace ZeroAgent.Tools.Data
{
    /// <summary>
    /// Metadata descriptor for a column in the database/DataFrame catalog.
    /// </summary>
    public sealed class ColumnMetadata
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public bool IsPrimaryKey { get; set; }
        public bool IsVector { get; set; }
        public int VectorDimension { get; set; }

        public ColumnMetadata() { }

        public ColumnMetadata(string name, string dataType, bool isPrimaryKey = false, bool isVector = false, int vectorDimension = 0)
        {
            Name = name;
            DataType = dataType;
            IsPrimaryKey = isPrimaryKey;
            IsVector = isVector;
            VectorDimension = vectorDimension;
        }
    }

    /// <summary>
    /// Metadata descriptor for a table in the database/DataFrame catalog.
    /// </summary>
    public sealed class TableMetadata
    {
        public string TableName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<ColumnMetadata> Columns { get; set; } = new List<ColumnMetadata>();
        public int EstimatedRowCount { get; set; }

        public TableMetadata() { }

        public TableMetadata(string tableName, string description, int estimatedRowCount = 0)
        {
            TableName = tableName;
            Description = description;
            EstimatedRowCount = estimatedRowCount;
        }
    }

    /// <summary>
    /// Dynamic Database & Columnar DataFrame query tool for ZeroAgent.
    /// Enables self-describing schema introspection, safe parameterized filtering,
    /// and hybrid vector similarity retrieval without requiring hardcoded schemas or raw SQL.
    /// </summary>
    public static class DynamicDatabaseQueryTool
    {
        private static readonly ConcurrentDictionary<string, TableMetadata> _catalog = new ConcurrentDictionary<string, TableMetadata>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DataFrame> _dataFrames = new ConcurrentDictionary<string, DataFrame>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> _tableToConnStr = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Func<string, global::System.Data.IDbConnection>? _connectionFactory;

        static DynamicDatabaseQueryTool()
        {
            // Seed a default industrial machines DataFrame to demonstrate immediate readiness
            SeedDefaultIndustrialDataset();
        }

        public static void SetConnectionFactory(Func<string, global::System.Data.IDbConnection> factory)
        {
            _connectionFactory = factory;
        }

        public static global::System.Data.IDbConnection CreateConnection(string connectionString)
        {
            if (_connectionFactory != null)
            {
                return _connectionFactory(connectionString);
            }

            var type = Type.GetType("Microsoft.Data.SqlClient.SqlConnection, Microsoft.Data.SqlClient")
                    ?? Type.GetType("System.Data.SqlClient.SqlConnection, System.Data.SqlClient")
                    ?? Type.GetType("System.Data.SqlClient.SqlConnection, System.Data");

            if (type != null)
            {
                return (global::System.Data.IDbConnection)Activator.CreateInstance(type, connectionString)!;
            }

            throw new InvalidOperationException("No IDbConnection provider available. Please call DynamicDatabaseQueryTool.SetConnectionFactory(...) with your connection provider.");
        }

        /// <summary>
        /// Automatically introspects and registers all tables and columns from a live SQL database into the Agent catalog.
        /// Does NOT load entire data into memory; queries will execute via SQL Pushdown.
        /// </summary>
        public static void RegisterLiveDatabase(string connectionString, string databaseName = "")
        {
            if (string.IsNullOrWhiteSpace(connectionString)) return;

            using (var conn = CreateConnection(connectionString))
            {
                conn.Open();

                // 1. Discover all tables
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT TABLE_NAME, TABLE_SCHEMA FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'";
                    var tables = new List<(string Name, string Schema)>();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string tName = reader.GetString(0);
                            string tSchema = reader.GetString(1);
                            tables.Add((tName, tSchema));
                        }
                    }

                    // 2. Discover columns for each table
                    foreach (var (tName, tSchema) in tables)
                    {
                        using (var colCmd = conn.CreateCommand())
                        {
                            colCmd.CommandText = $"SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{tName}' AND TABLE_SCHEMA = '{tSchema}' ORDER BY ORDINAL_POSITION";
                            var meta = new TableMetadata(tName, $"Live database table from {databaseName} ({tSchema}.{tName})", estimatedRowCount: 0);

                            using (var colReader = colCmd.ExecuteReader())
                            {
                                while (colReader.Read())
                                {
                                    string cName = colReader.GetString(0);
                                    string cType = colReader.GetString(1);
                                    bool isPk = cName.Equals("id", StringComparison.OrdinalIgnoreCase) || cName.EndsWith("_id", StringComparison.OrdinalIgnoreCase);
                                    meta.Columns.Add(new ColumnMetadata(cName, cType, isPrimaryKey: isPk));
                                }
                            }

                            _catalog[tName] = meta;
                            _tableToConnStr[tName] = connectionString;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Registers a DataFrame in the active agent database catalog with automatic schema deduction.
        /// </summary>
        public static void RegisterTable(string tableName, DataFrame dataFrame, string description = "")
        {
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (dataFrame == null) throw new ArgumentNullException(nameof(dataFrame));

            _dataFrames[tableName] = dataFrame;

            var meta = new TableMetadata(tableName, description, dataFrame.RowCount);
            foreach (var colName in dataFrame.ColumnNames)
            {
                var col = dataFrame[colName];
                bool isVec = col is VectorColumn;
                int dim = isVec ? ((VectorColumn)col).Dimension : 0;
                meta.Columns.Add(new ColumnMetadata(colName, col.DataType.Name, isPrimaryKey: colName.Equals("id", StringComparison.OrdinalIgnoreCase), isVector: isVec, vectorDimension: dim));
            }
            _catalog[tableName] = meta;
        }

        /// <summary>
        /// Registers all dynamic database query tools into the specified agent tool registry.
        /// </summary>
        public static void RegisterAll(AgentToolRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            // Tool 1: db_list_tables
            registry.Register(new AgentTool(
                "db_list_tables",
                "Lists all available tables and datasets currently registered in the database catalog.",
                "void",
                _ => Task.FromResult(ExecuteListTables()),
                requiresApproval: false
            ));

            // Tool 2: db_describe_table
            var describeSchema = new JsonSchemaConstraint("DescribeTable")
                .AddProperty("tableName", SchemaPropertyType.String, required: true);

            registry.Register(new AgentTool(
                "db_describe_table",
                "Returns schema metadata for a specified table: column names, data types, primary keys, and vector dimensions.",
                "tableName: string",
                ExecuteDescribeTableAsync,
                schema: describeSchema,
                requiresApproval: false
            ));

            // Tool 3: db_query_table
            var querySchema = new JsonSchemaConstraint("QueryTable")
                .AddProperty("tableName", SchemaPropertyType.String, required: true)
                .AddProperty("whereColumn", SchemaPropertyType.String, required: false)
                .AddProperty("whereValue", SchemaPropertyType.String, required: false)
                .AddProperty("limit", SchemaPropertyType.Number, required: false);

            registry.Register(new AgentTool(
                "db_query_table",
                "Queries records from a database table using safe parameterized column filters and row limits.",
                "tableName: string, whereColumn?: string, whereValue?: string, limit?: int",
                ExecuteQueryTableAsync,
                schema: querySchema,
                requiresApproval: false
            ));

            // Tool 4: db_vector_search
            var vecSearchSchema = new JsonSchemaConstraint("VectorSearchTable")
                .AddProperty("tableName", SchemaPropertyType.String, required: true)
                .AddProperty("columnName", SchemaPropertyType.String, required: true)
                .AddProperty("topK", SchemaPropertyType.Number, required: false);

            registry.Register(new AgentTool(
                "db_vector_search",
                "Executes high-speed hybrid vector similarity search across a table's VectorColumn using 1-Bit BQ and SIMD Cosine reranking.",
                "tableName: string, columnName: string, queryVector: float[], topK?: int",
                ExecuteVectorSearchTableAsync,
                schema: vecSearchSchema,
                requiresApproval: false
            ));
        }

        private static string ExecuteListTables()
        {
            var summary = _catalog.Values.Select(t => new
            {
                table = t.TableName,
                description = t.Description,
                rowCount = t.EstimatedRowCount,
                columnCount = t.Columns.Count,
                columns = t.Columns.Select(c => c.Name).ToArray()
            }).ToList();

            return JsonSerializer.Serialize(new { totalTables = summary.Count, tables = summary });
        }

        private static Task<string> ExecuteDescribeTableAsync(string arg)
        {
            string tableName = arg.Trim();
            if (arg.TrimStart().StartsWith("{"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(arg);
                    if (doc.RootElement.TryGetProperty("tableName", out var prop))
                    {
                        tableName = prop.GetString() ?? tableName;
                    }
                }
                catch { }
            }

            if (!_catalog.TryGetValue(tableName, out var meta))
            {
                return Task.FromResult($"Table '{tableName}' not found in catalog.");
            }

            return Task.FromResult(JsonSerializer.Serialize(meta));
        }

        private static Task<string> ExecuteQueryTableAsync(string arg)
        {
            try
            {
                string tableName = string.Empty;
                string? filterCol = null;
                string? filterVal = null;
                int limit = 10;

                if (arg.TrimStart().StartsWith("{"))
                {
                    using var doc = JsonDocument.Parse(arg);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("tableName", out var t)) tableName = t.GetString() ?? string.Empty;
                    if (root.TryGetProperty("whereColumn", out var c)) filterCol = c.GetString();
                    if (root.TryGetProperty("whereValue", out var v)) filterVal = v.GetString();
                    if (root.TryGetProperty("limit", out var l)) limit = l.GetInt32();
                }
                else
                {
                    tableName = arg.Trim();
                }

                if (!_dataFrames.TryGetValue(tableName, out var df))
                {
                    if (_tableToConnStr.TryGetValue(tableName, out var connStr))
                    {
                        return ExecuteSqlPushdownQueryAsync(connStr, tableName, filterCol, filterVal, limit);
                    }
                    return Task.FromResult($"Error: Table '{tableName}' was not found.");
                }

                // Apply filter if specified
                DataFrame filtered = df;
                if (!string.IsNullOrWhiteSpace(filterCol) && filterVal != null)
                {
                    if (df.HasColumn(filterCol))
                    {
                        var mask = new SelectionMask(df.RowCount);
                        var targetCol = df[filterCol];
                        for (int r = 0; r < df.RowCount; r++)
                        {
                            var val = targetCol.GetValue(r)?.ToString();
                            if (string.Equals(val, filterVal, StringComparison.OrdinalIgnoreCase))
                            {
                                mask.SetSelected(r, true);
                            }
                        }
                        filtered = df.Filter(mask);
                    }
                }

                // Materialize rows up to limit
                int actualCount = Math.Min(limit, filtered.RowCount);
                var rows = new List<Dictionary<string, object?>>(actualCount);

                for (int r = 0; r < actualCount; r++)
                {
                    var rowDict = new Dictionary<string, object?>(filtered.ColumnCount);
                    foreach (var colName in filtered.ColumnNames)
                    {
                        var col = filtered[colName];
                        if (col is VectorColumn)
                        {
                            rowDict[colName] = $"[Vector {((VectorColumn)col).Dimension}d]";
                        }
                        else
                        {
                            rowDict[colName] = col.GetValue(r);
                        }
                    }
                    rows.Add(rowDict);
                }

                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    table = tableName,
                    totalMatched = filtered.RowCount,
                    returned = actualCount,
                    data = rows
                }));
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Failed to execute query: {ex.Message}");
            }
        }

        private static Task<string> ExecuteSqlPushdownQueryAsync(string connStr, string tableName, string? filterCol, string? filterVal, int limit)
        {
            try
            {
                using var conn = CreateConnection(connStr);
                conn.Open();

                using var cmd = conn.CreateCommand();
                var sb = new StringBuilder();
                int safeLimit = Math.Max(1, Math.Min(limit, 500));
                sb.Append("SELECT TOP (").Append(safeLimit).Append(") * FROM [").Append(tableName.Replace("]", "]]")).Append("]");

                if (!string.IsNullOrWhiteSpace(filterCol) && filterVal != null)
                {
                    sb.Append(" WHERE [").Append(filterCol.Replace("]", "]]")).Append("] = @val");
                    var valParam = cmd.CreateParameter();
                    valParam.ParameterName = "@val";
                    valParam.Value = filterVal;
                    cmd.Parameters.Add(valParam);
                }

                cmd.CommandText = sb.ToString();

                using var reader = cmd.ExecuteReader();
                var df = DataFrame.FromDataReader(reader, safeLimit);

                var rows = new List<Dictionary<string, object?>>(df.RowCount);
                for (int r = 0; r < df.RowCount; r++)
                {
                    var rowDict = new Dictionary<string, object?>(df.ColumnCount);
                    foreach (var colName in df.ColumnNames)
                    {
                        rowDict[colName] = df[colName].GetValue(r);
                    }
                    rows.Add(rowDict);
                }

                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    table = tableName,
                    source = "SQL_PUSHDOWN_LIVE",
                    totalMatched = df.RowCount,
                    returned = df.RowCount,
                    data = rows
                }));
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Failed to execute live SQL pushdown: {ex.Message}");
            }
        }

        private static Task<string> ExecuteVectorSearchTableAsync(string arg)
        {
            try
            {
                string tableName = string.Empty;
                string colName = string.Empty;
                float[]? queryVec = null;
                int topK = 5;

                using (var doc = JsonDocument.Parse(arg))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("tableName", out var t)) tableName = t.GetString() ?? string.Empty;
                    if (root.TryGetProperty("columnName", out var c)) colName = c.GetString() ?? string.Empty;
                    if (root.TryGetProperty("topK", out var k)) topK = k.GetInt32();

                    if (root.TryGetProperty("queryVector", out var qv) && qv.ValueKind == JsonValueKind.Array)
                    {
                        queryVec = qv.EnumerateArray().Select(x => x.GetSingle()).ToArray();
                    }
                }

                if (!_dataFrames.TryGetValue(tableName, out var df))
                    return Task.FromResult($"Table '{tableName}' not found.");

                if (!df.HasColumn(colName) || !(df[colName] is VectorColumn vecCol))
                    return Task.FromResult($"Column '{colName}' in '{tableName}' is not a valid VectorColumn.");

                queryVec ??= new float[vecCol.Dimension]; // Fallback if empty for testing
                var matches = vecCol.SearchNearest(queryVec, topK);

                var results = new List<object>(matches.Length);
                foreach (var m in matches)
                {
                    var rowData = new Dictionary<string, object?>();
                    foreach (var cn in df.ColumnNames)
                    {
                        if (cn != colName) rowData[cn] = df[cn].GetValue(m.RowIndex);
                    }
                    results.Add(new { rowIndex = m.RowIndex, score = m.Score, row = rowData });
                }

                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    table = tableName,
                    vectorColumn = colName,
                    totalResults = matches.Length,
                    results
                }));
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Vector search failed: {ex.Message}");
            }
        }

        private static void SeedDefaultIndustrialDataset()
        {
            int count = 5;
            int[] ids = new[] { 1, 2, 3, 4, 5 };
            string[] machines = new[] { "CNC-01", "CNC-02", "ROBOT-ARM-01", "PRESS-03", "CONVEYOR-04" };
            string[] status = new[] { "RUNNING", "WARNING", "RUNNING", "CRITICAL_ERROR", "IDLE" };
            double[] efficiency = new[] { 98.4, 76.1, 99.5, 41.2, 0.0 };
            int[] defects = new[] { 0, 4, 0, 18, 0 };

            var vecCol = new VectorColumn("embedding", 16);
            for (int i = 0; i < count; i++)
            {
                float[] v = new float[16];
                v[i % 16] = 1.0f;
                vecCol.AddVector(v);
            }

            var df = new DataFrame(
                new DataColumn<int>("id", ids),
                new DataColumn<string>("machine_id", machines),
                new DataColumn<string>("status", status),
                new DataColumn<double>("efficiency", efficiency),
                new DataColumn<int>("defect_count", defects),
                vecCol
            );

            RegisterTable("factory_machines", df, "Factory equipment real-time operational status, efficiency, and defect metrics.");
        }
    }
}
