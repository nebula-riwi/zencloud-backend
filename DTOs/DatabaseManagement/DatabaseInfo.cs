
namespace ZenCloud.DTOs.DatabaseManagement
{
    public class DatabaseInfo
    {
        public string Name { get; set; } = null!;
        public string Version { get; set; } = null!;
        public string Hostname { get; set; } = null!;
        public int Port { get; set; }
        public decimal TotalSize { get; set; } // en MB
        public int TableCount { get; set; }
        public string CharacterSet { get; set; } = null!;
        public string Collation { get; set; } = null!;
    }
}