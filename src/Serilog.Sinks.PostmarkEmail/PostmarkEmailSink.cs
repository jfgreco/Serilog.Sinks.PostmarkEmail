using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace Serilog.Sinks.PostmarkEmail
{
    /// <summary>
    /// Renders a batch of log events into one email and posts it to the Postmark API.
    /// </summary>
    sealed class PostmarkEmailSink : IBatchedLogEventSink, IDisposable
    {
        /// <summary>
        /// Mail subjects are a single header line; RFC 5322 caps a line at 998 octets. Well short
        /// of that keeps the subject readable in a mail client's list view.
        /// </summary>
        internal const int MaxSubjectLength = 250;

        const string SendPath = "email";
        const string ServerTokenHeader = "X-Postmark-Server-Token";

        static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            // Postmark treats an explicit null differently from an absent field in some cases,
            // and sending nulls for a dozen unused fields just bloats the request.
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        static readonly string UserAgent = "Serilog.Sinks.PostmarkEmail/" +
            (typeof(PostmarkEmailSink).GetTypeInfo().Assembly.GetName().Version?.ToString() ?? "0.0.0");

        readonly PostmarkEmailSinkOptions _options;
        readonly ITextFormatter _bodyFormatter;
        readonly ITextFormatter _subjectFormatter;
        readonly HttpClient _httpClient;
        readonly bool _ownsHttpClient;
        readonly Uri _endpoint;
        readonly string _to;
        readonly string? _cc;
        readonly string? _bcc;

        /// <summary>
        /// Constructs a sink from validated options.
        /// </summary>
        /// <param name="options">Options that have already passed
        /// <see cref="PostmarkEmailSinkOptions.Validate"/>.</param>
        public PostmarkEmailSink(PostmarkEmailSinkOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));

            _bodyFormatter = new MessageTemplateTextFormatter(options.OutputTemplate, options.FormatProvider);
            _subjectFormatter = new MessageTemplateTextFormatter(options.Subject, options.FormatProvider);

            // Validate() guarantees at least one To recipient, so Format cannot return null here.
            _to = RecipientList.Format(options.To, nameof(options.To))!;
            _cc = RecipientList.Format(options.Cc, nameof(options.Cc));
            _bcc = RecipientList.Format(options.Bcc, nameof(options.Bcc));

            _endpoint = new Uri(options.ServerUrl ?? PostmarkEmailSinkOptions.DefaultServerUrl, SendPath);

            if (options.HttpClient != null)
            {
                // Borrowed: never mutated, never disposed. Every request carries an absolute URI
                // so the borrowed client's own BaseAddress is irrelevant.
                _httpClient = options.HttpClient;
                _ownsHttpClient = false;
            }
            else
            {
                _httpClient = options.MessageHandler != null
                    ? new HttpClient(options.MessageHandler, disposeHandler: false)
                    : new HttpClient();
                _httpClient.Timeout = options.HttpTimeout;
                _ownsHttpClient = true;
            }
        }

        /// <summary>
        /// Renders the batch into one email and sends it.
        /// </summary>
        /// <param name="batch">The events to send.</param>
        public async Task EmitBatchAsync(IReadOnlyCollection<LogEvent> batch)
        {
            if (batch == null)
                throw new ArgumentNullException(nameof(batch));
            if (batch.Count == 0)
                return;

            var body = new StringWriter();
            LogEvent? mostSignificant = null;

            foreach (var logEvent in batch)
            {
                _bodyFormatter.Format(logEvent, body);

                // Strictly greater, so ties keep the earliest event at that level.
                if (mostSignificant == null || logEvent.Level > mostSignificant.Level)
                    mostSignificant = logEvent;
            }

            var payload = body.ToString();

            var message = new PostmarkMessage
            {
                From = _options.From!,
                To = _to,
                Cc = _cc,
                Bcc = _bcc,
                ReplyTo = _options.ReplyTo,
                Subject = RenderSubject(mostSignificant!),
                Tag = _options.Tag,
                MessageStream = _options.MessageStream,
                TrackOpens = _options.TrackOpens
            };

            if (_options.IsBodyHtml)
                message.HtmlBody = payload;
            else
                message.TextBody = payload;

            await SendAsync(message).ConfigureAwait(false);
        }

        /// <summary>
        /// Nothing to do on an empty batch; the sink holds no state between sends.
        /// </summary>
        public Task OnEmptyBatchAsync() =>
#if NET8_0_OR_GREATER
            Task.CompletedTask;
#else
            Task.FromResult(false);
#endif

        /// <summary>
        /// Disposes the client if the sink created it. A borrowed client is left alone.
        /// </summary>
        public void Dispose()
        {
            if (_ownsHttpClient)
                _httpClient.Dispose();
        }

        string RenderSubject(LogEvent logEvent)
        {
            var writer = new StringWriter();
            _subjectFormatter.Format(logEvent, writer);

            var rendered = CollapseWhitespace(writer.ToString());

            if (rendered.Length > MaxSubjectLength)
                rendered = rendered.Substring(0, MaxSubjectLength - 3) + "...";

            return rendered;
        }

        /// <summary>
        /// Replaces every run of whitespace with a single space and trims the ends. A subject
        /// template containing {Message} or {Exception} would otherwise inject newlines into the
        /// Subject header.
        /// </summary>
        internal static string CollapseWhitespace(string value)
        {
            var builder = new StringBuilder(value.Length);
            var pendingSpace = false;

            foreach (var c in value)
            {
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }

                builder.Append(c);
            }

            return builder.ToString();
        }

        async Task SendAsync(PostmarkMessage message)
        {
            var json = JsonSerializer.Serialize(message, JsonOptions);

            using (var request = new HttpRequestMessage(HttpMethod.Post, _endpoint))
            {
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.TryAddWithoutValidation(ServerTokenHeader, _options.ServerToken);
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

                using (var response = await _httpClient.SendAsync(request).ConfigureAwait(false))
                {
                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var result = TryParseResponse(content);

                    if (response.IsSuccessStatusCode)
                    {
                        // Postmark can report a failure inside a 200-level response.
                        if (result != null && result.ErrorCode != 0)
                            SelfLog.WriteLine(
                                "Serilog.Sinks.PostmarkEmail: Postmark accepted the request but reported ErrorCode {0}: {1}",
                                result.ErrorCode, result.Message ?? "(no message)");

                        return;
                    }

                    var description = Describe(response.StatusCode, result, content);

                    // Serilog's guidance is to let batch failures propagate so the batching
                    // infrastructure can retry. That is right for transient faults, but a bad
                    // token or a malformed address will fail identically for the full ten-minute
                    // RetryTimeLimit, so those are reported once and the batch is dropped.
                    if (IsTransient(response.StatusCode))
                        throw new PostmarkApiException(response.StatusCode, result?.ErrorCode ?? 0, description);

                    SelfLog.WriteLine("Serilog.Sinks.PostmarkEmail: dropping batch, {0}", description);
                }
            }
        }

        static string Describe(HttpStatusCode statusCode, PostmarkSendResponse? result, string content)
        {
            if (result != null && (result.ErrorCode != 0 || result.Message != null))
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Postmark returned {0} ({1}) with ErrorCode {2}: {3}",
                    (int)statusCode, statusCode, result.ErrorCode, result.Message ?? "(no message)");

            var body = content.Length > 512 ? content.Substring(0, 512) + "..." : content;
            return string.Format(
                CultureInfo.InvariantCulture,
                "Postmark returned {0} ({1}): {2}",
                (int)statusCode, statusCode, body);
        }

        /// <summary>
        /// Whether re-sending the identical batch could plausibly succeed.
        /// </summary>
        static bool IsTransient(HttpStatusCode statusCode) =>
            (int)statusCode >= 500 ||
            statusCode == HttpStatusCode.RequestTimeout ||
            (int)statusCode == 429; // TooManyRequests is not in the netstandard2.0 enum.

        static PostmarkSendResponse? TryParseResponse(string content)
        {
            if (content == null || content.Length == 0)
                return null;

            try
            {
                return JsonSerializer.Deserialize<PostmarkSendResponse>(content, JsonOptions);
            }
            catch (JsonException)
            {
                // A gateway or proxy in front of Postmark may return HTML; the caller falls back
                // to reporting the raw body.
                return null;
            }
        }
    }
}
