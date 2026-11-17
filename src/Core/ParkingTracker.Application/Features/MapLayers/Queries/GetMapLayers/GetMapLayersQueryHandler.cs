using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using Pivot.Framework.Application.Abstractions.Messaging.Queries;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.MapLayers.Queries.GetMapLayers;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Handles <see cref="GetMapLayersQuery"/>: reads the switched-on layers at the same time and keeps the layers that
///              could be read when another one fails. The query itself never fails for a source problem.
/// </summary>
public sealed class GetMapLayersQueryHandler : IQueryHandler<GetMapLayersQuery, GetMapLayersResponse>
{
	#region Constants
	/// <summary>
	/// The municipality assumed for a card saved by an older version that did not record it.
	/// </summary>
	private const string CityId = "bruxelles";
	#endregion

	#region Fields
	/// <summary>
	/// Port to parking.brussels.
	/// </summary>
	private readonly IParkingBrusselsClient parking;

	/// <summary>
	/// Port to the Brussels Mobility layers.
	/// </summary>
	private readonly IMobilityLayersClient mobility;

	/// <summary>
	/// Port to the saved card.
	/// </summary>
	private readonly ISettingsStore settings;

	/// <summary>
	/// Port to the municipalities.
	/// </summary>
	private readonly IReferenceData referenceData;

	/// <summary>
	/// Clock that supplies the current moment.
	/// </summary>
	private readonly TimeProvider clock;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="GetMapLayersQueryHandler"/> class.
	/// </summary>
	/// <param name="parking">Reads spaces and areas.</param>
	/// <param name="mobility">Reads roadworks, car parks and charging points.</param>
	/// <param name="settings">Gives the saved card.</param>
	/// <param name="referenceData">Gives the municipality of the card.</param>
	/// <param name="clock">Clock that supplies the current moment.</param>
	public GetMapLayersQueryHandler(IParkingBrusselsClient parking, IMobilityLayersClient mobility, ISettingsStore settings, IReferenceData referenceData, TimeProvider clock)
	{
		this.parking = parking;
		this.mobility = mobility;
		this.settings = settings;
		this.referenceData = referenceData;
		this.clock = clock;
	}
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public async Task<Result<GetMapLayersResponse>> Handle(GetMapLayersQuery request, CancellationToken cancellationToken)
	{
		var box = new GeoBox(request.South, request.West, request.North, request.East);
		var now = clock.GetLocalNow();
		var layers = request.Layers;
		var areas = layers.Streets ? LoadAreasAsync(box, cancellationToken) : EmptyLayer<SectorMaille>();
		var spaces = layers.Spaces ? parking.GetSpacesAsync(box, cancellationToken) : EmptyLayer<ReservedSpace>();
		var works = layers.Works ? mobility.GetWorksAsync(box, now, cancellationToken) : EmptyLayer<WorkItem>();
		var carParks = layers.CarParks ? mobility.GetCarParksAsync(box, cancellationToken) : EmptyLayer<CarPark>();
		var chargers = layers.Chargers ? mobility.GetChargersAsync(box, cancellationToken) : EmptyLayer<ChargingPoint>();
		await Task.WhenAll(areas, spaces, works, carParks, chargers).ConfigureAwait(false);

		var failures = new Dictionary<string, string>(StringComparer.Ordinal);
		return Result.Success(new GetMapLayersResponse(
			Take(await areas.ConfigureAwait(false), GetMapLayersResponse.AreasLayer, failures),
			Take(await spaces.ConfigureAwait(false), GetMapLayersResponse.SpacesLayer, failures),
			Take(await works.ConfigureAwait(false), GetMapLayersResponse.WorksLayer, failures),
			Take(await carParks.ConfigureAwait(false), GetMapLayersResponse.CarParksLayer, failures),
			Take(await chargers.ConfigureAwait(false), GetMapLayersResponse.ChargersLayer, failures),
			failures));
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Gives the answer of a layer that is switched off.
	/// </summary>
	/// <typeparam name="T">The type of the items of the layer.</typeparam>
	/// <returns>An empty success.</returns>
	private static Task<Result<IReadOnlyList<T>>> EmptyLayer<T>()
	{
		return Task.FromResult(Result.Success<IReadOnlyList<T>>([]));
	}

	/// <summary>
	/// Takes the items of a layer, or notes why the layer is empty.
	/// </summary>
	/// <typeparam name="T">The type of the items of the layer.</typeparam>
	/// <param name="result">What the source answered.</param>
	/// <param name="layer">The name of the layer.</param>
	/// <param name="failures">Collects the reasons of the layers that failed.</param>
	/// <returns>The items, or an empty list when the source failed.</returns>
	private static IReadOnlyList<T> Take<T>(Result<IReadOnlyList<T>> result, string layer, Dictionary<string, string> failures)
	{
		if (result.IsSuccess)
		{
			return result.Value;
		}

		failures[layer] = result.Error.Message;
		return [];
	}

	/// <summary>
	/// Reads the outlines of the areas and border zones on the card, for municipalities that issue cards per area.
	/// </summary>
	/// <param name="box">The area, in degrees.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The outlines; empty when the card has no areas; a failure when the source cannot be read.</returns>
	private async Task<Result<IReadOnlyList<SectorMaille>>> LoadAreasAsync(GeoBox box, CancellationToken cancellationToken)
	{
		var card = await settings.GetCardAsync().ConfigureAwait(false);
		var data = referenceData.GetReferenceData();
		if (card is null || data.IsFailure || card.Mailles.Count == 0)
		{
			return Result.Success<IReadOnlyList<SectorMaille>>([]);
		}

		var municipality = data.Value.Municipalities.FirstOrDefault(m => string.Equals(m.Id, card.Municipality, StringComparison.Ordinal))
			?? data.Value.Municipalities.FirstOrDefault(m => string.Equals(m.Id, CityId, StringComparison.Ordinal));
		if (municipality?.Kind != MunicipalityKind.Mailles)
		{
			return Result.Success<IReadOnlyList<SectorMaille>>([]);
		}

		var mailles = await parking.GetMaillesAsync(box, cancellationToken).ConfigureAwait(false);
		return mailles.IsFailure
			? mailles
			: Result.Success<IReadOnlyList<SectorMaille>>([.. mailles.Value.Where(maille => card.Mailles.Contains(maille.ZoneCode, StringComparer.Ordinal) || card.Lims.Contains(maille.ZoneCode, StringComparer.Ordinal))]);
	}
	#endregion
}
