using AskLucy.Areas.Identity.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AskLucy.Models
{
    public class UserChat
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        public required string Title { get; set; }
        public string? SessionId { get; set; }
        public DateTime? CreationDateTime { get; set; } 
        public DateTime? LastAccessDateTime { get; set; }    
        public required string UserId { get; set; } 
        public virtual required ApplicationUser User { get; set; }    

    }
}
