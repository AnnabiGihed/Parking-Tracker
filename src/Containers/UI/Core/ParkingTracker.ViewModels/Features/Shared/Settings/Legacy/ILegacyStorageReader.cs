namespace ParkingTracker.ViewModels.Features.Shared.Settings.Legacy;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The port through which <see cref="LegacyDataMigration"/> reaches what the Capacitor version of the app saved in the
///              <c>localStorage</c> of its web view (ADR-0004). The host implements it over the files of the old web view, so that
///              the migration and the parser stay in code that tests run on any computer.
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
	/// could not be read; a failure is worth trying again at the next launch.
	/// </returns>
	Task<LegacyStorageReading> ReadAsync(CancellationToken cancellationToken);
	#endregion
}
