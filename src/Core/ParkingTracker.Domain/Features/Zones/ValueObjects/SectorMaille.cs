using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.Domain.Features.Zones.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A sector polygon ("maille") published by parking.brussels, with its zone code.
/// </summary>
/// <param name="ZoneCode">The zone code, for example <c>IXLR_Ouest</c>; empty when missing.</param>
/// <param name="NameFrench">The name in French, or <see langword="null"/>.</param>
/// <param name="Shape">The shape in Web Mercator.</param>
public sealed record SectorMaille(string ZoneCode, string? NameFrench, SurfaceShape Shape)
{
	#region Properties
	/// <summary>
	/// Gets the name shown to the user: the French name, or the zone code when there is none.
	/// </summary>
	public string DisplayName => string.IsNullOrEmpty(NameFrench) ? ZoneCode : NameFrench;
	#endregion
}
