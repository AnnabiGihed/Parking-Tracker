using System.Globalization;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Services;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using Pivot.Framework.Application.Abstractions.Messaging.Queries;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.Sectors.Queries.GetMailles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Handles <see cref="GetMaillesQuery"/>: reads the full list of areas from parking.brussels and keeps the ones that
///              belong to the municipality, as the card form of the first version of the app did.
/// </summary>
public sealed class GetMaillesQueryHandler : IQueryHandler<GetMaillesQuery, GetMaillesResponse>
{
	#region Fields
	/// <summary>
	/// Port to parking.brussels.
	/// </summary>
	private readonly IParkingBrusselsClient parking;

	/// <summary>
	/// Port to the municipalities.
	/// </summary>
	private readonly IReferenceData referenceData;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="GetMaillesQueryHandler"/> class.
	/// </summary>
	/// <param name="parking">Reads the list of areas.</param>
	/// <param name="referenceData">Gives the municipality with its code prefixes.</param>
	public GetMaillesQueryHandler(IParkingBrusselsClient parking, IReferenceData referenceData)
	{
		this.parking = parking;
		this.referenceData = referenceData;
	}
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public async Task<Result<GetMaillesResponse>> Handle(GetMaillesQuery request, CancellationToken cancellationToken)
	{
		var data = referenceData.GetReferenceData();
		if (data.IsFailure)
		{
			return Result.Failure<GetMaillesResponse>(data.Error);
		}

		var municipality = data.Value.Municipalities.FirstOrDefault(m => string.Equals(m.Id, request.MunicipalityId, StringComparison.Ordinal));
		if (municipality is null)
		{
			return Result.Failure<GetMaillesResponse>(new Error(DecisionContextFactory.MunicipalityMissingCode, $"The municipality '{request.MunicipalityId}' is missing from the reference data."));
		}

		var all = await parking.GetMailleOptionsAsync(cancellationToken).ConfigureAwait(false);
		return all.IsFailure ? Result.Failure<GetMaillesResponse>(all.Error) : Result.Success(Select(municipality, all.Value));
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Picks the areas and border zones of a municipality.
	/// </summary>
	/// <param name="municipality">The municipality of the card.</param>
	/// <param name="all">Every area of the region.</param>
	/// <returns>The areas sorted by name, and the border zones.</returns>
	private static GetMaillesResponse Select(Municipality municipality, IReadOnlyList<MailleOption> all)
	{
		var areas = all
			.Where(option => municipality.Prefixes.Any(prefix => string.Equals(option.ZoneCode, prefix, StringComparison.Ordinal) || option.ZoneCode.StartsWith(prefix + "_", StringComparison.Ordinal)))
			.OrderBy(option => option.DisplayName, StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.None))
			.ToList();
		var borderZones = all.Where(option => municipality.Lims.Contains(option.ZoneCode, StringComparer.Ordinal)).ToList();
		return new GetMaillesResponse(areas, borderZones);
	}
	#endregion
}
