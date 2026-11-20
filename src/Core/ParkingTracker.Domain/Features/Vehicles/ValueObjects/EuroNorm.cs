namespace ParkingTracker.Domain.Features.Vehicles.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The European emission standard on the registration certificate, which the Brussels low-emission zone checks. The
///              device database stores the number of each value, so a value is never renumbered and a new one takes the next free number.
/// </summary>
public enum EuroNorm
{
	/// <summary>
	/// The user did not enter the standard.
	/// </summary>
	Unknown = 0,

	/// <summary>
	/// Euro 1.
	/// </summary>
	Euro1 = 1,

	/// <summary>
	/// Euro 2.
	/// </summary>
	Euro2 = 2,

	/// <summary>
	/// Euro 3.
	/// </summary>
	Euro3 = 3,

	/// <summary>
	/// Euro 4.
	/// </summary>
	Euro4 = 4,

	/// <summary>
	/// Euro 5.
	/// </summary>
	Euro5 = 5,

	/// <summary>
	/// Euro 6, including its sub-levels.
	/// </summary>
	Euro6 = 6,
}
