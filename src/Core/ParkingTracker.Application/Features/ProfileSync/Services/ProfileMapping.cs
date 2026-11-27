using ParkingTracker.Application.Features.ProfileSync.Abstractions;
using ParkingTracker.Contracts.Features.Profiles;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.SavedPlaces.Aggregates;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.Vehicles.ValueObjects;
using Pivot.Framework.Domain.Shared;
using PlaceList = ParkingTracker.Domain.Features.SavedPlaces.Aggregates.SavedPlaces;

namespace ParkingTracker.Application.Features.ProfileSync.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Maps the local data of the phone to the sections of the profile document and back (ADR-0032), without loss for every
///              field the document holds, including the moment of a clear. The fields that stay on the phone (the parked car, the timer, the card photos, the ids of the
///              places) are not part of either side. A value of a document that the phone's own rules refuse (an unknown fuel, a
///              place outside Brussels, more favourites than the phone keeps) comes back as a failure, never as a half-applied section.
/// </summary>
public static class ProfileMapping
{
	#region Constants
	/// <summary>
	/// The code of the failure for a section of a document that the phone cannot hold.
	/// </summary>
	public const string InvalidSectionCode = "ProfileSync.InvalidSection";
	#endregion

	#region Static Instances
	/// <summary>
	/// The moment written for a section that never changed on the phone; the merge reads it as "no moment".
	/// </summary>
	public static readonly DateTimeOffset NoMoment = DateTimeOffset.UnixEpoch;
	#endregion

	#region Public Methods
	/// <summary>
	/// Maps the home or the work place to a section.
	/// </summary>
	/// <param name="place">The place, or <see langword="null"/> when none is saved.</param>
	/// <param name="at">The moment the place last changed on the phone, or <see langword="null"/>.</param>
	/// <returns>The section, or <see langword="null"/> when there is no place.</returns>
	public static ProfilePlaceSection? ToPlaceSection(SavedPlace? place, DateTimeOffset? at)
	{
		return place is null ? null : new ProfilePlaceSection(place.Label, place.Point.Latitude, place.Point.Longitude, place.AddressText, at ?? NoMoment);
	}

	/// <summary>
	/// Maps the favourite places, in the order they were saved, to a section.
	/// </summary>
	/// <param name="list">The saved places, or <see langword="null"/> when none are stored.</param>
	/// <param name="at">The moment the list last changed on the phone, or <see langword="null"/>.</param>
	/// <returns>The section; its list is empty when there is no favourite.</returns>
	public static ProfileFavouritesSection ToFavouritesSection(PlaceList? list, DateTimeOffset? at)
	{
		var places = list is null
			? []
			: list.Places.Where(place => place.Kind == PlaceKind.Favourite)
				.Select(place => new ProfileFavouritePlace(place.Label, place.Point.Latitude, place.Point.Longitude, place.AddressText))
				.ToArray();
		return new ProfileFavouritesSection(at ?? NoMoment, places);
	}

	/// <summary>
	/// Maps the vehicle profile to a section.
	/// </summary>
	/// <param name="details">The vehicle, or <see langword="null"/> when none is saved.</param>
	/// <param name="at">The moment the vehicle last changed on the phone, or <see langword="null"/>.</param>
	/// <returns>The section, or <see langword="null"/> when there is no vehicle.</returns>
	public static ProfileVehicleSection? ToVehicleSection(VehicleDetails? details, DateTimeOffset? at)
	{
		if (details is null)
		{
			return null;
		}

		var connectors = details.Charging?.Connectors.Select(connector => connector.ToString()).ToArray() ?? [];
		return new ProfileVehicleSection(
			details.Make,
			details.Model,
			details.Year,
			details.Fuel.ToString(),
			details.EuroNorm.ToString(),
			connectors,
			details.Charging?.BatteryKwh,
			details.Charging?.RangeKm,
			details.Plate,
			at ?? NoMoment);
	}

	/// <summary>
	/// Maps the residents' card to a section.
	/// </summary>
	/// <param name="card">The card, or <see langword="null"/> when none is saved.</param>
	/// <param name="at">The moment the card last changed on the phone, or <see langword="null"/>.</param>
	/// <returns>The section, or <see langword="null"/> when there is no card.</returns>
	public static ProfileCardSection? ToCardSection(ParkingCard? card, DateTimeOffset? at)
	{
		return card is null
			? null
			: new ProfileCardSection(
				card.Municipality,
				card.Sector,
				card.Period.ValidFrom,
				card.Period.ValidTo,
				card.Tolerances.ToArray(),
				card.Mailles.ToArray(),
				card.Lims.ToArray(),
				card.Number,
				card.Plate,
				at ?? NoMoment);
	}

	/// <summary>
	/// Gives the moments of the sections that are absent from a document, so that a clear travels with the document.
	/// </summary>
	/// <param name="homeAbsent">Whether the document holds no home.</param>
	/// <param name="workAbsent">Whether the document holds no work place.</param>
	/// <param name="vehicleAbsent">Whether the document holds no vehicle.</param>
	/// <param name="cardAbsent">Whether the document holds no card.</param>
	/// <param name="times">The moment each section last changed.</param>
	/// <returns>The moments of the absent sections, or <see langword="null"/> when no absent section has one.</returns>
	public static ProfileClearedSections? ToClearedSections(bool homeAbsent, bool workAbsent, bool vehicleAbsent, bool cardAbsent, SectionTimes times)
	{
		ArgumentNullException.ThrowIfNull(times);
		var home = homeAbsent ? times.Home : null;
		var work = workAbsent ? times.Work : null;
		var vehicle = vehicleAbsent ? times.Vehicle : null;
		var card = cardAbsent ? times.Card : null;
		return home is null && work is null && vehicle is null && card is null ? null : new ProfileClearedSections(home, work, vehicle, card);
	}

	/// <summary>
	/// Maps a vehicle section to the values the vehicle profile takes.
	/// </summary>
	/// <param name="section">The section.</param>
	/// <returns>The values, or a failure when the fuel, the norm or a connector is not a name the phone knows.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="section"/> is <see langword="null"/>.</exception>
	public static Result<VehicleDetails> ToVehicleDetails(ProfileVehicleSection section)
	{
		ArgumentNullException.ThrowIfNull(section);
		if (!TryParseName<FuelType>(section.Fuel, out var fuel) || !TryParseName<EuroNorm>(section.EuroNorm, out var norm))
		{
			return Invalid<VehicleDetails>();
		}

		var connectors = new List<ChargingConnector>();
		foreach (var name in section.Connectors)
		{
			if (!TryParseName<ChargingConnector>(name, out var connector))
			{
				return Invalid<VehicleDetails>();
			}

			connectors.Add(connector);
		}

		var hasCharging = connectors.Count > 0 || section.BatteryKwh is not null || section.RangeKm is not null;
		var charging = hasCharging ? new ChargingDetails(connectors, section.BatteryKwh, section.RangeKm) : null;
		return new VehicleDetails(section.Make, section.Model, section.Year, fuel, norm, charging, section.Plate);
	}

	/// <summary>
	/// Maps a card section to the values the residents' card takes.
	/// </summary>
	/// <param name="section">The section.</param>
	/// <returns>The card values; the card itself checks them against its municipality when it is created.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="section"/> is <see langword="null"/>.</exception>
	public static ParkingCard ToParkingCard(ProfileCardSection section)
	{
		ArgumentNullException.ThrowIfNull(section);
		return new ParkingCard(
			section.Municipality,
			section.Sector,
			new CardPeriod(section.ValidFrom, section.ValidTo),
			section.Tolerances.ToArray(),
			section.Mailles.ToArray(),
			section.Lims.ToArray(),
			section.Number,
			section.Plate);
	}

	/// <summary>
	/// Builds the failure for a section of a document that the phone cannot hold. It never repeats a value of the document.
	/// </summary>
	/// <typeparam name="T">The type of the result.</typeparam>
	/// <returns>The failure.</returns>
	public static Result<T> Invalid<T>() => Result.Failure<T>(new Error(InvalidSectionCode, "A section of the saved profile cannot be used on this phone."));
	#endregion

	#region Private Helpers
	/// <summary>
	/// Reads the name of a member of an enumeration, and nothing else (a number is refused, like the server does).
	/// </summary>
	/// <typeparam name="TEnum">The enumeration.</typeparam>
	/// <param name="name">The text.</param>
	/// <param name="value">The member.</param>
	/// <returns><see langword="true"/> when the text is exactly the name of a member.</returns>
	private static bool TryParseName<TEnum>(string? name, out TEnum value)
		where TEnum : struct, Enum
	{
		value = default;
		return name is not null && Array.Exists(Enum.GetNames<TEnum>(), candidate => string.Equals(candidate, name, StringComparison.Ordinal)) && Enum.TryParse(name, ignoreCase: false, out value);
	}
	#endregion
}
