using System;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.PostmarkEmail;

namespace Serilog
{
    /// <summary>
    /// Adds the Postmark email sink to a <see cref="LoggerConfiguration"/>.
    /// </summary>
    public static class PostmarkEmailLoggerConfigurationExtensions
    {
        /// <summary>
        /// Sends batched log events as email through the Postmark API.
        /// </summary>
        /// <param name="loggerSinkConfiguration">The <c>WriteTo</c> configuration object.</param>
        /// <param name="options">Configuration for the sink.</param>
        /// <param name="restrictedToMinimumLevel">The minimum level for events passed through the sink.</param>
        /// <param name="levelSwitch">A switch allowing the pass-through minimum level to be changed at runtime.</param>
        /// <returns>Logger configuration, allowing configuration to continue.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="options"/> is incomplete or inconsistent.</exception>
        public static LoggerConfiguration PostmarkEmail(
            this LoggerSinkConfiguration loggerSinkConfiguration,
            PostmarkEmailSinkOptions options,
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
            LoggingLevelSwitch? levelSwitch = null)
        {
            if (loggerSinkConfiguration == null)
                throw new ArgumentNullException(nameof(loggerSinkConfiguration));
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            options.Validate(nameof(options));

            var batchingOptions = new BatchingOptions
            {
                BatchSizeLimit = options.BatchSizeLimit,
                BufferingTimeLimit = options.BufferingTimeLimit,
                QueueLimit = options.QueueLimit,
                EagerlyEmitFirstEvent = options.EagerlyEmitFirstEvent
            };

            if (options.RetryTimeLimit.HasValue)
                batchingOptions.RetryTimeLimit = options.RetryTimeLimit.Value;

            // BatchingSink disposes the batched sink it wraps, which is how the sink's
            // HttpClient gets cleaned up on Log.CloseAndFlush().
            return loggerSinkConfiguration.Sink(
                new PostmarkEmailSink(options),
                batchingOptions,
                restrictedToMinimumLevel,
                levelSwitch);
        }

        /// <summary>
        /// Sends batched log events as email through the Postmark API. Every parameter is a
        /// primitive or string so that this overload can be driven from <c>appsettings.json</c>
        /// via <c>Serilog.Settings.Configuration</c>.
        /// </summary>
        /// <param name="loggerSinkConfiguration">The <c>WriteTo</c> configuration object.</param>
        /// <param name="serverToken">The Postmark <em>server</em> token. Account tokens cannot send email.</param>
        /// <param name="from">The sender address; must be a verified Postmark sender signature.</param>
        /// <param name="to">Recipient address(es), separated by commas or semicolons. Postmark accepts at most 50.</param>
        /// <param name="cc">Optional carbon-copy recipient(s).</param>
        /// <param name="bcc">Optional blind-carbon-copy recipient(s).</param>
        /// <param name="replyTo">Optional reply-to address.</param>
        /// <param name="subject">Subject line, as an output template rendered against the most significant event in the batch.</param>
        /// <param name="outputTemplate">Output template applied to every event to build the body.</param>
        /// <param name="isBodyHtml">Send the rendered body as Postmark's <c>HtmlBody</c> rather than <c>TextBody</c>.</param>
        /// <param name="tag">Optional Postmark tag for grouping in statistics.</param>
        /// <param name="messageStream">Optional Postmark message stream ID; Postmark defaults to <c>outbound</c>.</param>
        /// <param name="trackOpens">Optional open tracking; leave null to inherit the server setting.</param>
        /// <param name="restrictedToMinimumLevel">The minimum level for events passed through the sink.</param>
        /// <param name="levelSwitch">A switch allowing the pass-through minimum level to be changed at runtime.</param>
        /// <param name="batchSizeLimit">The maximum number of events packed into a single email.</param>
        /// <param name="bufferingTimeLimit">The longest to wait for a batch to fill. Defaults to 30 seconds.</param>
        /// <param name="queueLimit">Events buffered in memory before new ones are dropped.</param>
        /// <param name="eagerlyEmitFirstEvent">Send the first event immediately instead of waiting for a batch.</param>
        /// <param name="formatProvider">Supplies culture-specific formatting for the subject and body.</param>
        /// <returns>Logger configuration, allowing configuration to continue.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="loggerSinkConfiguration"/> is null.</exception>
        /// <exception cref="ArgumentException">A required argument is missing or invalid.</exception>
        public static LoggerConfiguration PostmarkEmail(
            this LoggerSinkConfiguration loggerSinkConfiguration,
            string serverToken,
            string from,
            string to,
            string? cc = null,
            string? bcc = null,
            string? replyTo = null,
            string subject = PostmarkEmailSinkOptions.DefaultSubject,
            string outputTemplate = PostmarkEmailSinkOptions.DefaultOutputTemplate,
            bool isBodyHtml = false,
            string? tag = null,
            string? messageStream = null,
            bool? trackOpens = null,
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
            LoggingLevelSwitch? levelSwitch = null,
            int batchSizeLimit = PostmarkEmailSinkOptions.DefaultBatchSizeLimit,
            TimeSpan? bufferingTimeLimit = null,
            int queueLimit = PostmarkEmailSinkOptions.DefaultQueueLimit,
            bool eagerlyEmitFirstEvent = false,
            IFormatProvider? formatProvider = null)
        {
            var options = BuildOptions(
                serverToken, from, to, cc, bcc, replyTo, subject, outputTemplate, isBodyHtml,
                tag, messageStream, trackOpens, batchSizeLimit, bufferingTimeLimit, queueLimit,
                eagerlyEmitFirstEvent, formatProvider);

            return loggerSinkConfiguration.PostmarkEmail(options, restrictedToMinimumLevel, levelSwitch);
        }

        /// <summary>
        /// Maps the flat parameters onto an options object. Split out so the mapping can be
        /// asserted directly; a transposed argument in a list this long is otherwise invisible.
        /// </summary>
        internal static PostmarkEmailSinkOptions BuildOptions(
            string serverToken,
            string from,
            string to,
            string? cc,
            string? bcc,
            string? replyTo,
            string subject,
            string outputTemplate,
            bool isBodyHtml,
            string? tag,
            string? messageStream,
            bool? trackOpens,
            int batchSizeLimit,
            TimeSpan? bufferingTimeLimit,
            int queueLimit,
            bool eagerlyEmitFirstEvent,
            IFormatProvider? formatProvider)
        {
            return new PostmarkEmailSinkOptions
            {
                ServerToken = serverToken,
                From = from,
                To = to,
                Cc = cc,
                Bcc = bcc,
                ReplyTo = replyTo,
                Subject = subject,
                OutputTemplate = outputTemplate,
                IsBodyHtml = isBodyHtml,
                Tag = tag,
                MessageStream = messageStream,
                TrackOpens = trackOpens,
                BatchSizeLimit = batchSizeLimit,
                BufferingTimeLimit = bufferingTimeLimit ?? PostmarkEmailSinkOptions.DefaultBufferingTimeLimit,
                QueueLimit = queueLimit,
                EagerlyEmitFirstEvent = eagerlyEmitFirstEvent,
                FormatProvider = formatProvider
            };
        }
    }
}
