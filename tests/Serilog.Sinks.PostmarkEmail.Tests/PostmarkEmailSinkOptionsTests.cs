using System;
using System.Net.Http;
using Serilog.Events;
using Serilog.Sinks.PostmarkEmail.Tests.Support;

using Xunit;

namespace Serilog.Sinks.PostmarkEmail.Tests
{
    public class PostmarkEmailSinkOptionsTests
    {
        static PostmarkEmailSinkOptions Valid() => new()
        {
            ServerToken = "token",
            From = "logs@example.com",
            To = "ops@example.com"
        };

        [Fact]
        public void ValidOptionsPassValidation() =>
            Valid().Validate("options");

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ServerTokenIsRequired(string? token)
        {
            var options = Valid();
            options.ServerToken = token;

            var ex = Assert.Throws<ArgumentException>(() => options.Validate("options"));
            Assert.Contains(nameof(PostmarkEmailSinkOptions.ServerToken), ex.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void FromIsRequired(string? from)
        {
            var options = Valid();
            options.From = from;

            var ex = Assert.Throws<ArgumentException>(() => options.Validate("options"));
            Assert.Contains(nameof(PostmarkEmailSinkOptions.From), ex.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" , ; ")]
        public void AtLeastOneRecipientIsRequired(string? to)
        {
            var options = Valid();
            options.To = to;

            var ex = Assert.Throws<ArgumentException>(() => options.Validate("options"));
            Assert.Contains(nameof(PostmarkEmailSinkOptions.To), ex.Message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void BatchSizeLimitMustBePositive(int limit)
        {
            var options = Valid();
            options.BatchSizeLimit = limit;

            var ex = Assert.Throws<ArgumentException>(() => options.Validate("options"));
            Assert.Contains(nameof(PostmarkEmailSinkOptions.BatchSizeLimit), ex.Message);
        }

        [Fact]
        public void QueueLimitMustBePositiveWhenSet()
        {
            var options = Valid();
            options.QueueLimit = 0;

            var ex = Assert.Throws<ArgumentException>(() => options.Validate("options"));
            Assert.Contains(nameof(PostmarkEmailSinkOptions.QueueLimit), ex.Message);
        }

        [Fact]
        public void NullQueueLimitMeansUnboundedAndIsAllowed()
        {
            var options = Valid();
            options.QueueLimit = null;

            options.Validate("options");
        }

        [Fact]
        public void BufferingTimeLimitMustBePositive()
        {
            var options = Valid();
            options.BufferingTimeLimit = TimeSpan.Zero;

            var ex = Assert.Throws<ArgumentException>(() => options.Validate("options"));
            Assert.Contains(nameof(PostmarkEmailSinkOptions.BufferingTimeLimit), ex.Message);
        }

        [Fact]
        public void ServerUrlMustBeHttpOrHttps()
        {
            var options = Valid();
            options.ServerUrl = new Uri("file:///tmp/postmark");

            var ex = Assert.Throws<ArgumentException>(() => options.Validate("options"));
            Assert.Contains(nameof(PostmarkEmailSinkOptions.ServerUrl), ex.Message);
        }

        [Theory]
        [InlineData("http://localhost:8080/")]
        [InlineData("https://proxy.internal/postmark/")]
        public void ServerUrlAcceptsHttpAndHttps(string url)
        {
            var options = Valid();
            options.ServerUrl = new Uri(url);

            options.Validate("options");
        }

        [Fact]
        public void HttpClientAndMessageHandlerAreMutuallyExclusive()
        {
            using var handler = new StubHttpMessageHandler();
            using var client = new HttpClient();

            var options = Valid();
            options.HttpClient = client;
            options.MessageHandler = handler;

            var ex = Assert.Throws<ArgumentException>(() => options.Validate("options"));
            Assert.Contains("mutually exclusive", ex.Message);
        }

        [Fact]
        public void EagerlyEmitFirstEventDefaultsToFalseUnlikeSerilogsOwnDefault() =>
            Assert.False(new PostmarkEmailSinkOptions().EagerlyEmitFirstEvent);

        [Fact]
        public void ConfigurationRejectsInvalidOptionsAtStartup()
        {
            var options = new PostmarkEmailSinkOptions { From = "logs@example.com", To = "ops@example.com" };

            Assert.Throws<ArgumentException>(() =>
                new LoggerConfiguration().WriteTo.PostmarkEmail(options).CreateLogger());
        }

        [Fact]
        public void ConfigurationRejectsNullOptions()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new LoggerConfiguration().WriteTo.PostmarkEmail((PostmarkEmailSinkOptions)null!).CreateLogger());
        }

        [Fact]
        public void FlatOverloadRejectsMissingServerToken()
        {
            Assert.Throws<ArgumentException>(() =>
                new LoggerConfiguration()
                    .WriteTo.PostmarkEmail(serverToken: "", from: "logs@example.com", to: "ops@example.com")
                    .CreateLogger());
        }

        [Fact]
        public void FlatOverloadAcceptsMinimumLevel()
        {
            using var handler = new StubHttpMessageHandler();
            var options = Valid();
            options.MessageHandler = handler;

            using var logger = new LoggerConfiguration()
                .WriteTo.PostmarkEmail(options, restrictedToMinimumLevel: LogEventLevel.Error)
                .CreateLogger();

            logger.Information("filtered out");

            Assert.Empty(handler.Requests);
        }
    }
}
