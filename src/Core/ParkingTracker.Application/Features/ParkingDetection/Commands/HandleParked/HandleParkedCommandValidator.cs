using FluentValidation;

namespace ParkingTracker.Application.Features.ParkingDetection.Commands.HandleParked;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Rejects a <see cref="HandleParkedCommand"/> whose coordinates are out of range or whose accuracy is negative.
/// </summary>
public sealed class HandleParkedCommandValidator : AbstractValidator<HandleParkedCommand>
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="HandleParkedCommandValidator"/> class.
	/// </summary>
	public HandleParkedCommandValidator()
	{
		RuleFor(command => command.Latitude).InclusiveBetween(-90, 90);
		RuleFor(command => command.Longitude).InclusiveBetween(-180, 180);
		RuleFor(command => command.AccuracyMeters).GreaterThanOrEqualTo(0).When(command => command.AccuracyMeters.HasValue);
	}
	#endregion
}
