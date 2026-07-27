using AskLucy.Models;
using Microsoft.AspNetCore.Identity;

//https://codewithmukesh.com/blog/user-management-in-aspnet-core-mvc/
namespace AskLucy.Areas.Identity.Models
{
    public class ApplicationUser : IdentityUser
    {
        public ApplicationUser()
        {
                this.UserChats = new HashSet<UserChat>();  
        }

        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateOnly BirthDate { get; set; }
        public byte[]? ProfilePicture { get; set; }

        public ICollection<UserChat> UserChats { get; set; }
    }
}
