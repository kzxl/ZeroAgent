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
    public static partial class DynamicDatabaseQueryTool
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
    }
}
