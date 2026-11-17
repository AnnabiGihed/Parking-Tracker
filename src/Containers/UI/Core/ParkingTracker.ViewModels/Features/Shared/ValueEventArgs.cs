namespace ParkingTracker.ViewModels.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Event data that carries one value, so that a view model event can pass a request or a flag with the standard
///              <see cref="EventHandler{TEventArgs}"/> signature.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
public sealed class ValueEventArgs<T> : EventArgs
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ValueEventArgs{T}"/> class.
	/// </summary>
	/// <param name="value">The value to carry.</param>
	public ValueEventArgs(T value)
	{
		Value = value;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the value the event carries.
	/// </summary>
	public T Value { get; }
	#endregion
}
