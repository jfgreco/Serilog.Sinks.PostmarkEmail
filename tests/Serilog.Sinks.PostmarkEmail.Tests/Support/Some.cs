using System;
using System.Collections.Generic;
using Serilog.Events;
using Serilog.Parsing;

namespace Serilog.Sinks.PostmarkEmail.Tests.Support
{
    /// <summary>
    /// Fixed-value factories, so assertions can compare against literals.
    /// </summary>
    static class Some
    {
        public static readonly DateTimeOffset Timestamp =
            new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

        static readonly MessageTemplateParser Parser = new();

        public static LogEvent LogEvent(
            LogEventLevel level = LogEventLevel.Information,
            string messageTemplate = "A message",
            Exception? exception = null) =>
            new LogEvent(
                Timestamp,
                level,
                exception,
                Parser.Parse(messageTemplate),
                Array.Empty<LogEventProperty>());

        public static PostmarkEmailSinkOptions Options(StubHttpMessageHandler handler) =>
            new PostmarkEmailSinkOptions
            {
                ServerToken = "test-server-token",
                From = "logs@example.com",
                To = "ops@example.com",
                MessageHandler = handler
            };

        public static IReadOnlyCollection<LogEvent> Batch(params LogEvent[] events) => events;
    }
}
