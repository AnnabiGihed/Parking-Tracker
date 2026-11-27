namespace ParkingTracker.Contracts.Features.Profiles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The profile document of an account, version 1 (ADR-0032): the places, the vehicle and the residents' card that follow the
///              account from phone to phone. Each section carries the time the phone changed it, so two phones merge section by
///              section and the newest wins. A section that was never set is <see langword="null"/>; one the phone cleared is
///              <see langword="null"/> too, with the time of the clear in <see cref="ProfileClearedSections"/>. The server keeps the document
///              encrypted and reads nothing of it but the schema version.
/// </summary>
/// <param name="SchemaVersion">The version of the document layout; the server accepts <see cref="ProfileDocument.SupportedSchemaVersion"/> only.</param>
/// <param name="Home">The home, or <see langword="null"/> when it was never set.</param>
/// <param name="Work">The work place, or <see langword="null"/> when it was never set.</param>
/// <param name="Favourites">The favourite places, as one section.</param>
/// <param name="Vehicle">The vehicle profile, or <see langword="null"/> when it was never set.</param>
/// <param name="Card">The residents' card, or <see langword="null"/> when it was never set.</param>
/// <param name="Cleared">When the phone cleared a section that is absent here, or <see langword="null"/> (also for a document written before this field existed).</param>
public sealed record ProfileDocument(
	int SchemaVersion,
	ProfilePlaceSection? Home,
	ProfilePlaceSection? Work,
	ProfileFavouritesSection Favourites,
	ProfileVehicleSection? Vehicle,
	ProfileCardSection? Card,
	ProfileClearedSections? Cleared = null)
{
	#region Constants
	/// <summary>
	/// The only schema version of the document the server accepts.
	/// </summary>
	public const int SupportedSchemaVersion = 1;
	#endregion
}
