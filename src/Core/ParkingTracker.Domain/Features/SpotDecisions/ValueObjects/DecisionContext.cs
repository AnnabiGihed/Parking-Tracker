using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Sectors.Services;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;

namespace ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Everything a decision knows besides the place and the live data: the card, how its municipality works, the
///              street lists of the card's sector and whether those lists are confirmed unchanged.
/// </summary>
/// <param name="Card">The user's card, or <see langword="null"/> when none is set up.</param>
/// <param name="Municipality">How the card of its municipality works; the City of Brussels when there is no card.</param>
/// <param name="Sector">The street lists of the card's sector; only for municipalities whose kind is <see cref="MunicipalityKind.Streets"/>.</param>
/// <param name="OfficialList">The result of the last check of the official documents, or <see langword="null"/> when none was made.</param>
public sealed record DecisionContext(ParkingCard? Card, Municipality Municipality, SectorModel? Sector, OfficialListStatus? OfficialList)
{
	#region Properties
	/// <summary>
	/// Gets the codes of the tolerance agreements on the card.
	/// </summary>
	public IReadOnlySet<string> Tolerances { get; } = new HashSet<string>(Card?.Tolerances ?? [], StringComparer.Ordinal);

	/// <summary>
	/// Gets the zone codes of the areas (mailles) on the card.
	/// </summary>
	public IReadOnlySet<string> Mailles { get; } = new HashSet<string>(Card?.Mailles ?? [], StringComparer.Ordinal);

	/// <summary>
	/// Gets the zone codes of the border zones on the card.
	/// </summary>
	public IReadOnlySet<string> Lims { get; } = new HashSet<string>(Card?.Lims ?? [], StringComparer.Ordinal);

	/// <summary>
	/// Gets the sentence that explains why the street lists cannot be used, or <see langword="null"/> when they can (or when
	/// the municipality has no street lists).
	/// </summary>
	public string? ListProblem => Sector is null ? null : OfficialList?.Problem;
	#endregion
}
