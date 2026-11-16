using ParkingTracker.Application.ReferenceData;
using ParkingTracker.Application.Sources;
using ParkingTracker.Domain.Addressing;
using ParkingTracker.Domain.Decisions;
using ParkingTracker.Domain.ReferenceData;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Decisions;

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

	private const string CityId = "bruxelles";
	private const string DefaultSectorId = "louise";
	#endregion

	#region Fields
	private readonly ISettingsStore settings;
	private readonly IReferenceData referenceData;
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

		var card = settings.GetCard();
		var municipalities = data.Value.Municipalities;
		var municipality = municipalities.FirstOrDefault(m => m.Id == (card?.Municipality ?? CityId)) ?? municipalities.FirstOrDefault(m => m.Id == CityId);
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
	private static Result<SectorModel> BuildSector(ReferenceDataSet data, string? sectorId)
	{
		var definition = data.CitySectors.FirstOrDefault(s => s.Id == (sectorId ?? DefaultSectorId)) ?? data.CitySectors.FirstOrDefault(s => s.Id == DefaultSectorId);
		if (definition is null)
		{
			return Result.Failure<SectorModel>(new Error(MunicipalityMissingCode, $"The sector '{DefaultSectorId}' is missing from the reference data."));
		}

		var extra = data.ToleranceSectors.FirstOrDefault(s => s.Id == definition.Id);
		var extraSources = (extra?.Sources ?? []).Where(source => definition.Sources.All(known => known.Url != source.Url));
		return SectorModel.Build(definition with
		{
			Tolerances = extra?.Tolerances ?? [],
			Sources = definition.Sources.Concat(extraSources).ToList(),
		});
	}
	#endregion
}
