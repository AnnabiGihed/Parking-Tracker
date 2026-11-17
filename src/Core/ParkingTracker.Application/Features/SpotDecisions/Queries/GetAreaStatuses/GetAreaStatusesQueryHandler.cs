using ParkingTracker.Application.Features.SpotDecisions.Services;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using Pivot.Framework.Application.Abstractions.Messaging.Queries;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.SpotDecisions.Queries.GetAreaStatuses;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Handles <see cref="GetAreaStatusesQuery"/>: builds the decision context and evaluates the surfaces of the area.
///              A source that cannot be read is a failed result, because a map layer cannot say "can't verify" per street.
/// </summary>
public sealed class GetAreaStatusesQueryHandler : IQueryHandler<GetAreaStatusesQuery, IReadOnlyList<SurfaceStatus>>
{
	#region Fields
	/// <summary>
	/// Factory that builds the decision context from the saved card and the reference data.
	/// </summary>
	private readonly IDecisionContextFactory contexts;

	/// <summary>
	/// Provider that loads the status of every street surface in an area.
	/// </summary>
	private readonly IAreaStatusProvider areas;

	/// <summary>
	/// Clock that supplies the current moment.
	/// </summary>
	private readonly TimeProvider clock;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="GetAreaStatusesQueryHandler"/> class.
	/// </summary>
	/// <param name="contexts">Builds the card and street-list context.</param>
	/// <param name="areas">Fetches and evaluates the surfaces of an area.</param>
	/// <param name="clock">Clock that supplies the current moment.</param>
	public GetAreaStatusesQueryHandler(IDecisionContextFactory contexts, IAreaStatusProvider areas, TimeProvider clock)
	{
		this.contexts = contexts;
		this.areas = areas;
		this.clock = clock;
	}
	#endregion

	#region IQueryHandler Implementation
	/// <inheritdoc />
	public async Task<Result<IReadOnlyList<SurfaceStatus>>> Handle(GetAreaStatusesQuery request, CancellationToken cancellationToken)
	{
		var now = clock.GetLocalNow();
		var context = await contexts.CreateAsync(now, cancellationToken).ConfigureAwait(false);
		if (context.IsFailure)
		{
			return Result.Failure<IReadOnlyList<SurfaceStatus>>(context.Error);
		}

		var box = new GeoBox(request.South, request.West, request.North, request.East);
		return await areas.LoadAsync(context.Value, box, now, request.WithAddresses ? AddressFetching.Fetch : AddressFetching.Skip, cancellationToken).ConfigureAwait(false);
	}
	#endregion
}
