namespace ParkingTracker.Application.Features.ProfileSync.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The answer of a write of the profile (ADR-0032): stored under a new revision, refused because the revision is stale
///              (412), or refused for another reason that retrying does not fix (400, 413, 428).
/// </summary>
/// <param name="Outcome">Which answer it is.</param>
/// <param name="Revision">The new revision; zero unless <see cref="ProfilePutOutcome.Saved"/>.</param>
/// <param name="Status">The HTTP status of a rejection; zero otherwise.</param>
public sealed record ProfilePutResult(ProfilePutOutcome Outcome, long Revision, int Status)
{
	#region Static Instances
	/// <summary>
	/// The revision the phone sent is no longer the current one.
	/// </summary>
	public static readonly ProfilePutResult Stale = new(ProfilePutOutcome.Stale, 0, 412);
	#endregion

	#region Public Methods
	/// <summary>
	/// Builds the answer of a stored document.
	/// </summary>
	/// <param name="revision">The new revision.</param>
	/// <returns>The answer.</returns>
	public static ProfilePutResult Saved(long revision) => new(ProfilePutOutcome.Saved, revision, 0);

	/// <summary>
	/// Builds the answer of a refused document.
	/// </summary>
	/// <param name="status">The HTTP status.</param>
	/// <returns>The answer.</returns>
	public static ProfilePutResult Rejected(int status) => new(ProfilePutOutcome.Rejected, 0, status);
	#endregion
}
