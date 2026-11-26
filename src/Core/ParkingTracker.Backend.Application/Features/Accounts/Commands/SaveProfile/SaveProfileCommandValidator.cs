using FluentValidation;

namespace ParkingTracker.Backend.Application.Features.Accounts.Commands.SaveProfile;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Rejects a <see cref="SaveProfileCommand"/> whose document is missing or invalid (see <see cref="ProfileDocumentValidator"/>)
///              or whose condition names a revision below 1.
/// </summary>
public sealed class SaveProfileCommandValidator : AbstractValidator<SaveProfileCommand>
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SaveProfileCommandValidator"/> class.
	/// </summary>
	/// <param name="clock">The clock the document is checked against.</param>
	public SaveProfileCommandValidator(TimeProvider clock)
	{
		_ = RuleFor(command => command.Document).NotNull();
		_ = RuleFor(command => command.Document).SetValidator(new ProfileDocumentValidator(clock)).When(command => command.Document is not null);
		_ = RuleFor(command => command.Condition).NotNull();
		_ = RuleFor(command => command.Condition.ExpectedRevision!.Value)
			.GreaterThanOrEqualTo(1)
			.WithName("Condition.ExpectedRevision")
			.When(command => command.Condition?.ExpectedRevision is not null);
	}
	#endregion
}
