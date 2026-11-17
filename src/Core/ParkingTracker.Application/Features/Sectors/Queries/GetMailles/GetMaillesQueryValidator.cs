using FluentValidation;

namespace ParkingTracker.Application.Features.Sectors.Queries.GetMailles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Rejects a <see cref="GetMaillesQuery"/> without a municipality.
/// </summary>
public sealed class GetMaillesQueryValidator : AbstractValidator<GetMaillesQuery>
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="GetMaillesQueryValidator"/> class.
	/// </summary>
	public GetMaillesQueryValidator()
	{
		RuleFor(query => query.MunicipalityId).NotEmpty();
	}
	#endregion
}
