using System;
using System.Collections.Generic;
using Serilog.Debugging;

namespace Serilog.Sinks.PostmarkEmail
{
    /// <summary>
    /// Turns a user-supplied recipient string into the comma-separated form the Postmark
    /// API expects, applying Postmark's per-field recipient cap.
    /// </summary>
    static class RecipientList
    {
        /// <summary>
        /// Postmark accepts at most 50 recipients in each of To, Cc and Bcc.
        /// </summary>
        internal const int MaxRecipientsPerField = 50;

        /// <summary>
        /// Only ',' and ';' separate addresses. Whitespace is deliberately NOT a separator:
        /// Postmark accepts the "Display Name &lt;addr@example.com&gt;" form, and splitting on
        /// spaces would tear those apart.
        /// </summary>
        static readonly char[] Separators = { ',', ';' };

        /// <summary>
        /// Splits, trims and de-duplicates (case-insensitively, preserving first-seen order)
        /// the addresses in <paramref name="value"/>.
        /// </summary>
        public static IReadOnlyList<string> Parse(string? value)
        {
            if (value == null || value.Length == 0)
                return Array.Empty<string>();

            var parts = value.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>(parts.Length);

            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.Length == 0)
                    continue;
                if (seen.Add(trimmed))
                    result.Add(trimmed);
            }

            return result;
        }

        /// <summary>
        /// Renders <paramref name="value"/> as a Postmark recipient field, or <c>null</c> when it
        /// contains no addresses. Recipients beyond <see cref="MaxRecipientsPerField"/> are dropped
        /// with a <see cref="SelfLog"/> warning, because Postmark would reject the whole message.
        /// </summary>
        public static string? Format(string? value, string fieldName)
        {
            var addresses = Parse(value);
            if (addresses.Count == 0)
                return null;

            if (addresses.Count > MaxRecipientsPerField)
            {
                // SelfLog.WriteLine accepts at most three format arguments.
                SelfLog.WriteLine(
                    "Serilog.Sinks.PostmarkEmail: {0} lists {1} recipients but Postmark accepts at most {2} per field; dropping the excess.",
                    fieldName, addresses.Count, MaxRecipientsPerField);

                var capped = new string[MaxRecipientsPerField];
                for (var i = 0; i < MaxRecipientsPerField; i++)
                    capped[i] = addresses[i];
                return string.Join(",", capped);
            }

            return string.Join(",", addresses);
        }
    }
}
