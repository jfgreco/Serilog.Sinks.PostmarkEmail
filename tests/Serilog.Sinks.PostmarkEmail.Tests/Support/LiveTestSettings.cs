using System;
using Microsoft.Extensions.Configuration;

namespace Serilog.Sinks.PostmarkEmail.Tests.Support
{
    /// <summary>
    /// Credentials for the opt-in live test, read from .NET user secrets or the environment.
    /// <para>
    /// Both stores live outside the repository, so a real Postmark server token cannot be
    /// committed. Nothing here is checked in, and CI supplies none of it, so the live test skips
    /// itself everywhere except a developer machine that has deliberately configured it.
    /// </para>
    /// <para>Set it up with:</para>
    /// <code>
    /// dotnet user-secrets set "Postmark:ServerToken" "your-token" --project tests/Serilog.Sinks.PostmarkEmail.Tests
    /// dotnet user-secrets set "Postmark:From"        "logs@yourdomain.com" --project tests/Serilog.Sinks.PostmarkEmail.Tests
    /// dotnet user-secrets set "Postmark:To"          "you@yourdomain.com"  --project tests/Serilog.Sinks.PostmarkEmail.Tests
    /// </code>
    /// <para>
    /// Or export <c>POSTMARK__SERVERTOKEN</c>, <c>POSTMARK__FROM</c> and <c>POSTMARK__TO</c>.
    /// </para>
    /// </summary>
    static class LiveTestSettings
    {
        static readonly IConfigurationRoot Configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(LiveTestSettings).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        public static string? ServerToken => Value("Postmark:ServerToken");

        public static string? From => Value("Postmark:From");

        public static string? To => Value("Postmark:To");

        /// <summary>
        /// True only when all three values are present. Supplying a token is the opt-in: with it
        /// set, the live test sends a real email through Postmark and a real message is delivered.
        /// </summary>
        public static bool IsConfigured =>
            !IsBlank(ServerToken) && !IsBlank(From) && !IsBlank(To);

        /// <summary>Explains which pieces are missing, for the skip message.</summary>
        public static string MissingDescription
        {
            get
            {
                var missing = string.Empty;
                if (IsBlank(ServerToken)) missing += "Postmark:ServerToken ";
                if (IsBlank(From)) missing += "Postmark:From ";
                if (IsBlank(To)) missing += "Postmark:To ";
                return missing.Trim();
            }
        }

        static string? Value(string key)
        {
            // Environment variables use __ as the section separator.
            var v = Configuration[key];
            return IsBlank(v) ? Environment.GetEnvironmentVariable(key.Replace(":", "__").ToUpperInvariant()) : v;
        }

        static bool IsBlank(string? value) => value == null || value.Trim().Length == 0;
    }
}
