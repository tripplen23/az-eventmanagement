using Microsoft.AspNetCore.Identity;

namespace EventManagementApi.Entity
{
    public class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; }
    }
}
