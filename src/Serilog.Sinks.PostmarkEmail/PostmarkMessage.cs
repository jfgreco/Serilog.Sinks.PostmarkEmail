using System.Text.Json.Serialization;

namespace Serilog.Sinks.PostmarkEmail
{
    /// <summary>
    /// The request body for <c>POST https://api.postmarkapp.com/email</c>.
    /// Property names are stated explicitly rather than relying on the serializer's default
    /// naming, so a future change to <see cref="System.Text.Json.JsonSerializerOptions"/>
    /// cannot silently break the wire format.
    /// </summary>
    sealed class PostmarkMessage
    {
        [JsonPropertyName("From")]
        public string From { get; set; } = string.Empty;

        [JsonPropertyName("To")]
        public string To { get; set; } = string.Empty;

        [JsonPropertyName("Cc")]
        public string? Cc { get; set; }

        [JsonPropertyName("Bcc")]
        public string? Bcc { get; set; }

        [JsonPropertyName("ReplyTo")]
        public string? ReplyTo { get; set; }

        [JsonPropertyName("Subject")]
        public string? Subject { get; set; }

        [JsonPropertyName("TextBody")]
        public string? TextBody { get; set; }

        [JsonPropertyName("HtmlBody")]
        public string? HtmlBody { get; set; }

        [JsonPropertyName("Tag")]
        public string? Tag { get; set; }

        [JsonPropertyName("MessageStream")]
        public string? MessageStream { get; set; }

        [JsonPropertyName("TrackOpens")]
        public bool? TrackOpens { get; set; }
    }
}
