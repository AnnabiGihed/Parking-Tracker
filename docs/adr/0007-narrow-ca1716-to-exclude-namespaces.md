# ADR-0007: Exclude namespaces from CA1716 so the mandatory Features/Shared folder builds

- Status: Accepted
- Date: 2026-11-17
- Deciders: Gihed Annabi
- Expires: 2027-11-17

## Context

The Pivot.Framework feature-folder layout (`dotnet-ddd-cqrs-conventions`) is mandatory and puts cross-feature code in
`Features/Shared/`. Namespaces follow folders, so public types live in namespaces such as
`ParkingTracker.Domain.Features.Shared.Services`. Rule CA1716 reports `Shared` because it is a reserved keyword in
Visual Basic, and with warnings as errors the build fails.

The solution is an Android app written only in C#; no assembly is published as a library for other languages. Renaming
the folder would break the house layout. Switching CA1716 off entirely would also stop it from checking type, member
and parameter names.

## Decision

We narrow CA1716 in the root `.editorconfig` instead of switching it off:
`dotnet_code_quality.CA1716.analyzed_symbol_kinds = NamedType, Method, Property, Event, Parameter`. Namespaces are no
longer checked; every other symbol kind still is. The line carries the rule ID, this ADR, the author and the date.

## Consequences

### Positive

- The mandatory `Features/Shared/` folder and folder-matching namespaces build without renaming.
- Type, member and parameter names are still checked against reserved keywords.

### Negative

- A namespace segment that is a keyword in another .NET language is no longer reported.

## Review

Before 2027-11-17, check whether the layout convention or CA1716 changed; renew this waiver or remove the setting.
