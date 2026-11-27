using ParkingTracker.Contracts.Features.Profiles;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.ProfileSync.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the profile endpoints of the API (ADR-0032), called as the signed-in user. A failed <see cref="Result"/> means
///              the call did not get an answer it can interpret: <c>Account.Network</c> (offline, timeout), <c>Account.NotSignedIn</c>
///              (the session is gone) or <c>Account.Server</c> (any other status).
/// </summary>
public interface IProfileClient
{
	#region Methods
	/// <summary>
	/// Reads the profile.
	/// </summary>
	/// <param name="ifNoneMatchRevision">The revision the phone already holds, so that the server can answer 304; <see langword="null"/> to always get the document.</param>
	/// <param name="cancellationToken">Cancels the call.</param>
	/// <returns>A task with the outcome, or the failure of the call.</returns>
	Task<Result<ProfileGetResult>> GetAsync(long? ifNoneMatchRevision, CancellationToken cancellationToken = default);

	/// <summary>
	/// Writes the profile.
	/// </summary>
	/// <param name="document">The document to store.</param>
	/// <param name="ifMatchRevision">The revision the document replaces, or <see langword="null"/> to create the first one (<c>If-None-Match: *</c>).</param>
	/// <param name="cancellationToken">Cancels the call.</param>
	/// <returns>A task with the outcome, or the failure of the call.</returns>
	Task<Result<ProfilePutResult>> PutAsync(ProfileDocument document, long? ifMatchRevision, CancellationToken cancellationToken = default);
	#endregion
}
