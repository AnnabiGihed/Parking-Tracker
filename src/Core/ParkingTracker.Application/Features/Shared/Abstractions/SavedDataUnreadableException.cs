namespace ParkingTracker.Application.Features.Shared.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The explicit answer of the settings store when the saved card or parked car exists but cannot be read (a key that
///              was lost, a value that was changed, a row that breaks a rule). It is not "nothing saved": a caller that reads it as no
///              card would let the spot check answer FREE. The spot check answers "can't verify" with <see cref="CardMessage"/>, and the
///              card line asks the user to enter the card again. A <see cref="IsTemporary"/> failure (the key cannot be read right now)
///              may succeed later and is never remembered; any other failure is permanent until the user saves the value again.
/// </summary>
public sealed class SavedDataUnreadableException : InvalidOperationException
{
	#region Constants
	/// <summary>
	/// What the user is told when the saved card cannot be read.
	/// </summary>
	public const string CardMessage = "Your saved card could not be read. Please enter it again.";

	/// <summary>
	/// What the user is told when the saved parked car cannot be read.
	/// </summary>
	public const string CarMessage = "Your saved parked car could not be read.";

	/// <summary>
	/// What the user is told when the saved Home and Work places cannot be read.
	/// </summary>
	public const string PlacesMessage = "Your saved places could not be read. Please set them again.";

	/// <summary>
	/// What the user is told when the saved vehicle cannot be read.
	/// </summary>
	public const string VehicleMessage = "Your saved vehicle could not be read. Please enter it again.";
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SavedDataUnreadableException"/> class.
	/// </summary>
	/// <param name="message">What the user is told.</param>
	/// <param name="errorCode">The code of the error that stopped the read.</param>
	/// <param name="isTemporary">Whether a later read may succeed.</param>
	public SavedDataUnreadableException(string message, string errorCode, bool isTemporary)
		: base(message)
	{
		ErrorCode = errorCode;
		IsTemporary = isTemporary;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the code of the error that stopped the read.
	/// </summary>
	public string ErrorCode { get; }

	/// <summary>
	/// Gets a value indicating whether a later read may succeed (the key cannot be read right now).
	/// </summary>
	public bool IsTemporary { get; }
	#endregion
}
