using ParkingTracker.Application.Features.Shared.Abstractions;

namespace ParkingTracker.Application.Features.ProfileSync.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port through which the stores record that the user changed a section (ADR-0032). <see cref="Stage"/> puts the moment of
///              the change and the changed flag into the same scope as the data change, so that both are saved or neither is; after the
///              commit the store calls <see cref="NotifyCommitted"/>, which tells the sync trigger that something is waiting to be
///              uploaded. Applying data that came from the server never goes through this port, so it never marks the phone as changed.
/// </summary>
public interface IProfileChangeTracker
{
	#region Events
	/// <summary>
	/// Raised after a user change was committed.
	/// </summary>
	event EventHandler? Changed;
	#endregion

	#region Methods
	/// <summary>
	/// Stages the moment of the change of a section and the changed flag in the scope of the data change.
	/// </summary>
	/// <param name="scope">The scope that also holds the data change.</param>
	/// <param name="section">The section the user changed or cleared.</param>
	void Stage(IDeviceChangeScope scope, SyncSection section);

	/// <summary>
	/// Tells that the scope given to <see cref="Stage"/> was committed.
	/// </summary>
	void NotifyCommitted();
	#endregion
}
