using FluentValidation;

namespace ParkingTracker.Application.Queries;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Rejects a <see cref="DecideSpotQuery"/> whose coordinates are out of range or whose accuracy is negative.
/// </summary>
public sealed class DecideSpotQueryValidator : AbstractValidator<DecideSpotQuery>
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="DecideSpotQueryValidator"/> class.
	/// </summary>
	public DecideSpotQueryValidator()
	{
		RuleFor(query => query.Latitude).InclusiveBetween(-90, 90);
		RuleFor(query => query.Longitude).InclusiveBetween(-180, 180);
		RuleFor(query => query.AccuracyMeters).GreaterThanOrEqualTo(0).When(query => query.AccuracyMeters.HasValue);
	}
	#endregion
}
