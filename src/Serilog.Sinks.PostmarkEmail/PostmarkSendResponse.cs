using System.Text.Json.Serialization;

namespace Serilog.Sinks.PostmarkEmail
{
    /// <summary>
    /// The response body from <c>POST https://api.postmarkapp.com/email</c>.
    /// Postmark returns <c>ErrorCode</c> on both success (0) and failure responses, and can
    /// report a non-zero <c>ErrorCode</c> alongside a 200-level status.
    /// </summary>
    sealed class PostmarkSendResponse
    {
        [JsonPropertyName("ErrorCode")]
        public int ErrorCode { get; set; }

        [JsonPropertyName("Message")]
        public string? Message { get; set; }

        [JsonPropertyName("MessageID")]
        public string? MessageId { get; set; }

        [JsonPropertyName("To")]
        public string? To { get; set; }

        [JsonPropertyName("SubmittedAt")]
        public string? SubmittedAt { get; set; }
    }
}
