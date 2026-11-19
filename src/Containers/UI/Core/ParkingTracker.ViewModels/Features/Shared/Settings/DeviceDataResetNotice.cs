namespace ParkingTracker.ViewModels.Features.Shared.Settings;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tells the stores of the device database that the stale encrypted rows were removed after the key was lost. The removal
///              deletes the card, the parked car and the places together, because a new key is created only when no encrypted row
///              exists; a store that remembers what it read (the card, or that a value is unreadable) must forget it, or it would go on
///              reporting rows that no longer exist. One instance is shared by the stores.
/// </summary>
public sealed class DeviceDataResetNotice
{
	#region Events
	/// <summary>
	/// Occurs after the stale encrypted rows were removed and the removal was committed.
	/// </summary>
	public event EventHandler? StaleRowsRemoved;
	#endregion

	#region Public Methods
	/// <summary>
	/// Raises <see cref="StaleRowsRemoved"/>.
	/// </summary>
	public void Raise()
	{
		StaleRowsRemoved?.Invoke(this, EventArgs.Empty);
	}
	#endregion
}
