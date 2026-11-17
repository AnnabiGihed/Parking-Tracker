namespace ParkingTracker.ViewModels.Features.Card;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The state of the list of areas in the card form.
/// </summary>
public enum MailleLoadState
{
	/// <summary>
	/// The municipality has no list of areas to choose from.
	/// </summary>
	NotNeeded,

	/// <summary>
	/// The list is being loaded from parking.brussels.
	/// </summary>
	Loading,

	/// <summary>
	/// The list is loaded.
	/// </summary>
	Loaded,

	/// <summary>
	/// The list could not be loaded.
	/// </summary>
	Failed,
}
