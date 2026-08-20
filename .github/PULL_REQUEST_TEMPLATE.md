<!-- Thanks for contributing. Keep this short; delete anything that does not apply. -->

## What this changes

<!-- One or two sentences. If it fixes an open issue, write "Fixes #123". -->

## Why

<!-- The problem being solved, not a restatement of the diff. -->

## Checklist

- [ ] `dotnet test` passes locally (remember: **not** `--nologo`, MTP rejects it)
- [ ] A test fails without this change
- [ ] No test reaches the network — handlers are injected via `PostmarkEmailSinkOptions.MessageHandler`
- [ ] No new dependency added to the library (`System.Text.Json` on `netstandard2.0` is the only one)
- [ ] New public members have XML documentation (the build treats a missing comment as an error)
- [ ] No test asserts on how Serilog groups events into batches — see CONTRIBUTING.md

## Public API impact

<!-- Does this change PostmarkEmailSinkOptions or the WriteTo.PostmarkEmail signatures?
     Published versions cannot be re-cut, so breaking changes need a major version. -->

- [ ] No change to the public API
- [ ] Additive only (new optional members)
- [ ] Breaking — needs a major version bump
