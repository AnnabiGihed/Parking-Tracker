namespace ParkingTracker.Application.Features.Shared.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The port through which the app reaches what the Capacitor version saved in the <c>localStorage</c> of its web view
///              (ADR-0008). Infrastructure reads the files of the old web view (LevelDB), the host can ask a hidden web view, and a
///              composite tries the second when the first cannot read the data; the migration only sees this port, so everything
///              that decides what is copied stays in code that tests run on any computer.
/// </summary>
public interface ILegacyStorageReader
{
	#region Methods
	/// <summary>
	/// Reads every <c>pt.*</c> value the old version saved, as the JSON text it wrote.
	/// </summary>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>
	/// A task with the values by key, empty when there is no old data (a new install), or a failure when the old data exists but
	/// could not be read; a failure is worth trying again at the next launch. An implementation never changes the old storage.
	/// </returns>
	Task<LegacyStorageReading> ReadAsync(CancellationToken cancellationToken);
	#endregion
}
