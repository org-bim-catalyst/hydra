namespace AskLucy.Models
{
    public class UserChat
    {
        public int Id { get; set; }
        public required string Title { get; set; }
        public string? SessionId { get; set; }
        public DateTime? CreationDateTime { get; set; } 
        public DateTime? LastAccessDateTime { get; set; }    
    }
}
