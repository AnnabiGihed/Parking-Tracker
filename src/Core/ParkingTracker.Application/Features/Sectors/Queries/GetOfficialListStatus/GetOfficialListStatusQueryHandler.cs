using ParkingTracker.Application.Features.SpotDecisions.Services;
using Pivot.Framework.Application.Abstractions.Messaging.Queries;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.Sectors.Queries.GetOfficialListStatus;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Handles <see cref="GetOfficialListStatusQuery"/>: builds the decision context, which refreshes the official-list
///              check when it is stale, and returns the status it holds.
/// </summary>
public sealed class GetOfficialListStatusQueryHandler : IQueryHandler<GetOfficialListStatusQuery, GetOfficialListStatusResponse>
{
	#region Fields
	/// <summary>
	/// Factory that builds the decision context from the saved card and the reference data.
	/// </summary>
	private readonly IDecisionContextFactory contexts;

	/// <summary>
	/// Clock that supplies the current moment.
	/// </summary>
	private readonly TimeProvider clock;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="GetOfficialListStatusQueryHandler"/> class.
	/// </summary>
	/// <param name="contexts">Builds the card and street-list context.</param>
	/// <param name="clock">Clock that supplies the current moment.</param>
	public GetOfficialListStatusQueryHandler(IDecisionContextFactory contexts, TimeProvider clock)
	{
		this.contexts = contexts;
		this.clock = clock;
	}
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public async Task<Result<GetOfficialListStatusResponse>> Handle(GetOfficialListStatusQuery request, CancellationToken cancellationToken)
	{
		var context = await contexts.CreateAsync(clock.GetLocalNow(), cancellationToken).ConfigureAwait(false);
		return context.IsFailure
			? Result.Failure<GetOfficialListStatusResponse>(context.Error)
			: Result.Success(new GetOfficialListStatusResponse(context.Value.OfficialList));
	}
	#endregion
}
