using Microsoft.Extensions.Logging;
using ParkingTracker.Application.Features.ProfileSync.Abstractions;
using ParkingTracker.Application.Features.ProfileSync.Services;
using ParkingTracker.Application.Features.ResidentCards.Abstractions;
using ParkingTracker.Application.Features.SavedPlaces.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Application.Features.Vehicles.Abstractions;
using ParkingTracker.Contracts.Features.Profiles;
using ParkingTracker.Domain.Features.ResidentCards.Aggregates;
using ParkingTracker.Domain.Features.SavedPlaces.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.Vehicles.Aggregates;
using Pivot.Framework.Domain.Shared;
using PlaceList = ParkingTracker.Domain.Features.SavedPlaces.Aggregates.SavedPlaces;

namespace ParkingTracker.Persistence.Device.Features.ProfileSync;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Reads the local profile as a document and writes a merged or downloaded document back (ADR-0032), over the repositories
///              of the device database. A read takes the sync state first and the data after it, so that a change in between shows
///              as a different set of moments at the next conditional write. Every write holds one scope of the unit of work (the write
///              gate), re-reads the sync state inside it, stops when a section changed since the snapshot, and otherwise stages the
///              changed sections and the new sync state together. It never stamps a section, so data from the server is not marked as a
///              change of the user.
/// </summary>
public sealed partial class ProfileLocalStore : IProfileLocalStore
{
	#region Fields
	/// <summary>
	/// The repository of the saved places.
	/// </summary>
	private readonly ISavedPlacesRepository places;

	/// <summary>
	/// The repository of the vehicle profile.
	/// </summary>
	private readonly IVehicleRepository vehicles;

	/// <summary>
	/// The repository of the residents' card.
	/// </summary>
	private readonly IResidentCardRepository cards;

	/// <summary>
	/// The repository of the sync state.
	/// </summary>
	private readonly ISyncStateRepository state;

	/// <summary>
	/// The account the data on the phone is bound to, which a write checks inside its scope.
	/// </summary>
	private readonly IDeviceOwnerStore owners;

	/// <summary>
	/// The unit of work that serialises the writes.
	/// </summary>
	private readonly IDeviceUnitOfWork unitOfWork;

	/// <summary>
	/// Brings the schema up to date before the first read or write.
	/// </summary>
	private readonly IDeviceDatabaseInitializer initializer;

	/// <summary>
	/// The municipalities that a card is checked against.
	/// </summary>
	private readonly IReferenceData referenceData;

	/// <summary>
	/// The clock that bounds the model year of a vehicle.
	/// </summary>
	private readonly TimeProvider clock;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<ProfileLocalStore> logger;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ProfileLocalStore"/> class.
	/// </summary>
	/// <param name="places">The repository of the saved places.</param>
	/// <param name="vehicles">The repository of the vehicle profile.</param>
	/// <param name="cards">The repository of the residents' card.</param>
	/// <param name="state">The repository of the sync state.</param>
	/// <param name="owners">The account the data on the phone is bound to.</param>
	/// <param name="unitOfWork">The unit of work that serialises the writes.</param>
	/// <param name="initializer">Brings the schema up to date before the first read or write.</param>
	/// <param name="referenceData">The municipalities that a card is checked against.</param>
	/// <param name="clock">The clock that bounds the model year of a vehicle.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public ProfileLocalStore(
		ISavedPlacesRepository places,
		IVehicleRepository vehicles,
		IResidentCardRepository cards,
		ISyncStateRepository state,
		IDeviceOwnerStore owners,
		IDeviceUnitOfWork unitOfWork,
		IDeviceDatabaseInitializer initializer,
		IReferenceData referenceData,
		TimeProvider clock,
		ILogger<ProfileLocalStore> logger)
	{
		ArgumentNullException.ThrowIfNull(places);
		ArgumentNullException.ThrowIfNull(vehicles);
		ArgumentNullException.ThrowIfNull(cards);
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(owners);
		ArgumentNullException.ThrowIfNull(unitOfWork);
		ArgumentNullException.ThrowIfNull(initializer);
		ArgumentNullException.ThrowIfNull(referenceData);
		ArgumentNullException.ThrowIfNull(clock);
		ArgumentNullException.ThrowIfNull(logger);
		this.places = places;
		this.vehicles = vehicles;
		this.cards = cards;
		this.state = state;
		this.owners = owners;
		this.unitOfWork = unitOfWork;
		this.initializer = initializer;
		this.referenceData = referenceData;
		this.clock = clock;
		this.logger = logger;
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs a section of the local profile that could not be read or written. The log carries the code only, never a value.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="code">The code of the error.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The local profile could not be used for the sync ({Code})")]
	private static partial void LogFailed(ILogger logger, string code);
	#endregion

	#region IProfileLocalStore Implementation
	/// <inheritdoc />
	public async Task<Result<ProfileSnapshot>> ReadAsync(CancellationToken cancellationToken = default)
	{
		var ready = await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
		if (ready.IsFailure)
		{
			return Result.Failure<ProfileSnapshot>(ready.Error);
		}

		// The state is read before the data: a change that lands in between shows up as different moments at the next conditional write.
		var syncState = await state.GetAsync(cancellationToken).ConfigureAwait(false);
		var list = await places.GetAsync(cancellationToken).ConfigureAwait(false);
		var vehicle = await vehicles.GetAsync(cancellationToken).ConfigureAwait(false);
		var card = await cards.GetAsync(cancellationToken).ConfigureAwait(false);
		var failed = FirstFailure(syncState, list, vehicle, card);
		if (failed is not null)
		{
			return Failed<ProfileSnapshot>(failed);
		}

		var times = syncState.Value.Times;
		var placeList = list.IsSuccess ? list.Value : null;
		var document = new ProfileDocument(
			ProfileDocument.SupportedSchemaVersion,
			ProfileMapping.ToPlaceSection(placeList?.Home, times.Home),
			ProfileMapping.ToPlaceSection(placeList?.Work, times.Work),
			ProfileMapping.ToFavouritesSection(placeList, times.Favourites),
			ProfileMapping.ToVehicleSection(vehicle.IsSuccess ? vehicle.Value.ToDetails() : null, times.Vehicle),
			ProfileMapping.ToCardSection(card.IsSuccess ? card.Value.ToParkingCard() : null, times.Card),
			ProfileMapping.ToClearedSections(
				placeList?.Home is null,
				placeList?.Work is null,
				!vehicle.IsSuccess,
				!card.IsSuccess,
				times));
		return new ProfileSnapshot(document, syncState.Value);
	}

	/// <inheritdoc />
	public async Task<Result<bool>> ApplyAsync(ProfileSnapshot snapshot, ProfileDocument document, SectionTimes times, long? revision, bool needsUpload, Func<string?> currentOwnerHash, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(times);
		ArgumentNullException.ThrowIfNull(currentOwnerHash);
		var ready = await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
		if (ready.IsFailure)
		{
			return Result.Failure<bool>(ready.Error);
		}

		var scope = await unitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);
		await using (scope.ConfigureAwait(false))
		{
			var current = await state.GetAsync(cancellationToken).ConfigureAwait(false);
			if (current.IsFailure)
			{
				return Failed<bool>(current);
			}

			// Nothing is written when a section changed, when the profile was removed (the base revision and the moments no longer match
			// the snapshot) or when the phone is no longer bound to the account the sync was verified for; all of it is read under the gate.
			if (!current.Value.Times.SameAs(snapshot.State.Times) || current.Value.BaseRevision != snapshot.State.BaseRevision || !await IsBoundToAsync(currentOwnerHash, cancellationToken).ConfigureAwait(false))
			{
				return false;
			}

			var staged = await StageSectionsAsync(scope, snapshot.Document, document, cancellationToken).ConfigureAwait(false);
			if (staged.IsFailure)
			{
				return Failed<bool>(staged);
			}

			state.StageState(scope, new SyncState(revision, needsUpload, times));
			var committed = await scope.CommitAsync(cancellationToken).ConfigureAwait(false);
			return committed.IsFailure ? Failed<bool>(committed) : Result.Success(true);
		}
	}

	/// <inheritdoc />
	public async Task<Result<bool>> MarkSavedAsync(long? revision, SectionTimes uploaded, Func<string?> currentOwnerHash, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(uploaded);
		ArgumentNullException.ThrowIfNull(currentOwnerHash);
		var ready = await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
		if (ready.IsFailure)
		{
			return Result.Failure<bool>(ready.Error);
		}

		var scope = await unitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);
		await using (scope.ConfigureAwait(false))
		{
			var current = await state.GetAsync(cancellationToken).ConfigureAwait(false);
			if (current.IsFailure)
			{
				return Failed<bool>(current);
			}

			// A phone that is bound to another account now, or whose profile was removed since the upload was built (the moments it had are
			// gone), is left as it is: writing a row would bring a removed profile's state back.
			var removed = !uploaded.SameAs(SectionTimes.None) && current.Value.Times.SameAs(SectionTimes.None);
			if (removed || !await IsBoundToAsync(currentOwnerHash, cancellationToken).ConfigureAwait(false))
			{
				return false;
			}

			// A change made while the upload ran keeps the flag set; the new revision is still stored, so that the next round can match it.
			var unchanged = current.Value.Times.SameAs(uploaded);
			state.StageState(scope, new SyncState(revision, !unchanged, current.Value.Times));
			var committed = await scope.CommitAsync(cancellationToken).ConfigureAwait(false);
			return committed.IsFailure ? Failed<bool>(committed) : Result.Success(unchanged);
		}
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Tells whether the phone is bound to an account. It is read while the caller holds the write gate, so that no bind can interleave.
	/// </summary>
	/// <param name="currentOwnerHash">Gives the hash of the verified account; asked here, under the gate, and never earlier.</param>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task with <see langword="true"/> when an account is verified and the bound hash equals it.</returns>
	private async Task<bool> IsBoundToAsync(Func<string?> currentOwnerHash, CancellationToken cancellationToken)
	{
		var verified = currentOwnerHash();
		if (verified is null)
		{
			return false;
		}

		var bound = await owners.GetAccountHashAsync(cancellationToken).ConfigureAwait(false);
		return bound.IsSuccess && string.Equals(bound.Value, verified, StringComparison.Ordinal);
	}

	/// <summary>
	/// Stages the sections that differ between what the phone holds and the document to hold.
	/// </summary>
	/// <param name="scope">The scope that holds the write gate.</param>
	/// <param name="local">What the phone holds.</param>
	/// <param name="target">The document to hold.</param>
	/// <param name="cancellationToken">Cancels the reads.</param>
	/// <returns>A task with success, or the first failure; nothing is committed here.</returns>
	private async Task<Result> StageSectionsAsync(IDeviceChangeScope scope, ProfileDocument local, ProfileDocument target, CancellationToken cancellationToken)
	{
		var changes = Enum.GetValues<SyncSection>().Where(section => !ProfileMerge.SameData(section, local, target)).ToHashSet();
		if (changes.Overlaps([SyncSection.Home, SyncSection.Work, SyncSection.Favourites]))
		{
			var staged = await StagePlacesAsync(scope, target, changes, cancellationToken).ConfigureAwait(false);
			if (staged.IsFailure)
			{
				return staged;
			}
		}

		if (changes.Contains(SyncSection.Vehicle))
		{
			var staged = await StageVehicleAsync(scope, target.Vehicle, cancellationToken).ConfigureAwait(false);
			if (staged.IsFailure)
			{
				return staged;
			}
		}

		return changes.Contains(SyncSection.Card) ? await StageCardAsync(scope, target.Card, cancellationToken).ConfigureAwait(false) : Result.Success();
	}

	/// <summary>
	/// Stages the home, the work place and the favourites that changed, on the one list of places.
	/// </summary>
	/// <param name="scope">The scope that holds the write gate.</param>
	/// <param name="target">The document to hold.</param>
	/// <param name="changes">The sections that differ.</param>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task with success, or the first failure.</returns>
	private async Task<Result> StagePlacesAsync(IDeviceChangeScope scope, ProfileDocument target, HashSet<SyncSection> changes, CancellationToken cancellationToken)
	{
		var stored = await places.GetAsync(cancellationToken).ConfigureAwait(false);
		if (stored.IsFailure && !DeviceDataErrors.IsNotStored(stored))
		{
			return stored;
		}

		var list = stored.IsSuccess ? stored.Value : PlaceList.Create();
		var edited = EditPlaces(list, target, changes);
		if (edited.IsFailure)
		{
			return edited;
		}

		if (list.Places.Count == 0)
		{
			places.Remove(scope);
			return Result.Success();
		}

		return await places.UpdateAsync(scope, list, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Changes the list of places to hold the sections of the document that differ.
	/// </summary>
	/// <param name="list">The list to change.</param>
	/// <param name="target">The document to hold.</param>
	/// <param name="changes">The sections that differ.</param>
	/// <returns>Success, or the first rule of the domain a place breaks.</returns>
	private static Result EditPlaces(PlaceList list, ProfileDocument target, HashSet<SyncSection> changes)
	{
		if (changes.Contains(SyncSection.Home))
		{
			var home = SetShortcut(list, PlaceKind.Home, target.Home);
			if (home.IsFailure)
			{
				return home;
			}
		}

		if (changes.Contains(SyncSection.Work))
		{
			var work = SetShortcut(list, PlaceKind.Work, target.Work);
			if (work.IsFailure)
			{
				return work;
			}
		}

		if (!changes.Contains(SyncSection.Favourites))
		{
			return Result.Success();
		}

		foreach (var favourite in list.Places.Where(place => place.Kind == PlaceKind.Favourite).ToArray())
		{
			_ = list.Remove(favourite.Id);
		}

		foreach (var place in target.Favourites.Places)
		{
			var added = list.AddFavourite(place.Label, new GeoPoint(place.Latitude, place.Longitude), place.AddressText);
			if (added.IsFailure)
			{
				return Result.Failure(added.Error, added.ResultExceptionType);
			}
		}

		return Result.Success();
	}

	/// <summary>
	/// Sets or removes the home or the work place.
	/// </summary>
	/// <param name="list">The list to change.</param>
	/// <param name="kind">Home or work.</param>
	/// <param name="section">The section to hold, or <see langword="null"/> to remove the place.</param>
	/// <returns>Success, or the rule of the domain the place breaks.</returns>
	private static Result SetShortcut(PlaceList list, PlaceKind kind, ProfilePlaceSection? section)
	{
		var existing = kind == PlaceKind.Home ? list.Home : list.Work;
		if (section is null)
		{
			return existing is null ? Result.Success() : list.Remove(existing.Id);
		}

		var label = string.IsNullOrWhiteSpace(section.Label) ? kind.ToString() : section.Label;
		var point = new GeoPoint(section.Latitude, section.Longitude);
		return kind == PlaceKind.Home ? list.SetHome(label, point, section.AddressText) : list.SetWork(label, point, section.AddressText);
	}

	/// <summary>
	/// Stages the vehicle profile, or its removal.
	/// </summary>
	/// <param name="scope">The scope that holds the write gate.</param>
	/// <param name="section">The section to hold, or <see langword="null"/> to remove the vehicle.</param>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task with success, or the first failure.</returns>
	private async Task<Result> StageVehicleAsync(IDeviceChangeScope scope, ProfileVehicleSection? section, CancellationToken cancellationToken)
	{
		if (section is null)
		{
			vehicles.Remove(scope);
			return Result.Success();
		}

		var details = ProfileMapping.ToVehicleDetails(section);
		if (details.IsFailure)
		{
			return details;
		}

		var year = clock.GetLocalNow().Year;
		var stored = await vehicles.GetAsync(cancellationToken).ConfigureAwait(false);
		if (stored.IsFailure && !DeviceDataErrors.IsNotStored(stored))
		{
			return stored;
		}

		Result<Vehicle> vehicle;
		if (stored.IsSuccess)
		{
			var updated = stored.Value.Update(details.Value, year);
			vehicle = updated.IsFailure ? Result.Failure<Vehicle>(updated.Error, updated.ResultExceptionType) : stored.Value;
		}
		else
		{
			vehicle = Vehicle.Create(details.Value, year);
		}

		return vehicle.IsFailure ? vehicle : await vehicles.UpdateAsync(scope, vehicle.Value, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Stages the residents' card, or its removal.
	/// </summary>
	/// <param name="scope">The scope that holds the write gate.</param>
	/// <param name="section">The section to hold, or <see langword="null"/> to remove the card.</param>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>A task with success, or the first failure.</returns>
	private async Task<Result> StageCardAsync(IDeviceChangeScope scope, ProfileCardSection? section, CancellationToken cancellationToken)
	{
		if (section is null)
		{
			cards.Remove(scope);
			return Result.Success();
		}

		var data = referenceData.GetReferenceData();
		if (data.IsFailure)
		{
			return data;
		}

		var municipality = data.Value.Municipalities.FirstOrDefault(candidate => string.Equals(candidate.Id, section.Municipality, StringComparison.Ordinal));
		if (municipality is null)
		{
			return ProfileMapping.Invalid<ResidentCard>();
		}

		var card = ResidentCard.Create(municipality, ProfileMapping.ToParkingCard(section));
		return card.IsFailure ? card : await cards.UpdateAsync(scope, card.Value, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Finds the first read that failed for a reason other than "nothing stored".
	/// </summary>
	/// <param name="syncState">The read of the sync state.</param>
	/// <param name="list">The read of the places.</param>
	/// <param name="vehicle">The read of the vehicle.</param>
	/// <param name="card">The read of the card.</param>
	/// <returns>The failed result, or <see langword="null"/> when every read succeeded or found nothing.</returns>
	private static Result? FirstFailure(Result syncState, Result list, Result vehicle, Result card)
	{
		if (syncState.IsFailure)
		{
			return syncState;
		}

		return new[] { list, vehicle, card }.FirstOrDefault(read => read.IsFailure && !DeviceDataErrors.IsNotStored(read));
	}

	/// <summary>
	/// Logs a failure by its code and passes it on as a failed result of another type.
	/// </summary>
	/// <typeparam name="T">The type of the result.</typeparam>
	/// <param name="failed">The failed result.</param>
	/// <returns>The failure.</returns>
	private Result<T> Failed<T>(Result failed)
	{
		LogFailed(logger, failed.Error.Code);
		return Result.Failure<T>(failed.Error, failed.ResultExceptionType);
	}
	#endregion
}
