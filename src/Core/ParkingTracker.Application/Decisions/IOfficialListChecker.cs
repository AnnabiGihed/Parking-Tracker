using ParkingTracker.Domain.Addressing;
using ParkingTracker.Domain.Decisions;

namespace ParkingTracker.Application.Decisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Confirms that the City of Brussels' official sector documents are still the ones the street lists were verified
///              against, and remembers the outcome in memory so that the documents are not asked for at every decision.
/// </summary>
public interface IOfficialListChecker
{
	#region Methods
	/// <summary>
	/// Gives the outcome of the last check of a sector's documents, checking again when there is none, it belongs to another
	/// sector or it is too old (6 hours after an unchanged result, 1 minute after any other).
	/// </summary>
	/// <param name="sector">The sector whose documents are checked.</param>
	/// <param name="now">The current moment.</param>
	/// <param name="cancellationToken">Cancels the check.</param>
	/// <returns>The status; a check that could not be made is a status, not a failure.</returns>
	Task<OfficialListStatus> EnsureFreshAsync(SectorModel sector, DateTimeOffset now, CancellationToken cancellationToken);
	#endregion
}
