namespace ZenCloud.DTOs.DatabaseManagement
{
    public class TableInfo
    {
        public string TableName { get; set; } = null!;
        public string TableType { get; set; } = null!;
        public long RowCount { get; set; }
        public DateTime CreateTime { get; set; } // Agregar esta línea
    }
}