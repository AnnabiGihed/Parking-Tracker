using FluentValidation;

namespace ParkingTracker.Application.Features.Cards.Queries.ParseCardText;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Rejects a <see cref="ParseCardTextQuery"/> without text or with a text too long to be a card. An empty text is
///              valid: it simply holds no field.
/// </summary>
public sealed class ParseCardTextQueryValidator : AbstractValidator<ParseCardTextQuery>
{
	#region Constants
	/// <summary>
	/// The longest text accepted, in characters; a card holds a few hundred.
	/// </summary>
	public const int MaxLength = 20000;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ParseCardTextQueryValidator"/> class.
	/// </summary>
	public ParseCardTextQueryValidator()
	{
		RuleFor(query => query.Text).NotNull().MaximumLength(MaxLength);
	}
	#endregion
}
