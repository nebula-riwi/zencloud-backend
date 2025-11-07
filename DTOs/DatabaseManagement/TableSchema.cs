namespace ZenCloud.DTOs.DatabaseManagement
{
    public class TableSchema
    {
        public string TableName { get; set; } = null!;
        public List<ColumnInfo> Columns { get; set; } = new List<ColumnInfo>();
        public List<IndexInfo> Indexes { get; set; } = new List<IndexInfo>(); // Agregar esta línea
    }

    public class ColumnInfo
    {
        public string ColumnName { get; set; } = null!;
        public string DataType { get; set; } = null!;
        public bool IsNullable { get; set; }
        public bool IsPrimaryKey { get; set; }
        public string? DefaultValue { get; set; }
        public int? MaxLength { get; set; }
        public string? Extra { get; set; } // Agregar esta línea
    }

   
    public class IndexInfo
    {
        public string IndexName { get; set; } = null!;
        public string ColumnName { get; set; } = null!;
        public bool IsUnique { get; set; }
        public string IndexType { get; set; } = null!;
    }
}