using EventManagementApi.Entity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EventManagementApi.Database
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Event> Events { get; set; }
        public DbSet<EventImage> EventImages { get; set; }
        public DbSet<EventDocument> EventDocuments { get; set; }
        public DbSet<ApplicationUser> ApplicationUsers { get; set; }
        public DbSet<EventRegistration> EventRegistrations { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // TODO: Define a relationship between Event and ApplicationUser
            builder.Entity<Event>()
                .HasOne(e => e.Organizer)
                .WithMany()
                .HasForeignKey(e => e.OrganizerId)
                .OnDelete(DeleteBehavior.Cascade);

            // TODO: Define the relationship between Event and EventImage (1-n)
            builder.Entity<EventImage>()
                .HasOne(ei => ei.Event)
                .WithMany(e => e.Images)
                .HasForeignKey(ei => ei.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            // TODO: Define the relationship between Event and EventDocument (1-n)
            builder.Entity<EventDocument>()
                .HasOne(ed => ed.Event)
                .WithMany(e => e.Documents)
                .HasForeignKey(ed => ed.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            // TODO: Composite key configuration for EventRegistration
            builder.Entity<EventRegistration>()
                .HasKey(er => new { er.EventId, er.UserId });
        }
    }
}
