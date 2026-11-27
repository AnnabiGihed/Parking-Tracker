using ParkingTracker.Application.Features.ProfileSync.Abstractions;
using ParkingTracker.Contracts.Features.Profiles;

namespace ParkingTracker.Application.Features.ProfileSync.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The merge rules of the profile sync (ADR-0032), as pure functions. Each section is merged on its own and the whole
///              favourites list is one section. The section with the newer moment wins; a section without a moment (never changed on
///              the phone, or the Unix epoch in a document) is older than any moment; on equal moments the server wins. A section the
///              user cleared on the phone (absent, with a moment) beats an older value on the server, so that a clear propagates. When
///              neither side has a moment, the side that holds data wins over a side that holds none, so that data from before the
///              sync existed is not thrown away by an empty section; with data on both sides the server wins. The moment of a clear
///              travels in <see cref="ProfileClearedSections"/>: an absent section of a document has the moment of its clear (or none, for a
///              document written before that field existed), so a clear is compared with the other side's value like any other change.
/// </summary>
public static class ProfileMerge
{
	#region Public Methods
	/// <summary>
	/// Merges the local profile with the document of the server, section by section.
	/// </summary>
	/// <param name="local">The local profile and its sync state.</param>
	/// <param name="server">The document the server holds.</param>
	/// <returns>The merged document with the moment of each of its sections.</returns>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public static MergedProfile Merge(ProfileSnapshot local, ProfileDocument server)
	{
		ArgumentNullException.ThrowIfNull(local);
		ArgumentNullException.ThrowIfNull(server);
		var doc = local.Document;
		var times = local.State.Times;

		var home = Pick(times.Home, doc.Home is not null, Known(server.Home?.UpdatedAt ?? server.Cleared?.Home), server.Home is not null);
		var work = Pick(times.Work, doc.Work is not null, Known(server.Work?.UpdatedAt ?? server.Cleared?.Work), server.Work is not null);
		var favourites = Pick(times.Favourites, doc.Favourites.Places.Count > 0, Known(server.Favourites.UpdatedAt), server.Favourites.Places.Count > 0);
		var vehicle = Pick(times.Vehicle, doc.Vehicle is not null, Known(server.Vehicle?.UpdatedAt ?? server.Cleared?.Vehicle), server.Vehicle is not null);
		var card = Pick(times.Card, doc.Card is not null, Known(server.Card?.UpdatedAt ?? server.Cleared?.Card), server.Card is not null);

		var mergedTimes = new SectionTimes(home.At, work.At, favourites.At, vehicle.At, card.At);
		var mergedHome = home.IsLocal ? doc.Home : server.Home;
		var mergedWork = work.IsLocal ? doc.Work : server.Work;
		var mergedVehicle = vehicle.IsLocal ? doc.Vehicle : server.Vehicle;
		var mergedCard = card.IsLocal ? doc.Card : server.Card;
		var merged = new ProfileDocument(
			ProfileDocument.SupportedSchemaVersion,
			mergedHome,
			mergedWork,
			favourites.IsLocal ? doc.Favourites : server.Favourites,
			mergedVehicle,
			mergedCard,
			ProfileMapping.ToClearedSections(mergedHome is null, mergedWork is null, mergedVehicle is null, mergedCard is null, mergedTimes));
		return new MergedProfile(merged, mergedTimes);
	}

	/// <summary>
	/// Tells whether two documents hold the same data and moments, comparing moments to the millisecond.
	/// </summary>
	/// <param name="first">The first document.</param>
	/// <param name="second">The second document.</param>
	/// <returns><see langword="true"/> when every section is equal.</returns>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public static bool AreSame(ProfileDocument first, ProfileDocument second)
	{
		ArgumentNullException.ThrowIfNull(first);
		ArgumentNullException.ThrowIfNull(second);
		return first.SchemaVersion == second.SchemaVersion
			&& SamePlace(first.Home, second.Home)
			&& SamePlace(first.Work, second.Work)
			&& SameFavourites(first.Favourites, second.Favourites)
			&& SameVehicle(first.Vehicle, second.Vehicle)
			&& SameCard(first.Card, second.Card)
			&& SameCleared(first.Cleared, second.Cleared);
	}

	/// <summary>
	/// Tells whether one section holds the same data in two documents, whatever its moment.
	/// </summary>
	/// <param name="section">The section to compare.</param>
	/// <param name="first">The first document.</param>
	/// <param name="second">The second document.</param>
	/// <returns><see langword="true"/> when the section is equal apart from its moment.</returns>
	/// <exception cref="ArgumentNullException">A document is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="section"/> is not a known section.</exception>
	public static bool SameData(SyncSection section, ProfileDocument first, ProfileDocument second)
	{
		ArgumentNullException.ThrowIfNull(first);
		ArgumentNullException.ThrowIfNull(second);
		return section switch
		{
			SyncSection.Home => SamePlace(WithoutMoment(first.Home), WithoutMoment(second.Home)),
			SyncSection.Work => SamePlace(WithoutMoment(first.Work), WithoutMoment(second.Work)),
			SyncSection.Favourites => first.Favourites.Places.SequenceEqual(second.Favourites.Places),
			SyncSection.Vehicle => SameVehicle(WithoutMoment(first.Vehicle), WithoutMoment(second.Vehicle)),
			SyncSection.Card => SameCard(WithoutMoment(first.Card), WithoutMoment(second.Card)),
			_ => throw new ArgumentOutOfRangeException(nameof(section), section, "Unknown section."),
		};
	}

	/// <summary>
	/// Gives the moments a document carries for its sections, with the epoch and absent sections read as no moment.
	/// </summary>
	/// <param name="document">The document.</param>
	/// <returns>The moments.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
	public static SectionTimes TimesOf(ProfileDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);
		return new SectionTimes(
			Known(document.Home?.UpdatedAt ?? document.Cleared?.Home),
			Known(document.Work?.UpdatedAt ?? document.Cleared?.Work),
			Known(document.Favourites.UpdatedAt),
			Known(document.Vehicle?.UpdatedAt ?? document.Cleared?.Vehicle),
			Known(document.Card?.UpdatedAt ?? document.Cleared?.Card));
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Reads the epoch, which stands for a section that never changed on a phone, as no moment.
	/// </summary>
	/// <param name="at">The moment of a document.</param>
	/// <returns>The moment, or <see langword="null"/>.</returns>
	private static DateTimeOffset? Known(DateTimeOffset? at) => at is { } value && value > DateTimeOffset.UnixEpoch ? value : null;

	/// <summary>
	/// Decides which side wins one section.
	/// </summary>
	/// <param name="localAt">The moment of the local section.</param>
	/// <param name="localHasData">Whether the local section holds data.</param>
	/// <param name="serverAt">The moment of the server section.</param>
	/// <param name="serverHasData">Whether the server section holds data.</param>
	/// <returns>Whether the local side wins, and the moment of the winner.</returns>
	private static Winner Pick(DateTimeOffset? localAt, bool localHasData, DateTimeOffset? serverAt, bool serverHasData)
	{
		var local = Known(localAt);
		bool localWins;
		if (local is null && serverAt is null)
		{
			localWins = localHasData && !serverHasData;
		}
		else if (local is null)
		{
			localWins = false;
		}
		else if (serverAt is null)
		{
			localWins = true;
		}
		else
		{
			localWins = local.Value.ToUnixTimeMilliseconds() > serverAt.Value.ToUnixTimeMilliseconds();
		}

		return new Winner(localWins, localWins ? local : serverAt);
	}

	/// <summary>
	/// Compares two moments to the millisecond.
	/// </summary>
	/// <param name="first">The first moment.</param>
	/// <param name="second">The second moment.</param>
	/// <returns><see langword="true"/> when they are the same instant at that precision.</returns>
	private static bool SameTime(DateTimeOffset first, DateTimeOffset second) => first.ToUnixTimeMilliseconds() == second.ToUnixTimeMilliseconds();

	/// <summary>
	/// Compares two home or work sections.
	/// </summary>
	/// <param name="first">The first section.</param>
	/// <param name="second">The second section.</param>
	/// <returns><see langword="true"/> when both are absent or equal.</returns>
	private static bool SamePlace(ProfilePlaceSection? first, ProfilePlaceSection? second)
	{
		if (first is null || second is null)
		{
			return first is null && second is null;
		}

		return string.Equals(first.Label, second.Label, StringComparison.Ordinal)
			&& first.Latitude.CompareTo(second.Latitude) == 0
			&& first.Longitude.CompareTo(second.Longitude) == 0
			&& string.Equals(first.AddressText, second.AddressText, StringComparison.Ordinal)
			&& SameTime(first.UpdatedAt, second.UpdatedAt);
	}

	/// <summary>
	/// Gives a place section with its moment replaced by the epoch, for a comparison of the data alone.
	/// </summary>
	/// <param name="section">The section, or <see langword="null"/>.</param>
	/// <returns>The copy, or <see langword="null"/>.</returns>
	private static ProfilePlaceSection? WithoutMoment(ProfilePlaceSection? section) => section is null ? null : section with { UpdatedAt = ProfileMapping.NoMoment };

	/// <summary>
	/// Gives a vehicle section with its moment replaced by the epoch, for a comparison of the data alone.
	/// </summary>
	/// <param name="section">The section, or <see langword="null"/>.</param>
	/// <returns>The copy, or <see langword="null"/>.</returns>
	private static ProfileVehicleSection? WithoutMoment(ProfileVehicleSection? section) => section is null ? null : section with { UpdatedAt = ProfileMapping.NoMoment };

	/// <summary>
	/// Gives a card section with its moment replaced by the epoch, for a comparison of the data alone.
	/// </summary>
	/// <param name="section">The section, or <see langword="null"/>.</param>
	/// <returns>The copy, or <see langword="null"/>.</returns>
	private static ProfileCardSection? WithoutMoment(ProfileCardSection? section) => section is null ? null : section with { UpdatedAt = ProfileMapping.NoMoment };

	/// <summary>
	/// Compares the moments of the clears of two documents; no entry and an entry with no moment are the same.
	/// </summary>
	/// <param name="first">The first entry.</param>
	/// <param name="second">The second entry.</param>
	/// <returns><see langword="true"/> when every moment is equal to the millisecond.</returns>
	private static bool SameCleared(ProfileClearedSections? first, ProfileClearedSections? second)
	{
		return Millis(first?.Home) == Millis(second?.Home)
			&& Millis(first?.Work) == Millis(second?.Work)
			&& Millis(first?.Vehicle) == Millis(second?.Vehicle)
			&& Millis(first?.Card) == Millis(second?.Card);
	}

	/// <summary>
	/// Gives a moment in the precision the database stores.
	/// </summary>
	/// <param name="at">The moment.</param>
	/// <returns>Milliseconds since 1970, or <see langword="null"/>.</returns>
	private static long? Millis(DateTimeOffset? at) => at?.ToUnixTimeMilliseconds();

	/// <summary>
	/// Compares two favourites sections.
	/// </summary>
	/// <param name="first">The first section.</param>
	/// <param name="second">The second section.</param>
	/// <returns><see langword="true"/> when the moment and the places in their order are equal.</returns>
	private static bool SameFavourites(ProfileFavouritesSection first, ProfileFavouritesSection second)
	{
		return SameTime(first.UpdatedAt, second.UpdatedAt) && first.Places.SequenceEqual(second.Places);
	}

	/// <summary>
	/// Compares two vehicle sections.
	/// </summary>
	/// <param name="first">The first section.</param>
	/// <param name="second">The second section.</param>
	/// <returns><see langword="true"/> when both are absent or equal.</returns>
	private static bool SameVehicle(ProfileVehicleSection? first, ProfileVehicleSection? second)
	{
		if (first is null || second is null)
		{
			return first is null && second is null;
		}

		return string.Equals(first.Make, second.Make, StringComparison.Ordinal)
			&& string.Equals(first.Model, second.Model, StringComparison.Ordinal)
			&& first.Year == second.Year
			&& string.Equals(first.Fuel, second.Fuel, StringComparison.Ordinal)
			&& string.Equals(first.EuroNorm, second.EuroNorm, StringComparison.Ordinal)
			&& first.Connectors.SequenceEqual(second.Connectors, StringComparer.Ordinal)
			&& Nullable.Equals(first.BatteryKwh, second.BatteryKwh)
			&& first.RangeKm == second.RangeKm
			&& string.Equals(first.Plate, second.Plate, StringComparison.Ordinal)
			&& SameTime(first.UpdatedAt, second.UpdatedAt);
	}

	/// <summary>
	/// Compares two card sections.
	/// </summary>
	/// <param name="first">The first section.</param>
	/// <param name="second">The second section.</param>
	/// <returns><see langword="true"/> when both are absent or equal.</returns>
	private static bool SameCard(ProfileCardSection? first, ProfileCardSection? second)
	{
		if (first is null || second is null)
		{
			return first is null && second is null;
		}

		return string.Equals(first.Municipality, second.Municipality, StringComparison.Ordinal)
			&& string.Equals(first.Sector, second.Sector, StringComparison.Ordinal)
			&& first.ValidFrom == second.ValidFrom
			&& first.ValidTo == second.ValidTo
			&& first.Tolerances.SequenceEqual(second.Tolerances, StringComparer.Ordinal)
			&& first.Mailles.SequenceEqual(second.Mailles, StringComparer.Ordinal)
			&& first.Lims.SequenceEqual(second.Lims, StringComparer.Ordinal)
			&& string.Equals(first.Number, second.Number, StringComparison.Ordinal)
			&& string.Equals(first.Plate, second.Plate, StringComparison.Ordinal)
			&& SameTime(first.UpdatedAt, second.UpdatedAt);
	}
	#endregion

	#region Nested Types
	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : Which side won a section and the moment it carries.
	/// </summary>
	/// <param name="IsLocal">Whether the local side won.</param>
	/// <param name="At">The moment of the winner, or <see langword="null"/>.</param>
	private readonly record struct Winner(bool IsLocal, DateTimeOffset? At);
	#endregion
}
