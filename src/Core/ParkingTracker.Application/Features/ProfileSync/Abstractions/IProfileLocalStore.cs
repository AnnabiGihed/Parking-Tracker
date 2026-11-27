using ParkingTracker.Contracts.Features.Profiles;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.ProfileSync.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port through which the sync reads the local profile as a document and writes a merged or downloaded document back
///              (ADR-0032). Writing is conditional: it happens only when no section changed since the snapshot the caller works from,
///              inside the one scope that also holds the write gate, so that an edit made during a sync is never overwritten and
///              never loses its changed flag. Nothing here stamps a section: data that came from the server is not a user change.
/// </summary>
public interface IProfileLocalStore
{
	#region Methods
	/// <summary>
	/// Reads the local sections and the sync state.
	/// </summary>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task with the snapshot, or the failure of the read (for example a value that cannot be decrypted).</returns>
	Task<Result<ProfileSnapshot>> ReadAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Replaces the local sections that differ from a document and stores the revision it came from, in one transaction, unless a
	/// section changed since the snapshot.
	/// </summary>
	/// <param name="snapshot">The snapshot the document was derived from.</param>
	/// <param name="document">The sections to hold locally.</param>
	/// <param name="times">The moment of each section after the write.</param>
	/// <param name="revision">The revision of the server copy the document is based on, or <see langword="null"/> when the server holds none.</param>
	/// <param name="needsUpload">Whether the document still has to be uploaded, which keeps the changed flag set.</param>
	/// <param name="currentOwnerHash">Gives the hash of the account the sync is verified for right now, or <see langword="null"/> when it is not; it is called under the write gate, so a sign-out that landed before the write is seen. Nothing is written unless it equals the bound account.</param>
	/// <param name="cancellationToken">Cancels the write before it starts.</param>
	/// <returns>A task with <see langword="true"/> when it was written, <see langword="false"/> when a section changed, the phone was bound to another account or its profile was removed since the snapshot (nothing was written), or the failure.</returns>
	Task<Result<bool>> ApplyAsync(ProfileSnapshot snapshot, ProfileDocument document, SectionTimes times, long? revision, bool needsUpload, Func<string?> currentOwnerHash, CancellationToken cancellationToken = default);

	/// <summary>
	/// Records that the server holds a revision with everything the phone had: stores the revision and clears the changed flag, unless
	/// a section changed since the moments the upload was built from.
	/// </summary>
	/// <param name="revision">The revision the server answered, or <see langword="null"/> when the server holds no document and there was nothing to upload.</param>
	/// <param name="uploaded">The moments of the sections that were uploaded.</param>
	/// <param name="currentOwnerHash">Gives the hash of the account the sync is verified for right now, or <see langword="null"/> when it is not; it is called under the write gate. Nothing is written unless it equals the bound account.</param>
	/// <param name="cancellationToken">Cancels the write before it starts.</param>
	/// <returns>A task with <see langword="true"/> when the flag was cleared, <see langword="false"/> when a section changed meanwhile (the revision is stored and the flag stays set) or the phone was bound to another account or emptied meanwhile (nothing is written), or the failure.</returns>
	Task<Result<bool>> MarkSavedAsync(long? revision, SectionTimes uploaded, Func<string?> currentOwnerHash, CancellationToken cancellationToken = default);
	#endregion
}
