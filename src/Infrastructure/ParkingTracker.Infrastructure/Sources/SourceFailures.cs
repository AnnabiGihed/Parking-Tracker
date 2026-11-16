using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Sources;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Builds the failures of the live sources. The messages are shown to the user inside "can't verify" answers, so
///              they keep the wording of the first version of the app, for example <c>parking.brussels HTTP 500</c>.
/// </summary>
internal static class SourceFailures
{
	#region Internal Methods
	/// <summary>
	/// Builds the failure for an answer that is not the expected GeoJSON or JSON.
	/// </summary>
	/// <param name="source">The service that answered.</param>
	/// <param name="what">The name of the data in messages, for example <c>parking.brussels</c>.</param>
	/// <returns>The error, coded <c>&lt;Source&gt;.BadResponse</c>.</returns>
	internal static Error BadResponse(SourceKind source, string what) => new($"{source}.BadResponse", $"{what}: unexpected answer");

	/// <summary>
	/// Builds the failure for an HTTP error status.
	/// </summary>
	/// <param name="source">The service that answered.</param>
	/// <param name="what">The name of the data in messages.</param>
	/// <param name="httpStatus">The HTTP status code.</param>
	/// <returns>The error, coded <c>&lt;Source&gt;.Unavailable</c>.</returns>
	internal static Error HttpStatus(SourceKind source, string what, int httpStatus) => new($"{source}.Unavailable", $"{what} HTTP {httpStatus}");

	/// <summary>
	/// Builds the failure for a request that took longer than its limit.
	/// </summary>
	/// <param name="source">The service that did not answer.</param>
	/// <param name="what">The name of the data in messages.</param>
	/// <returns>The error, coded <c>&lt;Source&gt;.Timeout</c>.</returns>
	internal static Error TimedOut(SourceKind source, string what) => new($"{source}.Timeout", $"{what} timed out");

	/// <summary>
	/// Builds the failure for a request that could not reach the service or lost the connection while reading the answer.
	/// </summary>
	/// <param name="source">The service that could not be reached.</param>
	/// <param name="what">The name of the data in messages.</param>
	/// <returns>The error, coded <c>&lt;Source&gt;.Unavailable</c>.</returns>
	internal static Error Unreachable(SourceKind source, string what) => new($"{source}.Unavailable", $"{what} unreachable");
	#endregion
}
