using System;
using System.Net;

namespace Serilog.Sinks.PostmarkEmail
{
    /// <summary>
    /// Thrown when the Postmark API rejects a batch in a way that may succeed if retried.
    /// The Serilog batching infrastructure catches this and re-queues the batch until
    /// <see cref="Serilog.Configuration.BatchingOptions.RetryTimeLimit"/> elapses.
    /// </summary>
    public class PostmarkApiException : Exception
    {
        /// <summary>
        /// The HTTP status code returned by the Postmark API.
        /// </summary>
        public HttpStatusCode StatusCode { get; }

        /// <summary>
        /// The Postmark <c>ErrorCode</c> from the response body, or <c>0</c> when the body
        /// carried no recognizable error code.
        /// </summary>
        public int ErrorCode { get; }

        /// <summary>
        /// Constructs a <see cref="PostmarkApiException"/>.
        /// </summary>
        /// <param name="statusCode">The HTTP status code returned by the Postmark API.</param>
        /// <param name="errorCode">The Postmark <c>ErrorCode</c>, or <c>0</c> if unavailable.</param>
        /// <param name="message">A description of the failure.</param>
        public PostmarkApiException(HttpStatusCode statusCode, int errorCode, string message)
            : base(message)
        {
            StatusCode = statusCode;
            ErrorCode = errorCode;
        }
    }
}
