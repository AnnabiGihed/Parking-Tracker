using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Sources;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The answer of a live source before it is read: the HTTP status and the body text. Every source client turns its
///              HTTP answer into this and lets the parser do the rest, so that a failed status gives the same message everywhere
///              (<c>&lt;name&gt; HTTP 500</c>).
/// </summary>
/// <param name="HttpStatus">The HTTP status code.</param>
/// <param name="Body">The body text; empty when there is none.</param>
internal sealed record SourceResponse(int HttpStatus, string Body)
{
	#region Properties
	/// <summary>
	/// Gets a value indicating whether the status is a success status (200 to 299).
	/// </summary>
	internal bool IsSuccess => HttpStatus is >= 200 and < 300;
	#endregion

	#region Internal Methods
	/// <summary>
	/// Reads the body when the status is a success; otherwise gives the HTTP failure.
	/// </summary>
	/// <typeparam name="T">The parsed type.</typeparam>
	/// <param name="source">The service that answered.</param>
	/// <param name="what">The name of the data in failure messages.</param>
	/// <param name="parse">Reads the body.</param>
	/// <returns>The parsed value, or the failure.</returns>
	internal Result<T> Read<T>(SourceKind source, string what, Func<string, Result<T>> parse)
	{
		return IsSuccess ? parse(Body) : Result.Failure<T>(SourceFailures.HttpStatus(source, what, HttpStatus));
	}
	#endregion
}
