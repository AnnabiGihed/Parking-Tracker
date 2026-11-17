using FluentValidation;

namespace ParkingTracker.Application.Features.MapLayers.Queries.GetMapLayers;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Rejects a <see cref="GetMapLayersQuery"/> whose edges are out of range or reversed.
/// </summary>
public sealed class GetMapLayersQueryValidator : AbstractValidator<GetMapLayersQuery>
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="GetMapLayersQueryValidator"/> class.
	/// </summary>
	public GetMapLayersQueryValidator()
	{
		RuleFor(query => query.South).InclusiveBetween(-90, 90);
		RuleFor(query => query.North).InclusiveBetween(-90, 90).GreaterThanOrEqualTo(query => query.South);
		RuleFor(query => query.West).InclusiveBetween(-180, 180);
		RuleFor(query => query.East).InclusiveBetween(-180, 180).GreaterThanOrEqualTo(query => query.West);
		RuleFor(query => query.Layers).NotNull();
	}
	#endregion
}
