# ADR-0005: Waive StyleCop SA1514 where it conflicts with the region convention

- Status: Accepted
- Date: 2026-11-16
- Deciders: Gihed Annabi
- Expires: 2027-11-16

## Context

The solution enables the shared analyzer set (SonarAnalyzer.CSharp and StyleCop.Analyzers) with warnings as errors.
StyleCop rule SA1514 requires a blank line before a member's documentation header. The house `csharp-regions`
convention puts the first member's documentation header directly after `#region`, with no blank line. Both rules
cannot hold at the same time, so every documented first member in a region would break the build.

The `clean-code-static-analysis` rules require a waiver ADR with an expiry date for a project-wide suppression (R25).

## Decision

We set `dotnet_diagnostic.SA1514.severity = none` in the root `.editorconfig`, with the rule ID, the justification,
the author and the date at that line. The `csharp-regions` convention wins because the documentation gate and the
reviewers enforce it on every C# file.

## Consequences

### Positive

- The build stays warning-free without blank-line churn that the region convention forbids.
- The suppression is visible in one place with its reason.

### Negative

- A blank line before a documentation header outside a region is no longer checked by SA1514.

## Review

Before 2027-11-16, check whether StyleCop or the region convention changed; renew this waiver or remove the
suppression. An expired waiver blocks the next release (R25).
