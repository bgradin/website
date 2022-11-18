using System;
using System.Text.Json.Serialization;

namespace Gradinware.Models.Authentication
{
    internal class UserEvent
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("code")]
        public UserEventCode Code { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }
    }
}
