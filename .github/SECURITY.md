# Security Policy

## Supported versions

The latest released version receives security fixes. Older versions do not.

| Version | Supported |
| --- | --- |
| 1.0.x | Yes |

## Reporting a vulnerability

Please do **not** open a public issue for a security problem.

Use GitHub's private vulnerability reporting: go to the
[Security tab](https://github.com/jfgreco/Serilog.Sinks.PostmarkEmail/security/advisories/new)
and open a draft advisory. That keeps the report private until a fix is available.

Please include the sink version, the target framework, and enough detail to reproduce. You should
get an initial response within a week.

## What matters most in this project

This sink handles a **Postmark server token**, which can send mail as your domain. The failure mode
worth reporting above all others is anything that could cause that token to be written somewhere it
does not belong.

The token is deliberately kept out of diagnostics: it is set as a request header and is never
included in `SelfLog` output. When a send fails, the sink logs the HTTP status, the Postmark
`ErrorCode`, and Postmark's own message — and for a non-JSON error body, at most the first 512
characters of it. If you can construct a case where a token, or any other credential, reaches
`SelfLog`, a log file, or an exception message, that is a vulnerability and we want to hear about it.

Also in scope:

- Log event content escaping into a place it should not reach, such as mail headers. The subject is
  built from a template rendered against a log event, and newlines are collapsed specifically to
  stop a crafted message from injecting into the `Subject` header. A bypass of that is a bug worth
  reporting.
- Anything that causes the sink to send to a recipient other than the configured one.

Out of scope: your own Postmark account configuration, and denial of service caused by logging
volume you control (use `BatchSizeLimit` and `QueueLimit`).
