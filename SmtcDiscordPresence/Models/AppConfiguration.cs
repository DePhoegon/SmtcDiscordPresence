using System.Text.Json.Serialization;

namespace SmtcDiscordPresence.Models
{
    public class AppConfiguration
    {
        [JsonPropertyName("Discord")]
        public DiscordConfiguration Discord { get; set; } = new();

        [JsonPropertyName("Settings")]
        public AppSettings Settings { get; set; } = new();
    }

    public class DiscordConfiguration
    {
        [JsonPropertyName("ClientId")]
        public string ClientId { get; set; } = string.Empty;
    }

    public class AppSettings
    {
        [JsonPropertyName("UpdateIntervalSeconds")]
        public int UpdateIntervalSeconds { get; set; } = 10;

        [JsonPropertyName("ShowBalloonTips")]
        public bool ShowBalloonTips { get; set; } = true;
    }
}