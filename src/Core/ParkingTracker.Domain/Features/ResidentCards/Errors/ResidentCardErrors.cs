using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Features.ResidentCards.Errors;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The errors of the residents' card; each code is stable and dotted, each message tells the user what to fix.
/// </summary>
public static class ResidentCardErrors
{
	#region Static Instances
	/// <summary>
	/// The card names no municipality.
	/// </summary>
	public static readonly Error MunicipalityRequired = new("ResidentCard.Municipality.Required", "The card needs a municipality.");

	/// <summary>
	/// The municipality of the reference data is not the one the card names.
	/// </summary>
	public static readonly Error MunicipalityMismatch = new("ResidentCard.Municipality.Mismatch", "The card names another municipality than the one it is checked against.");

	/// <summary>
	/// The card is valid from a day after the day it is valid until.
	/// </summary>
	public static readonly Error PeriodReversed = new("ResidentCard.Period.Reversed", "The card cannot be valid from a day after the day it is valid until.");

	/// <summary>
	/// A sector is set for a municipality that has no street-list sectors.
	/// </summary>
	public static readonly Error SectorNotAllowed = new("ResidentCard.Sector.NotAllowed", "Only a card of a municipality with street lists has a sector.");

	/// <summary>
	/// Areas are set for a municipality that does not issue cards per area.
	/// </summary>
	public static readonly Error MaillesNotAllowed = new("ResidentCard.Mailles.NotAllowed", "Only a card of a municipality that issues cards per area has areas.");
	#endregion

	#region Factory Methods
	/// <summary>
	/// Builds the error for an area whose code does not start with a prefix of the municipality.
	/// </summary>
	/// <param name="zoneCode">The zone code of the area.</param>
	/// <returns>The error.</returns>
	public static Error MailleOutsideMunicipality(string zoneCode)
	{
		return new Error("ResidentCard.Mailles.OutsideMunicipality", $"The area '{zoneCode}' does not belong to the municipality of the card.");
	}

	/// <summary>
	/// Builds the error for a border zone that the municipality does not list.
	/// </summary>
	/// <param name="zoneCode">The zone code of the border zone.</param>
	/// <returns>The error.</returns>
	public static Error LimNotListed(string zoneCode)
	{
		return new Error("ResidentCard.Lims.NotListed", $"The border zone '{zoneCode}' is not in the list of the municipality of the card.");
	}
	#endregion
}
