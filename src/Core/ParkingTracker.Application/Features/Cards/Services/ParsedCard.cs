namespace ParkingTracker.Application.Features.Cards.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What <see cref="CardTextParser"/> found on a card photo. Only the fields that were found are set; the others stay
///              <see langword="null"/> or empty, so that the form keeps what the user already typed for them.
/// </summary>
/// <param name="Number">The card number, for example <c>B000-123-456</c>, or <see langword="null"/>.</param>
/// <param name="Plate">The number plate, for example <c>1ABC234</c>, or <see langword="null"/>.</param>
/// <param name="ValidFrom">The first day of validity, or <see langword="null"/>; set together with <paramref name="ValidTo"/>.</param>
/// <param name="ValidTo">The last day of validity, or <see langword="null"/>.</param>
/// <param name="MunicipalityId">The identifier of the municipality that issued the card, or <see langword="null"/>.</param>
/// <param name="SectorId">The identifier of the City of Brussels sector, or <see langword="null"/>.</param>
/// <param name="Tolerances">The codes of the tolerance agreements printed on the card; empty when none was found.</param>
public sealed record ParsedCard(
	string? Number,
	string? Plate,
	DateOnly? ValidFrom,
	DateOnly? ValidTo,
	string? MunicipalityId,
	string? SectorId,
	IReadOnlyList<string> Tolerances)
{
	#region Properties
	/// <summary>
	/// Gets the names of the fields that were found, in the order the first version of the app listed them: <c>number</c>,
	/// <c>from</c> and <c>to</c>, <c>plate</c>, <c>municipality</c>, <c>sector</c>, <c>tolerances</c>.
	/// </summary>
	public IReadOnlyList<string> FoundFields
	{
		get
		{
			var found = new List<string>();
			if (Number is not null)
			{
				found.Add("number");
			}

			if (ValidFrom is not null)
			{
				found.Add("from");
			}

			if (ValidTo is not null)
			{
				found.Add("to");
			}

			if (Plate is not null)
			{
				found.Add("plate");
			}

			if (MunicipalityId is not null)
			{
				found.Add("municipality");
			}

			if (SectorId is not null)
			{
				found.Add("sector");
			}

			if (Tolerances.Count > 0)
			{
				found.Add("tolerances");
			}

			return found;
		}
	}
	#endregion
}
