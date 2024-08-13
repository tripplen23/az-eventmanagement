using Newtonsoft.Json;

namespace EventManagementApi.Entity
{
    public class UserInteraction
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("userId")]
        public string UserId { get; set; }

        [JsonProperty("eventId")]
        public string EventId { get; set; }

        [JsonProperty("interactionType")]
        public string InteractionType { get; set; }

        [JsonProperty("interactionTime")]
        public DateTime InteractionTime { get; set; }
    }
}