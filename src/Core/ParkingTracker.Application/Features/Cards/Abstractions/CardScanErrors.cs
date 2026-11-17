using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.Cards.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The failure codes of scanning a card photo, so that the screen can tell a user who walked away from the camera
///              (nothing to report) from a camera that is refused or a recognition that broke (a message to show).
/// </summary>
public static class CardScanErrors
{
	#region Constants
	/// <summary>
	/// The user left the camera or the picker without choosing a photo.
	/// </summary>
	public const string CancelledCode = "CardScan.Cancelled";

	/// <summary>
	/// Neither the camera nor the photo picker could be used because the user refused the permission or the device has none.
	/// </summary>
	public const string UnavailableCode = "CardScan.Unavailable";

	/// <summary>
	/// The photo could not be read or recognised.
	/// </summary>
	public const string FailedCode = "CardScan.Failed";
	#endregion

	#region Public Methods
	/// <summary>
	/// Builds the failure for a user who left without a photo.
	/// </summary>
	/// <returns>The error with the code <see cref="CancelledCode"/>.</returns>
	public static Error Cancelled() => new(CancelledCode, "No photo was taken.");

	/// <summary>
	/// Builds the failure for a camera and picker that cannot be used.
	/// </summary>
	/// <param name="reason">Why, in words the user can read.</param>
	/// <returns>The error with the code <see cref="UnavailableCode"/>.</returns>
	public static Error Unavailable(string reason) => new(UnavailableCode, reason);

	/// <summary>
	/// Builds the failure for a photo that could not be recognised.
	/// </summary>
	/// <param name="reason">What went wrong, in words the user can read.</param>
	/// <returns>The error with the code <see cref="FailedCode"/>.</returns>
	public static Error Failed(string reason) => new(FailedCode, reason);
	#endregion
}
