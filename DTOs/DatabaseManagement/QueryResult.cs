
namespace ZenCloud.DTOs.DatabaseManagement
{
    public class QueryResult
    {
        public bool Success { get; set; }
        public List<string> Columns { get; set; } = new List<string>();
        public List<object?[]> Rows { get; set; } = new List<object?[]>();
        public int ExecutionTime { get; set; }
        public int AffectedRows { get; set; }
        public string? ErrorMessage { get; set; }
    }
}