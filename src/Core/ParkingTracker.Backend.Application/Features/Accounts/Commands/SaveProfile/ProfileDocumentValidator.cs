using System.Linq.Expressions;
using FluentValidation;
using ParkingTracker.Contracts.Features.Profiles;
using ParkingTracker.Domain.Features.Vehicles.Aggregates;
using ParkingTracker.Domain.Features.Vehicles.ValueObjects;
using SavedPlaceEntity = ParkingTracker.Domain.Features.SavedPlaces.Aggregates.SavedPlace;
using SavedPlacesList = ParkingTracker.Domain.Features.SavedPlaces.Aggregates.SavedPlaces;

namespace ParkingTracker.Backend.Application.Features.Accounts.Commands.SaveProfile;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Checks the plain profile document before it is encrypted (ADR-0032): the supported schema version, the lengths of the
///              texts, the coordinates inside the Brussels box (edges included), at most 50 favourites, the vehicle and the card within
///              the limits of the domain, the names of the enumerations, and no time more than a day ahead of the server, and a moment of a clear only for a section that is absent. The limits
///              of the domain are used where the domain has them. The messages never repeat a value of the document.
/// </summary>
public sealed class ProfileDocumentValidator : AbstractValidator<ProfileDocument>
{
	#region Constants
	/// <summary>
	/// The most favourite places a document holds; the phone keeps fewer, the server is tolerant.
	/// </summary>
	public const int MaximumFavourites = 50;

	/// <summary>
	/// The longest address text.
	/// </summary>
	public const int MaximumAddressTextLength = 200;

	/// <summary>
	/// The longest make code.
	/// </summary>
	public const int MaximumMakeLength = 60;

	/// <summary>
	/// The longest number plate.
	/// </summary>
	public const int MaximumPlateLength = 16;

	/// <summary>
	/// The longest municipality name.
	/// </summary>
	public const int MaximumMunicipalityLength = 64;

	/// <summary>
	/// The longest sector, card number and list item.
	/// </summary>
	public const int MaximumShortTextLength = 32;

	/// <summary>
	/// The most items a list of the card or the connectors of the vehicle holds.
	/// </summary>
	public const int MaximumListItems = 20;

	/// <summary>
	/// The days a time of the phone may be ahead of the server, for clocks that differ a little.
	/// </summary>
	public const int MaximumDaysAhead = 1;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ProfileDocumentValidator"/> class.
	/// </summary>
	/// <param name="clock">The clock, for the model year and the times of the sections.</param>
	/// <exception cref="ArgumentNullException"><paramref name="clock"/> is <see langword="null"/>.</exception>
	public ProfileDocumentValidator(TimeProvider clock)
	{
		ArgumentNullException.ThrowIfNull(clock);
		_ = RuleFor(document => document.SchemaVersion).Equal(ProfileDocument.SupportedSchemaVersion).WithMessage("The schema version is not supported.");
		_ = RuleFor(document => document.Home!).SetValidator(new PlaceSectionValidator(clock)).When(document => document.Home is not null);
		_ = RuleFor(document => document.Work!).SetValidator(new PlaceSectionValidator(clock)).When(document => document.Work is not null);
		_ = RuleFor(document => document.Favourites).NotNull();
		_ = RuleFor(document => document.Favourites).SetValidator(new FavouritesSectionValidator(clock)).When(document => document.Favourites is not null);
		_ = RuleFor(document => document.Vehicle!).SetValidator(new VehicleSectionValidator(clock)).When(document => document.Vehicle is not null);
		_ = RuleFor(document => document.Card!).SetValidator(new CardSectionValidator(clock)).When(document => document.Card is not null);
		AddClearedRules(clock);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Adds the rules of the optional moments at which sections were cleared: a moment only for a section that is absent from the same
	/// document, and not more than a day ahead of the server, like the moment of a section.
	/// </summary>
	/// <param name="clock">The clock.</param>
	private void AddClearedRules(TimeProvider clock)
	{
		_ = RuleFor(document => document.Cleared)
			.Must((document, cleared) => cleared is null
				|| ((cleared.Home is null || document.Home is null)
					&& (cleared.Work is null || document.Work is null)
					&& (cleared.Vehicle is null || document.Vehicle is null)
					&& (cleared.Card is null || document.Card is null)))
			.WithMessage("A section that is present cannot also be cleared.");
		_ = RuleFor(document => document.Cleared)
			.Must(cleared => cleared is null || new[] { cleared.Home, cleared.Work, cleared.Vehicle, cleared.Card }.All(at => at is null || at.Value <= clock.GetUtcNow().AddDays(MaximumDaysAhead)))
			.WithMessage("The time is more than a day in the future.");
	}

	/// <summary>
	/// Adds the rule of a time set by the phone: not more than a day ahead of the server.
	/// </summary>
	/// <typeparam name="T">The type that holds the time.</typeparam>
	/// <param name="rule">The rule builder of the time.</param>
	/// <param name="clock">The clock.</param>
	/// <returns>The rule builder.</returns>
	private static IRuleBuilderOptions<T, DateTimeOffset> NotFarAhead<T>(IRuleBuilder<T, DateTimeOffset> rule, TimeProvider clock)
	{
		return rule.Must(updatedAt => updatedAt <= clock.GetUtcNow().AddDays(MaximumDaysAhead)).WithMessage("The time is more than a day in the future.");
	}

	/// <summary>
	/// Adds the rule of a text that may be missing: at most a number of characters.
	/// </summary>
	/// <typeparam name="T">The type that holds the text.</typeparam>
	/// <param name="rule">The rule builder of the text.</param>
	/// <param name="maximum">The most characters.</param>
	/// <returns>The rule builder.</returns>
	private static IRuleBuilderOptions<T, string?> AtMost<T>(IRuleBuilder<T, string?> rule, int maximum)
	{
		return rule.MaximumLength(maximum).WithMessage($"The text has at most {maximum} characters.");
	}

	/// <summary>
	/// Adds the rule of a latitude: inside the Brussels box, edges included.
	/// </summary>
	/// <typeparam name="T">The type that holds the latitude.</typeparam>
	/// <param name="rule">The rule builder of the latitude.</param>
	/// <returns>The rule builder.</returns>
	private static IRuleBuilderOptions<T, double> InsideLatitudes<T>(IRuleBuilder<T, double> rule)
	{
		var box = SavedPlacesList.BrusselsBox;
		return rule.InclusiveBetween(box.South, box.North).WithMessage("The latitude is outside the Brussels region.");
	}

	/// <summary>
	/// Adds the rule of a longitude: inside the Brussels box, edges included.
	/// </summary>
	/// <typeparam name="T">The type that holds the longitude.</typeparam>
	/// <param name="rule">The rule builder of the longitude.</param>
	/// <returns>The rule builder.</returns>
	private static IRuleBuilderOptions<T, double> InsideLongitudes<T>(IRuleBuilder<T, double> rule)
	{
		var box = SavedPlacesList.BrusselsBox;
		return rule.InclusiveBetween(box.West, box.East).WithMessage("The longitude is outside the Brussels region.");
	}

	/// <summary>
	/// Tells whether a text is the name of a member of an enumeration, not a number.
	/// </summary>
	/// <typeparam name="TEnum">The enumeration.</typeparam>
	/// <param name="value">The text.</param>
	/// <returns><see langword="true"/> when the text is exactly the name of a member.</returns>
	private static bool IsName<TEnum>(string? value)
		where TEnum : struct, Enum
	{
		return value is not null && Array.Exists(Enum.GetNames<TEnum>(), name => string.Equals(name, value, StringComparison.Ordinal));
	}
	#endregion

	#region Nested Types
	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : Checks the home or the work place.
	/// </summary>
	private sealed class PlaceSectionValidator : AbstractValidator<ProfilePlaceSection>
	{
		#region Constructors
		/// <summary>
		/// Initializes a new instance of the <see cref="PlaceSectionValidator"/> class.
		/// </summary>
		/// <param name="clock">The clock.</param>
		public PlaceSectionValidator(TimeProvider clock)
		{
			_ = AtMost(RuleFor(place => place.Label), SavedPlaceEntity.MaximumLabelLength);
			_ = InsideLatitudes(RuleFor(place => place.Latitude));
			_ = InsideLongitudes(RuleFor(place => place.Longitude));
			_ = AtMost(RuleFor(place => place.AddressText), MaximumAddressTextLength);
			_ = NotFarAhead(RuleFor(place => place.UpdatedAt), clock);
		}
		#endregion
	}

	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : Checks one favourite place.
	/// </summary>
	private sealed class FavouritePlaceValidator : AbstractValidator<ProfileFavouritePlace>
	{
		#region Constructors
		/// <summary>
		/// Initializes a new instance of the <see cref="FavouritePlaceValidator"/> class.
		/// </summary>
		public FavouritePlaceValidator()
		{
			_ = AtMost(RuleFor(place => place.Label), SavedPlaceEntity.MaximumLabelLength);
			_ = InsideLatitudes(RuleFor(place => place.Latitude));
			_ = InsideLongitudes(RuleFor(place => place.Longitude));
			_ = AtMost(RuleFor(place => place.AddressText), MaximumAddressTextLength);
		}
		#endregion
	}

	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : Checks the favourites section: the number of places and each place.
	/// </summary>
	private sealed class FavouritesSectionValidator : AbstractValidator<ProfileFavouritesSection>
	{
		#region Constructors
		/// <summary>
		/// Initializes a new instance of the <see cref="FavouritesSectionValidator"/> class.
		/// </summary>
		/// <param name="clock">The clock.</param>
		public FavouritesSectionValidator(TimeProvider clock)
		{
			_ = NotFarAhead(RuleFor(section => section.UpdatedAt), clock);
			_ = RuleFor(section => section.Places).NotNull().Must(places => places is null || places.Count <= MaximumFavourites).WithMessage($"There are at most {MaximumFavourites} favourites.");
			_ = RuleForEach(section => section.Places).NotNull().SetValidator(new FavouritePlaceValidator());
		}
		#endregion
	}

	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : Checks the vehicle section against the limits of the vehicle of the domain.
	/// </summary>
	private sealed class VehicleSectionValidator : AbstractValidator<ProfileVehicleSection>
	{
		#region Constructors
		/// <summary>
		/// Initializes a new instance of the <see cref="VehicleSectionValidator"/> class.
		/// </summary>
		/// <param name="clock">The clock.</param>
		public VehicleSectionValidator(TimeProvider clock)
		{
			_ = RuleFor(vehicle => vehicle.Make).NotEmpty().MaximumLength(MaximumMakeLength).WithMessage($"The make has 1 to {MaximumMakeLength} characters.");
			_ = AtMost(RuleFor(vehicle => vehicle.Model), Vehicle.ModelMaximumLength);
			_ = RuleFor(vehicle => vehicle.Year)
				.Must(year => year >= Vehicle.EarliestYear && year <= clock.GetUtcNow().Year + 1)
				.WithMessage("The year is outside the supported range.");
			_ = RuleFor(vehicle => vehicle.Fuel).Must(IsName<FuelType>).WithMessage("The fuel is not a known fuel type.");
			_ = RuleFor(vehicle => vehicle.EuroNorm).Must(IsName<EuroNorm>).WithMessage("The emission norm is not a known norm.");
			_ = RuleFor(vehicle => vehicle.Connectors).NotNull().Must(connectors => connectors is null || connectors.Count <= MaximumListItems).WithMessage($"There are at most {MaximumListItems} connectors.");
			_ = RuleForEach(vehicle => vehicle.Connectors).Must(IsName<ChargingConnector>).WithMessage("The connector is not a known connector.");
			_ = RuleFor(vehicle => vehicle.BatteryKwh).InclusiveBetween(0d, Vehicle.MaximumBatteryKwh).WithMessage("The battery is outside the supported range.");
			_ = RuleFor(vehicle => vehicle.RangeKm).InclusiveBetween(0, Vehicle.MaximumRangeKm).WithMessage("The range is outside the supported range.");
			_ = AtMost(RuleFor(vehicle => vehicle.Plate), MaximumPlateLength);
			_ = NotFarAhead(RuleFor(vehicle => vehicle.UpdatedAt), clock);
		}
		#endregion
	}

	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : Checks the card section: the lengths, the lists and the order of the two dates.
	/// </summary>
	private sealed class CardSectionValidator : AbstractValidator<ProfileCardSection>
	{
		#region Constructors
		/// <summary>
		/// Initializes a new instance of the <see cref="CardSectionValidator"/> class.
		/// </summary>
		/// <param name="clock">The clock.</param>
		public CardSectionValidator(TimeProvider clock)
		{
			_ = RuleFor(card => card.Municipality).NotEmpty().MaximumLength(MaximumMunicipalityLength).WithMessage($"The municipality has 1 to {MaximumMunicipalityLength} characters.");
			_ = AtMost(RuleFor(card => card.Sector), MaximumShortTextLength);
			_ = AtMost(RuleFor(card => card.Number), MaximumShortTextLength);
			_ = AtMost(RuleFor(card => card.Plate), MaximumPlateLength);
			_ = RuleFor(card => card.ValidTo)
				.Must((card, validTo) => card.ValidFrom is null || validTo is null || card.ValidFrom <= validTo)
				.WithMessage("The card is valid to a day before it is valid from.");
			AddListRules(card => card.Tolerances, nameof(ProfileCardSection.Tolerances));
			AddListRules(card => card.Mailles, nameof(ProfileCardSection.Mailles));
			AddListRules(card => card.Lims, nameof(ProfileCardSection.Lims));
			_ = NotFarAhead(RuleFor(card => card.UpdatedAt), clock);
		}
		#endregion

		#region Private Helpers
		/// <summary>
		/// Adds the rules of a list of short texts: present, at most 20 items, each at most 32 characters.
		/// </summary>
		/// <param name="list">Reads the list.</param>
		/// <param name="name">The name of the list, for the messages.</param>
		private void AddListRules(Expression<Func<ProfileCardSection, IEnumerable<string>>> list, string name)
		{
			_ = RuleFor(list).NotNull().Must(items => items is null || items.Count() <= MaximumListItems).WithMessage($"{name} has at most {MaximumListItems} items.");
			_ = RuleForEach(list).NotNull().MaximumLength(MaximumShortTextLength).WithMessage($"An item of {name} has at most {MaximumShortTextLength} characters.");
		}
		#endregion
	}
	#endregion
}
