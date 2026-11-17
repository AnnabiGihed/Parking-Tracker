using FluentValidation;

namespace ParkingTracker.Application.Features.SpotDecisions.Queries.FindFreeSpots;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Rejects a <see cref="FindFreeSpotsQuery"/> whose coordinates are out of range or whose count is below 1.
/// </summary>
public sealed class FindFreeSpotsQueryValidator : AbstractValidator<FindFreeSpotsQuery>
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="FindFreeSpotsQueryValidator"/> class.
	/// </summary>
	public FindFreeSpotsQueryValidator()
	{
		RuleFor(query => query.Latitude).InclusiveBetween(-90, 90);
		RuleFor(query => query.Longitude).InclusiveBetween(-180, 180);
		RuleFor(query => query.Count).GreaterThanOrEqualTo(1);
	}
	#endregion
}
