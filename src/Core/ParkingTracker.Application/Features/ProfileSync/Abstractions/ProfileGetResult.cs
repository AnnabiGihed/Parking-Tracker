using ParkingTracker.Contracts.Features.Profiles;

namespace ParkingTracker.Application.Features.ProfileSync.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The answer of a read of the profile (ADR-0032): the copy the phone holds is current (304), the server has a document
///              (200 with its revision), or the server has none (404).
/// </summary>
/// <param name="Outcome">Which answer it is.</param>
/// <param name="Revision">The revision of the document; zero unless <see cref="ProfileGetOutcome.Found"/>.</param>
/// <param name="Document">The document; <see langword="null"/> unless <see cref="ProfileGetOutcome.Found"/>.</param>
public sealed record ProfileGetResult(ProfileGetOutcome Outcome, long Revision, ProfileDocument? Document)
{
	#region Static Instances
	/// <summary>
	/// The copy of the phone is current.
	/// </summary>
	public static readonly ProfileGetResult NotModified = new(ProfileGetOutcome.NotModified, 0, null);

	/// <summary>
	/// The server has no document.
	/// </summary>
	public static readonly ProfileGetResult NotFound = new(ProfileGetOutcome.NotFound, 0, null);
	#endregion

	#region Public Methods
	/// <summary>
	/// Builds the answer of a document the server holds.
	/// </summary>
	/// <param name="revision">The revision of the document.</param>
	/// <param name="document">The document.</param>
	/// <returns>The answer.</returns>
	public static ProfileGetResult Found(long revision, ProfileDocument document) => new(ProfileGetOutcome.Found, revision, document);
	#endregion
}
