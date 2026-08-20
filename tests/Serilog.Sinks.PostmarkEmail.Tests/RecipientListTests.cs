using System.Linq;
using Serilog.Sinks.PostmarkEmail.Tests.Support;

using Xunit;

namespace Serilog.Sinks.PostmarkEmail.Tests
{
    public class RecipientListTests
    {
        [Theory]
        [InlineData("a@x.com", "a@x.com")]
        [InlineData("a@x.com,b@x.com", "a@x.com,b@x.com")]
        [InlineData("a@x.com; b@x.com", "a@x.com,b@x.com")]
        [InlineData("  a@x.com ,, ; b@x.com  ", "a@x.com,b@x.com")]
        public void SeparatesOnCommasAndSemicolons(string input, string expected) =>
            Assert.Equal(expected, RecipientList.Format(input, "To"));

        [Fact]
        public void PreservesDisplayNamesContainingSpaces()
        {
            // Splitting on whitespace, as the SendGrid sink does, would tear these apart.
            var formatted = RecipientList.Format("Ops Team <ops@x.com>, On Call <oncall@x.com>", "To");

            Assert.Equal("Ops Team <ops@x.com>,On Call <oncall@x.com>", formatted);
        }

        [Fact]
        public void DeduplicatesCaseInsensitivelyKeepingFirstSeenOrder()
        {
            var formatted = RecipientList.Format("b@x.com, A@x.com, a@x.com, b@X.com", "To");

            Assert.Equal("b@x.com,A@x.com", formatted);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData(",;,")]
        public void ReturnsNullWhenThereAreNoAddresses(string? input) =>
            Assert.Null(RecipientList.Format(input, "Cc"));

        [Fact]
        public void ParseReturnsEmptyForNull() =>
            Assert.Empty(RecipientList.Parse(null));
    }

    [Collection(NonParallelCollection.Name)]
    public class RecipientListCapTests
    {
        [Fact]
        public void CapsAtPostmarksFiftyRecipientLimitAndWarns()
        {
            var input = string.Join(",", Enumerable.Range(0, 60).Select(i => $"user{i}@x.com"));

            using var selfLog = new SelfLogCapture();
            var formatted = RecipientList.Format(input, "To");

            var kept = formatted!.Split(',');
            Assert.Equal(RecipientList.MaxRecipientsPerField, kept.Length);
            Assert.Equal("user0@x.com", kept[0]);
            Assert.Equal("user49@x.com", kept[^1]);

            Assert.Contains("60 recipients", selfLog.Output);
            Assert.Contains("at most 50", selfLog.Output);
        }

        [Fact]
        public void DoesNotWarnAtExactlyTheLimit()
        {
            var input = string.Join(",", Enumerable.Range(0, RecipientList.MaxRecipientsPerField).Select(i => $"user{i}@x.com"));

            using var selfLog = new SelfLogCapture();
            var formatted = RecipientList.Format(input, "To");

            Assert.Equal(RecipientList.MaxRecipientsPerField, formatted!.Split(',').Length);
            Assert.Empty(selfLog.Output);
        }
    }
}
