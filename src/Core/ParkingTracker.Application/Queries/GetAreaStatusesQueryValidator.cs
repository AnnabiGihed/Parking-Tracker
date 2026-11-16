using FluentValidation;

namespace ParkingTracker.Application.Queries;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Rejects a <see cref="GetAreaStatusesQuery"/> whose edges are out of range or reversed.
/// </summary>
public sealed class GetAreaStatusesQueryValidator : AbstractValidator<GetAreaStatusesQuery>
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="GetAreaStatusesQueryValidator"/> class.
	/// </summary>
	public GetAreaStatusesQueryValidator()
	{
		RuleFor(query => query.South).InclusiveBetween(-90, 90);
		RuleFor(query => query.North).InclusiveBetween(-90, 90).GreaterThanOrEqualTo(query => query.South);
		RuleFor(query => query.West).InclusiveBetween(-180, 180);
		RuleFor(query => query.East).InclusiveBetween(-180, 180).GreaterThanOrEqualTo(query => query.West);
	}
	#endregion
}
