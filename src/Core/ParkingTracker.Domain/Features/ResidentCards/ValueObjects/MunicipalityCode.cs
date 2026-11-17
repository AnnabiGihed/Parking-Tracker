using ParkingTracker.Domain.Features.ResidentCards.Errors;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Features.ResidentCards.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The identifier of the municipality that issued a residents' card, for example <c>saint-gilles</c>. The card checks
///              it against the municipality of the reference data; this type only guarantees that it is present.
/// </summary>
public sealed record MunicipalityCode
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="MunicipalityCode"/> class.
	/// </summary>
	/// <param name="value">The trimmed, non-empty identifier.</param>
	private MunicipalityCode(string value)
	{
		Value = value;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the identifier as the reference data writes it.
	/// </summary>
	public string Value { get; }
	#endregion

	#region Factory Methods
	/// <summary>
	/// Creates a code from the text of a saved or typed card.
	/// </summary>
	/// <param name="text">The identifier; surrounding white space is dropped.</param>
	/// <returns>The code, or a failure when the text is missing or blank.</returns>
	public static Result<MunicipalityCode> Create(string? text)
	{
		return string.IsNullOrWhiteSpace(text)
			? Result.Failure<MunicipalityCode>(ResidentCardErrors.MunicipalityRequired)
			: new MunicipalityCode(text.Trim());
	}
	#endregion
}
