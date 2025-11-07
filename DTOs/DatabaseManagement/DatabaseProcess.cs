
namespace ZenCloud.DTOs.DatabaseManagement
{
    public class DatabaseProcess
    {
        public int Id { get; set; }
        public string User { get; set; } = null!;
        public string Host { get; set; } = null!;
        public string? Database { get; set; }
        public string Command { get; set; } = null!;
        public int Time { get; set; } // en segundos
        public string? State { get; set; }
        public string? Info { get; set; } // Query que está ejecutando
    }
}