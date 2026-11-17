using ParkingTracker.Application.Features.Sectors.Services;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Domain.Features.Sectors.Services;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.SpotDecisions.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Builds the decision context from the saved card and the shipped reference data. A card without a known
///              municipality is treated as a City of Brussels card, and an unknown City sector falls back to Louise, as the first
///              version of the app did. Only the City of Brussels has street lists that must be confirmed unchanged.
/// </summary>
public sealed class DecisionContextFactory : IDecisionContextFactory
{
	#region Constants
	/// <summary>
	/// Failure code for reference data that lacks the City of Brussels.
	/// </summary>
	public const string MunicipalityMissingCode = "ReferenceData.MunicipalityMissing";

	/// <summary>
	/// The identifier of the City of Brussels in the reference data.
	/// </summary>
	private const string CityId = "bruxelles";

	/// <summary>
	/// The sector used when the card names none.
	/// </summary>
	private const string DefaultSectorId = "louise";
	#endregion

	#region Fields
	/// <summary>
	/// The store of the saved card.
	/// </summary>
	private readonly ISettingsStore settings;

	/// <summary>
	/// Source of the municipalities and sectors.
	/// </summary>
	private readonly IReferenceData referenceData;

	/// <summary>
	/// The checker of the official documents.
	/// </summary>
	private readonly IOfficialListChecker officialList;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="DecisionContextFactory"/> class.
	/// </summary>
	/// <param name="settings">The saved card.</param>
	/// <param name="referenceData">The shipped reference data.</param>
	/// <param name="officialList">The check of the official documents.</param>
	public DecisionContextFactory(ISettingsStore settings, IReferenceData referenceData, IOfficialListChecker officialList)
	{
		this.settings = settings;
		this.referenceData = referenceData;
		this.officialList = officialList;
	}
	#endregion

	#region IDecisionContextFactory Implementation
	/// <inheritdoc />
	public async Task<Result<DecisionContext>> CreateAsync(DateTimeOffset now, CancellationToken cancellationToken)
	{
		var data = referenceData.GetReferenceData();
		if (data.IsFailure)
		{
			return Result.Failure<DecisionContext>(data.Error);
		}

		var card = await settings.GetCardAsync().ConfigureAwait(false);
		var municipalities = data.Value.Municipalities;
		var municipality = municipalities.FirstOrDefault(m => string.Equals(m.Id, card?.Municipality ?? CityId, StringComparison.Ordinal)) ?? municipalities.FirstOrDefault(m => string.Equals(m.Id, CityId, StringComparison.Ordinal));
		if (municipality is null)
		{
			return Result.Failure<DecisionContext>(new Error(MunicipalityMissingCode, $"The municipality '{CityId}' is missing from the reference data."));
		}

		if (municipality.Kind != MunicipalityKind.Streets)
		{
			return Result.Success(new DecisionContext(card, municipality, null, null));
		}

		var sector = BuildSector(data.Value, card?.Sector);
		if (sector.IsFailure)
		{
			return Result.Failure<DecisionContext>(sector.Error);
		}

		var status = await officialList.EnsureFreshAsync(sector.Value, now, cancellationToken).ConfigureAwait(false);
		return Result.Success(new DecisionContext(card, municipality, sector.Value, status));
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds the sector model of the chosen sector.
	/// </summary>
	/// <param name="data">Reference data to look the sector up in.</param>
	/// <param name="sectorId">The identifier of the sector.</param>
	/// <returns>The sector model, or the failure when the data is invalid.</returns>
	private static Result<SectorModel> BuildSector(ReferenceDataSet data, string? sectorId)
	{
		var definition = data.CitySectors.FirstOrDefault(s => string.Equals(s.Id, sectorId ?? DefaultSectorId, StringComparison.Ordinal)) ?? data.CitySectors.FirstOrDefault(s => string.Equals(s.Id, DefaultSectorId, StringComparison.Ordinal));
		if (definition is null)
		{
			return Result.Failure<SectorModel>(new Error(MunicipalityMissingCode, $"The sector '{DefaultSectorId}' is missing from the reference data."));
		}

		var extra = data.ToleranceSectors.FirstOrDefault(s => string.Equals(s.Id, definition.Id, StringComparison.Ordinal));
		var extraSources = (extra?.Sources ?? []).Where(source => definition.Sources.All(known => !string.Equals(known.Url, source.Url, StringComparison.Ordinal)));
		return SectorModel.Build(definition with
		{
			Tolerances = extra?.Tolerances ?? [],
			Sources = definition.Sources.Concat(extraSources).ToList(),
		});
	}
	#endregion
}
