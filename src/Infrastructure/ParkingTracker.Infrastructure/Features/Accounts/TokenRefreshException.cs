using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Raised by the <see cref="AuthenticatedHandler"/> when it cannot get a token for a request because the refresh failed for a reason
///              that is not a rejected session (the network, or a server error). It is an <see cref="HttpRequestException"/>, so the pipeline treats
///              it as a failed request, and it carries the failure so that the client can report the right reason without signing the user out.
/// </summary>
internal sealed class TokenRefreshException : HttpRequestException
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="TokenRefreshException"/> class.
	/// </summary>
	/// <param name="error">Why the token could not be had.</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
	public TokenRefreshException(Error error)
		: base(error?.Message)
	{
		ArgumentNullException.ThrowIfNull(error);
		Error = error;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets why the token could not be had.
	/// </summary>
	public Error Error { get; }
	#endregion
}
