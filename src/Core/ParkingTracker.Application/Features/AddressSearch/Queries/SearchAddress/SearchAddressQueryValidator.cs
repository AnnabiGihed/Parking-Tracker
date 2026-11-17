using FluentValidation;

namespace ParkingTracker.Application.Features.AddressSearch.Queries.SearchAddress;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Rejects a <see cref="SearchAddressQuery"/> that is blank or too long to be an address.
/// </summary>
public sealed class SearchAddressQueryValidator : AbstractValidator<SearchAddressQuery>
{
	#region Constants
	/// <summary>
	/// The longest text accepted, in characters.
	/// </summary>
	public const int MaxLength = 200;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SearchAddressQueryValidator"/> class.
	/// </summary>
	public SearchAddressQueryValidator()
	{
		RuleFor(query => query.Text)
			.Must(text => !string.IsNullOrWhiteSpace(text))
			.Must(text => text is null || text.Trim().Length <= MaxLength);
	}
	#endregion
}
