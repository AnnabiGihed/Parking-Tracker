namespace ParkingTracker.Backend.Application.Features.Accounts.Commands.SaveProfile;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What a write of the profile is based on (ADR-0032): the account has no profile yet, or it has the profile at one named
///              revision. A write that finds anything else is refused, so a phone never overwrites what it has not seen.
/// </summary>
/// <param name="ExpectedRevision">The revision the write replaces, or <see langword="null"/> to create the first profile.</param>
public sealed record ProfileWriteCondition(long? ExpectedRevision)
{
	#region Static Instances
	/// <summary>
	/// The condition of a write that creates the first profile of the account.
	/// </summary>
	public static readonly ProfileWriteCondition CreateOnly = new((long?)null);
	#endregion

	#region Properties
	/// <summary>
	/// Gets a value indicating whether the write only creates, that is, it names no revision.
	/// </summary>
	public bool IsCreateOnly => ExpectedRevision is null;
	#endregion

	#region Factory Methods
	/// <summary>
	/// Creates the condition of a write that replaces one revision.
	/// </summary>
	/// <param name="revision">The revision the write replaces.</param>
	/// <returns>The condition.</returns>
	public static ProfileWriteCondition ForRevision(long revision)
	{
		return new ProfileWriteCondition(revision);
	}
	#endregion
}
