using System.ComponentModel.DataAnnotations;
namespace EventManagementApi.Entity
{
    public class Event
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Location { get; set; }
        public string Date { get; set; }
        public int TotalSpots { get; set; }
        public int RegisteredCount { get; set; } = 0;
        [Required]
        public string OrganizerId { get; set; }
        public ApplicationUser Organizer { get; set; }

        // Navigation properties
        public virtual ICollection<EventImage> Images { get; set; } = new List<EventImage>();
        public virtual ICollection<EventDocument> Documents { get; set; } = new List<EventDocument>();
    }


    public class EventImage
    {
        public Guid Id { get; set; }
        public Guid EventId { get; set; }
        public string Url { get; set; }
        public virtual Event Event { get; set; }
    }

    public class EventDocument
    {
        public Guid Id { get; set; }
        public Guid EventId { get; set; }
        public string Url { get; set; }
        public virtual Event Event { get; set; }
    }

}