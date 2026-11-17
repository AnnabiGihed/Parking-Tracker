namespace ParkingTracker.ViewModels.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Base of the view models: one <see cref="Changed"/> event that a component subscribes to in order to render again.
///              It carries no data, because a component reads the properties it shows. The event can be raised from a timer
///              thread, so a component renders through its own synchronisation context.
/// </summary>
public abstract class ViewModelBase
{
	#region Events
	/// <summary>
	/// Occurs when something a component shows has changed.
	/// </summary>
	public event EventHandler? Changed;
	#endregion

	#region Protected Methods
	/// <summary>
	/// Raises <see cref="Changed"/>.
	/// </summary>
	protected void NotifyChanged()
	{
		Changed?.Invoke(this, EventArgs.Empty);
	}
	#endregion
}
