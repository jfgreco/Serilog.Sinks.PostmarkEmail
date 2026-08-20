using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog.Events;
using Serilog.Sinks.PostmarkEmail.Tests.Support;

using Xunit;

namespace Serilog.Sinks.PostmarkEmail.Tests
{
    public class PostmarkEmailSinkTests
    {
        static PostmarkEmailSink CreateSink(StubHttpMessageHandler handler, Action<PostmarkEmailSinkOptions>? configure = null)
        {
            var options = Some.Options(handler);
            configure?.Invoke(options);
            options.Validate("options");
            return new PostmarkEmailSink(options);
        }

        // -- Request shape ----------------------------------------------------

        [Fact]
        public async Task PostsToThePostmarkEmailEndpoint()
        {
            using var handler = new StubHttpMessageHandler();
            using var sink = CreateSink(handler);

            await sink.EmitBatchAsync(Some.Batch(Some.LogEvent()));

            var request = handler.SingleRequest;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.postmarkapp.com/email", request.RequestUri!.ToString());
        }

        [Fact]
        public async Task SendsTheServerTokenHeader()
        {
            using var handler = new StubHttpMessageHandler();
            using var sink = CreateSink(handler, o => o.ServerToken = "abc-123");

            await sink.EmitBatchAsync(Some.Batch(Some.LogEvent()));

            var values = handler.SingleRequest.Headers.GetValues("X-Postmark-Server-Token");
            Assert.Equal("abc-123", Assert.Single(values));
        }

        [Fact]
        public async Task HonoursAnOverriddenServerUrl()
        {
            using var handler = new StubHttpMessageHandler();
            using var sink = CreateSink(handler, o => o.ServerUrl = new Uri("https://proxy.internal/postmark/"));

            await sink.EmitBatchAsync(Some.Batch(Some.LogEvent()));

            Assert.Equal("https://proxy.internal/postmark/email", handler.SingleRequest.RequestUri!.ToString());
        }

        [Fact]
        public async Task SendsJsonContent()
        {
            using var handler = new StubHttpMessageHandler();
            using var sink = CreateSink(handler);

            await sink.EmitBatchAsync(Some.Batch(Some.LogEvent()));

            Assert.Equal("application/json", handler.SingleRequest.Content!.Headers.ContentType!.MediaType);
        }

        // -- Body -------------------------------------------------------------

        [Fact]
        public async Task WritesEveryEventToTheBodyInBatchOrder()
        {
            using var handler = new StubHttpMessageHandler();
            using var sink = CreateSink(handler, o => o.OutputTemplate = "{Message}|");

            await sink.EmitBatchAsync(Some.Batch(
                Some.LogEvent(LogEventLevel.Information, "first"),
                Some.LogEvent(LogEventLevel.Warning, "second"),
                Some.LogEvent(LogEventLevel.Error, "third")));

            Assert.Equal("first|second|third|", handler.SingleBody.GetProperty("TextBody").GetString());
        }

        [Fact]
        public async Task SendsTextBodyByDefaultAndNoHtmlBody()
        {
            using var handler = new StubHttpMessageHandler();
            using var sink = CreateSink(handler, o => o.OutputTemplate = "{Message}");

            await sink.EmitBatchAsync(Some.Batch(Some.LogEvent(messageTemplate: "plain")));

            var body = handler.SingleBody;
            Assert.Equal("plain", body.GetProperty("TextBody").GetString());
            Assert.False(body.TryGetProperty("HtmlBody", out _));
        }

        [Fact]
        public async Task SendsHtmlBodyWhenConfiguredAndNoTextBody()
        {
            using var handler = new StubHttpMessageHandler();
            using var sink = CreateSink(handler, o =>
            {
                o.IsBodyHtml = true;
                o.OutputTemplate = "<p>{Message}</p>";
            });

            await sink.EmitBatchAsync(Some.Batch(Some.LogEvent(messageTemplate: "styled")));

            var body = handler.SingleBody;
            Assert.Equal("<p>styled</p>", body.GetProperty("HtmlBody").GetString());
            Assert.False(body.TryGetProperty("TextBody", out _));
        }

        [Fact]
        public async Task IncludesTheExceptionWhenTheTemplateAsksForIt()
        {
            using var handler = new StubHttpMessageHandler();
            using var sink = CreateSink(handler, o => o.OutputTemplate = "{Message}{NewLine}{Exception}");

            await sink.EmitBatchAsync(Some.Batch(
                Some.LogEvent(LogEventLevel.Error, "boom", new InvalidOperationException("the cause"))));

            Assert.Contains("the cause", handler.SingleBody.GetProperty("TextBody").GetString());
        }

        // -- Subject ----------------------------------------------------------

        [Fact]
        public async Task RendersTheSubjectTemplateRatherThanSendingItVerbatim()
        {
            // Regression guard: the SendGrid sink this was modelled on formats the subject and
            // then sends the raw, unrendered template string instead of the result.
            using var handler = new StubHttpMessageHandler();
            using var sink = CreateSink(handler, o => o.Subject = "[{Level}] {Message}");

            await sink.EmitBatchAsync(Some.Batch(Some.LogEvent(LogEventLevel.Error, "disk full")));

            Assert.Equal("[Error] disk full", handler.SingleBody.GetProperty("Subject").GetString());
        }

        [Fact]
        public async Task RendersTheSubjectAgainstTheMostSignificantEventInTheBatch()
        {
            using var handler = new StubHttpMessageHandler();
            using var sink = CreateSink(handler, o => o.Subject = "[{Level}] {Message}");

            await sink.EmitBatchAsync(Some.Batch(
                Some.LogEvent(LogEventLevel.Information, "starting"),
                Some.LogEvent(LogEventLevel.Fatal, "the important one"),
                Some.LogEvent(LogEventLevel.Warning, "trailing")));

            Assert.Equal("[Fatal] the important one", handler.SingleBody.GetProperty("Subject").GetString());
        }

        [Fact]
        public async Task BreaksTiesOnLevelByChoosingTheEarliestEvent()
        {
            using var handler = new StubHttpMessageHandler();
            using var sink = CreateSink(handler, o => o.Subject = "{Message}");

            await sink.EmitBatchAsync(Some.Batch(
                Some.LogEvent(LogEventLevel.Error, "first error"),
                Some.LogEvent(LogEventLevel.Error, "second error")));

            Assert.Equal("first error", handler.SingleBody.GetProperty("Subject").GetString());
        }

        [Fact]
        public async Task CollapsesWhitespaceInTheSubjectBecauseHeadersCannotSpanLines()
        {
            using var handler = new StubHttpMessageHandler();
            using var sink = CreateSink(handler, o => o.Subject = "{Message}");

            await sink.EmitBatchAsync(Some.Batch(
                Some.LogEvent(LogEventLevel.Error, "line one\r\nline two\t\tindented   wide")));

            Assert.Equal("line one line two indented wide", handler.SingleBody.GetProperty("Subject").GetString());
        }

        [Fact]
        public async Task TruncatesAnOverlongSubject()
        {
            using var handler = new StubHttpMessageHandler();
            using var sink = CreateSink(handler, o => o.Subject = "{Message}");

            await sink.EmitBatchAsync(Some.Batch(
                Some.LogEvent(LogEventLevel.Error, new string('x', 400))));

            var subject = handler.SingleBody.GetProperty("Subject").GetString()!;
            Assert.Equal(PostmarkEmailSink.MaxSubjectLength, subject.Length);
            Assert.EndsWith("...", subject);
        }

        [Fact]
        public async Task UsesTheDefaultSubjectWhenNoneIsConfigured()
        {
            using var handler = new StubHttpMessageHandler();
            using var sink = CreateSink(handler);

            await sink.EmitBatchAsync(Some.Batch(Some.LogEvent()));

            Assert.Equal(PostmarkEmailSinkOptions.DefaultSubject, handler.SingleBody.GetProperty("Subject").GetString());
        }

        // -- Optional fields --------------------------------------------------

        [Fact]
        public async Task OmitsUnsetOptionalFieldsEntirely()
        {
            using var handler = new StubHttpMessageHandler();
            using var sink = CreateSink(handler);

            await sink.EmitBatchAsync(Some.Batch(Some.LogEvent()));

            var body = handler.SingleBody;
            foreach (var absent in new[] { "Cc", "Bcc", "ReplyTo", "Tag", "MessageStream", "TrackOpens" })
                Assert.False(body.TryGetProperty(absent, out _), $"Expected {absent} to be absent from the request.");
        }

        [Fact]
        public async Task SendsOptionalFieldsWhenConfigured()
        {
            using var handler = new StubHttpMessageHandler();
            using var sink = CreateSink(handler, o =>
            {
                o.Cc = "cc1@x.com; cc2@x.com";
                o.Bcc = "bcc@x.com";
                o.ReplyTo = "noreply@x.com";
                o.Tag = "serilog";
                o.MessageStream = "alerts";
                o.TrackOpens = false;
            });

            await sink.EmitBatchAsync(Some.Batch(Some.LogEvent()));

            var body = handler.SingleBody;
            Assert.Equal("logs@example.com", body.GetProperty("From").GetString());
            Assert.Equal("ops@example.com", body.GetProperty("To").GetString());
            Assert.Equal("cc1@x.com,cc2@x.com", body.GetProperty("Cc").GetString());
            Assert.Equal("bcc@x.com", body.GetProperty("Bcc").GetString());
            Assert.Equal("noreply@x.com", body.GetProperty("ReplyTo").GetString());
            Assert.Equal("serilog", body.GetProperty("Tag").GetString());
            Assert.Equal("alerts", body.GetProperty("MessageStream").GetString());
            Assert.False(body.GetProperty("TrackOpens").GetBoolean());
        }

        [Fact]
        public async Task NormalisesMultipleRecipientsIntoPostmarksCommaSeparatedForm()
        {
            using var handler = new StubHttpMessageHandler();
            using var sink = CreateSink(handler, o => o.To = "a@x.com; b@x.com , a@X.com");

            await sink.EmitBatchAsync(Some.Batch(Some.LogEvent()));

            Assert.Equal("a@x.com,b@x.com", handler.SingleBody.GetProperty("To").GetString());
        }

        // -- Batching contract ------------------------------------------------

        [Fact]
        public async Task SendsNothingForAnEmptyBatch()
        {
            using var handler = new StubHttpMessageHandler();
            using var sink = CreateSink(handler);

            await sink.EmitBatchAsync(Array.Empty<LogEvent>());

            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task OnEmptyBatchAsyncDoesNothing()
        {
            using var handler = new StubHttpMessageHandler();
            using var sink = CreateSink(handler);

            await sink.OnEmptyBatchAsync();

            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task RejectsANullBatch()
        {
            using var handler = new StubHttpMessageHandler();
            using var sink = CreateSink(handler);

            await Assert.ThrowsAsync<ArgumentNullException>(() => sink.EmitBatchAsync(null!));
        }

        // -- HttpClient ownership ---------------------------------------------

        [Fact]
        public void DoesNotDisposeACallerSuppliedMessageHandler()
        {
            using var handler = new StubHttpMessageHandler();
            var sink = CreateSink(handler);

            sink.Dispose();

            Assert.False(handler.Disposed);
        }

        [Fact]
        public async Task DoesNotDisposeABorrowedHttpClient()
        {
            using var handler = new StubHttpMessageHandler();
            using var client = new HttpClient(handler, disposeHandler: false);

            var options = Some.Options(handler);
            options.MessageHandler = null;
            options.HttpClient = client;
            options.Validate("options");

            var sink = new PostmarkEmailSink(options);
            await sink.EmitBatchAsync(Some.Batch(Some.LogEvent()));
            sink.Dispose();

            // The borrowed client must still be usable after the sink is gone.
            using var response = await client.GetAsync(
                "https://api.postmarkapp.com/still-alive", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task DoesNotMutateABorrowedHttpClient()
        {
            using var handler = new StubHttpMessageHandler();
            using var client = new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://unrelated.example.com/"),
                Timeout = TimeSpan.FromSeconds(7)
            };

            var options = Some.Options(handler);
            options.MessageHandler = null;
            options.HttpClient = client;
            options.HttpTimeout = TimeSpan.FromSeconds(99);
            options.Validate("options");

            using var sink = new PostmarkEmailSink(options);
            await sink.EmitBatchAsync(Some.Batch(Some.LogEvent()));

            Assert.Equal(new Uri("https://unrelated.example.com/"), client.BaseAddress);
            Assert.Equal(TimeSpan.FromSeconds(7), client.Timeout);
            Assert.Empty(client.DefaultRequestHeaders);

            // ...and the request still went to Postmark, via an absolute URI.
            Assert.Equal("https://api.postmarkapp.com/email", handler.Requests[0].RequestUri!.ToString());
        }
    }

    [Collection(NonParallelCollection.Name)]
    public class PostmarkEmailSinkErrorHandlingTests
    {
        static PostmarkEmailSink CreateSink(StubHttpMessageHandler handler)
        {
            var options = Some.Options(handler);
            options.Validate("options");
            return new PostmarkEmailSink(options);
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.BadGateway)]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        [InlineData(HttpStatusCode.GatewayTimeout)]
        [InlineData(HttpStatusCode.RequestTimeout)]
        [InlineData((HttpStatusCode)429)]
        public async Task ThrowsOnTransientFailuresSoTheBatchIsRetried(HttpStatusCode statusCode)
        {
            using var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Error(statusCode, 0, "try later"));
            using var sink = CreateSink(handler);

            var ex = await Assert.ThrowsAsync<PostmarkApiException>(
                () => sink.EmitBatchAsync(Some.Batch(Some.LogEvent())));

            Assert.Equal(statusCode, ex.StatusCode);
        }

        [Theory]
        [InlineData(HttpStatusCode.BadRequest, 300)]
        [InlineData(HttpStatusCode.Unauthorized, 10)]
        [InlineData(HttpStatusCode.Forbidden, 401)]
        [InlineData((HttpStatusCode)422, 300)]
        public async Task DropsTheBatchOnPermanentFailuresInsteadOfRetryingForTenMinutes(
            HttpStatusCode statusCode, int errorCode)
        {
            using var handler = new StubHttpMessageHandler(
                _ => StubHttpMessageHandler.Error(statusCode, errorCode, "Invalid 'From' address"));
            using var sink = CreateSink(handler);
            using var selfLog = new SelfLogCapture();

            await sink.EmitBatchAsync(Some.Batch(Some.LogEvent()));

            Assert.Contains("dropping batch", selfLog.Output);
            Assert.Contains(errorCode.ToString(), selfLog.Output);
            Assert.Contains("Invalid 'From' address", selfLog.Output);
        }

        [Fact]
        public async Task ReportsAnErrorCodeReturnedAlongsideASuccessStatus()
        {
            // Postmark can answer 200 while still refusing the message.
            using var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Ok(406, "Inactive recipient"));
            using var sink = CreateSink(handler);
            using var selfLog = new SelfLogCapture();

            await sink.EmitBatchAsync(Some.Batch(Some.LogEvent()));

            Assert.Contains("ErrorCode 406", selfLog.Output);
            Assert.Contains("Inactive recipient", selfLog.Output);
        }

        [Fact]
        public async Task StaysQuietOnASuccessfulSend()
        {
            using var handler = new StubHttpMessageHandler();
            using var sink = CreateSink(handler);
            using var selfLog = new SelfLogCapture();

            await sink.EmitBatchAsync(Some.Batch(Some.LogEvent()));

            Assert.Empty(selfLog.Output);
        }

        [Fact]
        public async Task ReportsANonJsonErrorBodyVerbatim()
        {
            using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("<html>gateway says no</html>", System.Text.Encoding.UTF8, "text/html")
            });
            using var sink = CreateSink(handler);
            using var selfLog = new SelfLogCapture();

            await sink.EmitBatchAsync(Some.Batch(Some.LogEvent()));

            Assert.Contains("gateway says no", selfLog.Output);
        }

        [Fact]
        public async Task LetsNetworkFailuresPropagate()
        {
            using var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("connection reset"));
            using var sink = CreateSink(handler);

            await Assert.ThrowsAsync<HttpRequestException>(
                () => sink.EmitBatchAsync(Some.Batch(Some.LogEvent())));
        }
    }

    /// <summary>
    /// End-to-end through a real <see cref="Logger"/>, proving the configuration extension wires
    /// batching, level filtering and flush-on-dispose together correctly.
    /// </summary>
    public class PostmarkEmailConfigurationTests
    {
        [Fact]
        public void FlushesBufferedEventsWhenTheLoggerIsDisposed()
        {
            using var handler = new StubHttpMessageHandler();

            var logger = new LoggerConfiguration()
                .WriteTo.PostmarkEmail(
                    new PostmarkEmailSinkOptions
                    {
                        ServerToken = "token",
                        From = "logs@example.com",
                        To = "ops@example.com",
                        Subject = "[{Level}] {Message}",
                        OutputTemplate = "{Message}|",
                        MessageHandler = handler,
                        BufferingTimeLimit = TimeSpan.FromMinutes(5)
                    },
                    restrictedToMinimumLevel: LogEventLevel.Warning)
                .CreateLogger();

            logger.Information("ignored, below the minimum level");
            logger.Error("the only event that reaches the sink");

            // Disposing the logger disposes the BatchingSink, which flushes and then disposes the
            // batched sink underneath it. Exactly one event reaches the sink, and one event cannot
            // be split, so this holds no matter how the batching worker is scheduled.
            logger.Dispose();

            var body = JsonDocument.Parse(Assert.Single(handler.Bodies)).RootElement;
            Assert.Equal("the only event that reaches the sink|", body.GetProperty("TextBody").GetString());
            Assert.Equal("[Error] the only event that reaches the sink", body.GetProperty("Subject").GetString());
        }

        [Fact]
        public void DeliversEveryBufferedEventOnDisposeHoweverTheSchedulerGroupsThem()
        {
            using var handler = new StubHttpMessageHandler();

            var logger = new LoggerConfiguration()
                .WriteTo.PostmarkEmail(
                    new PostmarkEmailSinkOptions
                    {
                        ServerToken = "token",
                        From = "logs@example.com",
                        To = "ops@example.com",
                        OutputTemplate = "{Message}|",
                        MessageHandler = handler,
                        BufferingTimeLimit = TimeSpan.FromMinutes(5)
                    },
                    restrictedToMinimumLevel: LogEventLevel.Warning)
                .CreateLogger();

            logger.Information("ignored, below the minimum level");
            logger.Warning("first");
            logger.Error("second");
            logger.Dispose();

            // How many emails these two events land in is Serilog's scheduling decision, not a
            // contract of this sink: the batching worker can drain the queue between the two Write
            // calls, producing two batches. Asserting a single request here made this test fail
            // roughly a third of the time. Assert what the sink actually guarantees instead --
            // every event delivered, in order, with sub-minimum-level events filtered out.
            var delivered = string.Concat(handler.Bodies.Select(
                b => JsonDocument.Parse(b).RootElement.GetProperty("TextBody").GetString()));

            Assert.NotEmpty(handler.Bodies);
            Assert.Equal("first|second|", delivered);
        }

        [Fact]
        public void FlatOverloadMapsEveryParameterOntoTheRightOption()
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

            var options = PostmarkEmailLoggerConfigurationExtensions.BuildOptions(
                serverToken: "the-token",
                from: "from@x.com",
                to: "to@x.com",
                cc: "cc@x.com",
                bcc: "bcc@x.com",
                replyTo: "reply@x.com",
                subject: "the-subject",
                outputTemplate: "the-template",
                isBodyHtml: true,
                tag: "the-tag",
                messageStream: "the-stream",
                trackOpens: true,
                batchSizeLimit: 7,
                bufferingTimeLimit: TimeSpan.FromMinutes(3),
                queueLimit: 11,
                eagerlyEmitFirstEvent: true,
                formatProvider: culture,
                serverUrl: "https://proxy.internal/postmark/",
                httpTimeout: TimeSpan.FromSeconds(13),
                retryTimeLimit: TimeSpan.FromMinutes(2));

            Assert.Equal("the-token", options.ServerToken);
            Assert.Equal("from@x.com", options.From);
            Assert.Equal("to@x.com", options.To);
            Assert.Equal("cc@x.com", options.Cc);
            Assert.Equal("bcc@x.com", options.Bcc);
            Assert.Equal("reply@x.com", options.ReplyTo);
            Assert.Equal("the-subject", options.Subject);
            Assert.Equal("the-template", options.OutputTemplate);
            Assert.True(options.IsBodyHtml);
            Assert.Equal("the-tag", options.Tag);
            Assert.Equal("the-stream", options.MessageStream);
            Assert.True(options.TrackOpens);
            Assert.Equal(7, options.BatchSizeLimit);
            Assert.Equal(TimeSpan.FromMinutes(3), options.BufferingTimeLimit);
            Assert.Equal(11, options.QueueLimit);
            Assert.True(options.EagerlyEmitFirstEvent);
            Assert.Same(culture, options.FormatProvider);
            Assert.Equal(new Uri("https://proxy.internal/postmark/"), options.ServerUrl);
            Assert.Equal(TimeSpan.FromSeconds(13), options.HttpTimeout);
            Assert.Equal(TimeSpan.FromMinutes(2), options.RetryTimeLimit);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void FlatOverloadLeavesServerUrlUnsetSoTheDefaultEndpointApplies(string? serverUrl)
        {
            var options = PostmarkEmailLoggerConfigurationExtensions.BuildOptions(
                "token", "from@x.com", "to@x.com", null, null, null,
                PostmarkEmailSinkOptions.DefaultSubject, PostmarkEmailSinkOptions.DefaultOutputTemplate,
                false, null, null, null, PostmarkEmailSinkOptions.DefaultBatchSizeLimit,
                null, PostmarkEmailSinkOptions.DefaultQueueLimit, false, null,
                serverUrl: serverUrl);

            Assert.Null(options.ServerUrl);
        }

        [Theory]
        [InlineData("not a uri")]
        [InlineData("api.postmarkapp.com")]
        // On Unix these parse as absolute file:// URIs, so checking UriKind.Absolute alone would
        // accept them on Linux and reject them on Windows. The scheme check makes it consistent.
        [InlineData("/relative/path")]
        [InlineData("file:///tmp/postmark")]
        [InlineData("ftp://example.com/")]
        public void FlatOverloadRejectsAServerUrlThatIsNotHttp(string serverUrl)
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                PostmarkEmailLoggerConfigurationExtensions.BuildOptions(
                    "token", "from@x.com", "to@x.com", null, null, null,
                    PostmarkEmailSinkOptions.DefaultSubject, PostmarkEmailSinkOptions.DefaultOutputTemplate,
                    false, null, null, null, PostmarkEmailSinkOptions.DefaultBatchSizeLimit,
                    null, PostmarkEmailSinkOptions.DefaultQueueLimit, false, null,
                    serverUrl: serverUrl));

            Assert.Equal("serverUrl", ex.ParamName);
        }

        [Fact]
        public void FlatOverloadFallsBackToTheDefaultHttpTimeout()
        {
            var options = PostmarkEmailLoggerConfigurationExtensions.BuildOptions(
                "token", "from@x.com", "to@x.com", null, null, null,
                PostmarkEmailSinkOptions.DefaultSubject, PostmarkEmailSinkOptions.DefaultOutputTemplate,
                false, null, null, null, PostmarkEmailSinkOptions.DefaultBatchSizeLimit,
                null, PostmarkEmailSinkOptions.DefaultQueueLimit, false, null);

            Assert.Equal(PostmarkEmailSinkOptions.DefaultHttpTimeout, options.HttpTimeout);
            Assert.Null(options.RetryTimeLimit); // null means Serilog's own default
        }

        [Fact]
        public async Task ServerUrlFromTheFlatOverloadReachesTheSink()
        {
            // The whole point of exposing serverUrl on the flat overload is that an
            // appsettings.json-configured sink can be pointed somewhere other than Postmark.
            using var handler = new StubHttpMessageHandler();

            var options = PostmarkEmailLoggerConfigurationExtensions.BuildOptions(
                "token", "from@x.com", "to@x.com", null, null, null,
                PostmarkEmailSinkOptions.DefaultSubject, "{Message}",
                false, null, null, null, PostmarkEmailSinkOptions.DefaultBatchSizeLimit,
                null, PostmarkEmailSinkOptions.DefaultQueueLimit, false, null,
                serverUrl: "https://proxy.internal/postmark/");

            options.MessageHandler = handler;
            options.Validate("options");

            using var sink = new PostmarkEmailSink(options);
            await sink.EmitBatchAsync(Some.Batch(Some.LogEvent()));

            Assert.Equal("https://proxy.internal/postmark/email", handler.SingleRequest.RequestUri!.ToString());
        }

        [Fact]
        public void FlatOverloadFallsBackToTheDefaultBufferingTimeLimit()
        {
            var options = PostmarkEmailLoggerConfigurationExtensions.BuildOptions(
                "token", "from@x.com", "to@x.com", null, null, null,
                PostmarkEmailSinkOptions.DefaultSubject, PostmarkEmailSinkOptions.DefaultOutputTemplate,
                false, null, null, null, PostmarkEmailSinkOptions.DefaultBatchSizeLimit,
                bufferingTimeLimit: null, PostmarkEmailSinkOptions.DefaultQueueLimit, false, null);

            Assert.Equal(PostmarkEmailSinkOptions.DefaultBufferingTimeLimit, options.BufferingTimeLimit);
        }
    }
}
