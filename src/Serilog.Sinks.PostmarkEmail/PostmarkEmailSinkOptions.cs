using System;
using System.Net.Http;

namespace Serilog.Sinks.PostmarkEmail
{
    /// <summary>
    /// Configuration for the Postmark email sink.
    /// </summary>
    public class PostmarkEmailSinkOptions
    {
        /// <summary>
        /// The output template applied to each log event in the batch when none is supplied.
        /// </summary>
        public const string DefaultOutputTemplate =
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] {Message}{NewLine}{Exception}";

        /// <summary>
        /// The subject template used when none is supplied.
        /// </summary>
        public const string DefaultSubject = "Log Email";

        /// <summary>
        /// The number of events sent in a single email when none is supplied.
        /// </summary>
        public const int DefaultBatchSizeLimit = 100;

        /// <summary>
        /// The number of events buffered in memory when none is supplied.
        /// </summary>
        public const int DefaultQueueLimit = 10_000;

        /// <summary>
        /// The Postmark API endpoint used when <see cref="ServerUrl"/> is not set.
        /// </summary>
        public static readonly Uri DefaultServerUrl = new Uri("https://api.postmarkapp.com/");

        /// <summary>
        /// The longest the sink waits to fill a batch when none is supplied.
        /// </summary>
        public static readonly TimeSpan DefaultBufferingTimeLimit = TimeSpan.FromSeconds(30);

        /// <summary>
        /// The HTTP timeout applied to a sink-owned <see cref="System.Net.Http.HttpClient"/>
        /// when none is supplied.
        /// </summary>
        public static readonly TimeSpan DefaultHttpTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Required. The Postmark <em>server</em> token, sent as the <c>X-Postmark-Server-Token</c>
        /// header. This is not the same as an account token: account tokens cannot send email.
        /// </summary>
        public string? ServerToken { get; set; }

        /// <summary>
        /// Required. The sender address. Must be a verified Postmark sender signature or an
        /// address on a confirmed domain. Accepts the <c>Display Name &lt;addr@example.com&gt;</c> form.
        /// </summary>
        public string? From { get; set; }

        /// <summary>
        /// Required. Recipient address(es), separated by commas or semicolons. Postmark accepts at
        /// most 50; any beyond that are dropped with a <see cref="Serilog.Debugging.SelfLog"/> warning.
        /// </summary>
        public string? To { get; set; }

        /// <summary>
        /// Optional carbon-copy recipient(s), separated by commas or semicolons.
        /// </summary>
        public string? Cc { get; set; }

        /// <summary>
        /// Optional blind-carbon-copy recipient(s), separated by commas or semicolons.
        /// </summary>
        public string? Bcc { get; set; }

        /// <summary>
        /// Optional reply-to address, overriding the sender for replies.
        /// </summary>
        public string? ReplyTo { get; set; }

        /// <summary>
        /// The subject line. This is a Serilog output template rendered against the most
        /// significant event in the batch, so <c>"[{Level}] {Message}"</c> works. Newlines and
        /// runs of whitespace in the rendered result are collapsed to single spaces, because a
        /// mail header cannot span lines.
        /// </summary>
        public string Subject { get; set; } = DefaultSubject;

        /// <summary>
        /// The output template applied to every event in the batch to build the email body.
        /// </summary>
        public string OutputTemplate { get; set; } = DefaultOutputTemplate;

        /// <summary>
        /// When <c>true</c> the rendered body is sent verbatim as Postmark's <c>HtmlBody</c>;
        /// otherwise as <c>TextBody</c>. Setting this does not escape or wrap the output, so pair
        /// it with an <see cref="OutputTemplate"/> that emits HTML.
        /// </summary>
        public bool IsBodyHtml { get; set; }

        /// <summary>
        /// Optional Postmark tag used to group these messages in Postmark's statistics.
        /// </summary>
        public string? Tag { get; set; }

        /// <summary>
        /// Optional Postmark message stream ID. Postmark defaults to <c>"outbound"</c> when unset.
        /// Log email is transactional, so the default stream is usually correct.
        /// </summary>
        public string? MessageStream { get; set; }

        /// <summary>
        /// Optional open tracking. Leave unset to inherit the server's configuration.
        /// </summary>
        public bool? TrackOpens { get; set; }

        /// <summary>
        /// Supplies culture-specific formatting for both the subject and body templates.
        /// </summary>
        public IFormatProvider? FormatProvider { get; set; }

        /// <summary>
        /// The maximum number of events packed into a single email.
        /// </summary>
        public int BatchSizeLimit { get; set; } = DefaultBatchSizeLimit;

        /// <summary>
        /// The longest the sink waits for a batch to fill before sending what it has.
        /// </summary>
        public TimeSpan BufferingTimeLimit { get; set; } = DefaultBufferingTimeLimit;

        /// <summary>
        /// Events held in memory before new ones are discarded, or <c>null</c> for an unbounded queue.
        /// </summary>
        public int? QueueLimit { get; set; } = DefaultQueueLimit;

        /// <summary>
        /// Whether to send the very first event immediately rather than waiting for a batch.
        /// Defaults to <c>false</c> here (Serilog's own default is <c>true</c>) because one email
        /// per first event defeats the purpose of batching log mail.
        /// </summary>
        public bool EagerlyEmitFirstEvent { get; set; }

        /// <summary>
        /// How long failed batches keep being retried, or <c>null</c> for Serilog's default of
        /// ten minutes.
        /// </summary>
        public TimeSpan? RetryTimeLimit { get; set; }

        /// <summary>
        /// Overrides the Postmark API base address. Intended for testing and for proxies.
        /// </summary>
        public Uri? ServerUrl { get; set; }

        /// <summary>
        /// The timeout applied to a sink-owned <see cref="System.Net.Http.HttpClient"/>. Ignored
        /// when <see cref="HttpClient"/> is supplied, since the sink does not mutate a borrowed client.
        /// </summary>
        public TimeSpan HttpTimeout { get; set; } = DefaultHttpTimeout;

        /// <summary>
        /// An externally-owned client to send through, for example one from
        /// <c>IHttpClientFactory</c>. The sink borrows it: it neither disposes it nor mutates its
        /// <c>BaseAddress</c>, timeout, or default headers, and always requests an absolute URI
        /// derived from <see cref="ServerUrl"/>. Mutually exclusive with <see cref="MessageHandler"/>.
        /// </summary>
        public HttpClient? HttpClient { get; set; }

        /// <summary>
        /// A handler to construct the sink's client over. The sink owns the resulting client but
        /// not the handler, so the caller stays responsible for disposing it. Mutually exclusive
        /// with <see cref="HttpClient"/>.
        /// </summary>
        public HttpMessageHandler? MessageHandler { get; set; }

        /// <summary>
        /// Throws if the options cannot produce a usable sink. Called at configuration time so
        /// that a misconfigured sink fails at startup rather than silently at the first error email.
        /// </summary>
        /// <param name="paramName">The name of the parameter reported in thrown exceptions.</param>
        internal void Validate(string paramName)
        {
            if (ServerToken == null || ServerToken.Trim().Length == 0)
                throw Invalid(nameof(ServerToken), "must be a non-empty Postmark server token", paramName);

            if (From == null || From.Trim().Length == 0)
                throw Invalid(nameof(From), "must be a non-empty sender address", paramName);

            if (RecipientList.Parse(To).Count == 0)
                throw Invalid(nameof(To), "must contain at least one recipient address", paramName);

            if (Subject == null)
                throw Invalid(nameof(Subject), "cannot be null", paramName);

            if (OutputTemplate == null || OutputTemplate.Length == 0)
                throw Invalid(nameof(OutputTemplate), "must be a non-empty output template", paramName);

            if (BatchSizeLimit <= 0)
                throw Invalid(nameof(BatchSizeLimit), "must be greater than zero", paramName);

            if (QueueLimit.HasValue && QueueLimit.Value <= 0)
                throw Invalid(nameof(QueueLimit), "must be greater than zero, or null for an unbounded queue", paramName);

            if (BufferingTimeLimit <= TimeSpan.Zero)
                throw Invalid(nameof(BufferingTimeLimit), "must be greater than zero", paramName);

            if (RetryTimeLimit.HasValue && RetryTimeLimit.Value < TimeSpan.Zero)
                throw Invalid(nameof(RetryTimeLimit), "cannot be negative", paramName);

            if (HttpTimeout <= TimeSpan.Zero)
                throw Invalid(nameof(HttpTimeout), "must be greater than zero", paramName);

            if (ServerUrl != null &&
                (!ServerUrl.IsAbsoluteUri ||
                 (ServerUrl.Scheme != Uri.UriSchemeHttp && ServerUrl.Scheme != Uri.UriSchemeHttps)))
            {
                throw Invalid(nameof(ServerUrl), "must be an absolute http or https URI", paramName);
            }

            if (HttpClient != null && MessageHandler != null)
                throw new ArgumentException(
                    $"{nameof(PostmarkEmailSinkOptions)}.{nameof(HttpClient)} and " +
                    $"{nameof(PostmarkEmailSinkOptions)}.{nameof(MessageHandler)} are mutually exclusive; supply at most one.",
                    paramName);
        }

        static ArgumentException Invalid(string property, string requirement, string paramName) =>
            new ArgumentException($"{nameof(PostmarkEmailSinkOptions)}.{property} {requirement}.", paramName);
    }
}
