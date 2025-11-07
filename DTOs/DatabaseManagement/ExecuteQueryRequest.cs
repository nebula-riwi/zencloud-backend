
using System.ComponentModel.DataAnnotations;

namespace ZenCloud.DTOs.DatabaseManagement
{
    public class ExecuteQueryRequest
    {
        [Required]
        [StringLength(5000)]
        public string Query { get; set; } = null!;
    }
}