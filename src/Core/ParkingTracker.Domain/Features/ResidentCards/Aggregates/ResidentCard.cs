using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.ResidentCards.Errors;
using ParkingTracker.Domain.Features.ResidentCards.ValueObjects;
using ParkingTracker.Domain.Features.Sectors.ValueObjects;
using Pivot.Framework.Domain.Primitives;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Features.ResidentCards.Aggregates;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The residents' card of the user: where it is valid, for how long and who holds it. The card enforces what the
///              municipality allows (a sector only for street lists, areas only with the municipality's prefixes, border zones only
///              from its list) and bridges to the <see cref="ParkingCard"/> record the decision engine and the screens read.
/// </summary>
public sealed class ResidentCard : LightweightAggregateRoot<ResidentCardId>
{
	#region Properties
	/// <summary>
	/// Gets the municipality that issued the card.
	/// </summary>
	public MunicipalityCode MunicipalityCode { get; private set; }

	/// <summary>
	/// Gets the City of Brussels sector, or <see langword="null"/> for other municipalities.
	/// </summary>
	public SectorCode? SectorCode { get; private set; }

	/// <summary>
	/// Gets the validity dates.
	/// </summary>
	public CardPeriod Period { get; private set; }

	/// <summary>
	/// Gets the codes of the tolerance agreements printed on the card.
	/// </summary>
	public IReadOnlyList<string> Tolerances { get; }

	/// <summary>
	/// Gets the zone codes of the areas (mailles) printed on the card.
	/// </summary>
	public IReadOnlyList<string> Mailles { get; }

	/// <summary>
	/// Gets the zone codes of the border zones ("limitrophes") on the card.
	/// </summary>
	public IReadOnlyList<string> Lims { get; }

	/// <summary>
	/// Gets the card number, or <see langword="null"/> when none was entered.
	/// </summary>
	public CardNumber? Number { get; private set; }

	/// <summary>
	/// Gets the number plate, or <see langword="null"/> when none was entered.
	/// </summary>
	public LicencePlate? Plate { get; private set; }
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ResidentCard"/> class from a card that passed the checks.
	/// </summary>
	/// <param name="id">The identifier of the card.</param>
	/// <param name="municipalityCode">The municipality of the card.</param>
	/// <param name="card">The checked card whose values the aggregate takes over.</param>
	private ResidentCard(ResidentCardId id, MunicipalityCode municipalityCode, ParkingCard card)
		: base(id)
	{
		MunicipalityCode = municipalityCode;
		SectorCode = ValueObjects.SectorCode.CreateOptional(card.Sector);
		Period = card.Period;
		Tolerances = Copy(card.Tolerances);
		Mailles = Copy(card.Mailles);
		Lims = Copy(card.Lims);
		Number = CardNumber.CreateOptional(card.Number);
		Plate = LicencePlate.CreateOptional(card.Plate);
	}
	#endregion

	#region Factory Methods
	/// <summary>
	/// Creates a card from the values of a <see cref="ParkingCard"/> after checking them against the municipality of the reference
	/// data. The number is trimmed and the plate put in capitals without spaces or dashes.
	/// </summary>
	/// <param name="municipality">The municipality of the reference data that the card names.</param>
	/// <param name="card">The card as typed or read from the saved settings.</param>
	/// <returns>The card, or the first rule it breaks.</returns>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public static Result<ResidentCard> Create(Municipality municipality, ParkingCard card)
	{
		ArgumentNullException.ThrowIfNull(municipality);
		ArgumentNullException.ThrowIfNull(card);
		var code = ValueObjects.MunicipalityCode.Create(card.Municipality);
		if (code.IsFailure)
		{
			return Result.Failure<ResidentCard>(code.Error, code.ResultExceptionType);
		}

		var checks = CheckAgainst(municipality, code.Value, card);
		return checks.IsFailure
			? Result.Failure<ResidentCard>(checks.Error, checks.ResultExceptionType)
			: new ResidentCard(ResidentCardId.New(), code.Value, card);
	}

	/// <summary>
	/// Rebuilds a card that was stored before, with its stored identifier and the same checks as <see cref="Create"/>. It raises
	/// no event: the card existed already.
	/// </summary>
	/// <param name="id">The stored identifier of the card.</param>
	/// <param name="municipality">The municipality of the reference data that the card names.</param>
	/// <param name="card">The stored values.</param>
	/// <returns>The card, or the first rule the stored values break.</returns>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public static Result<ResidentCard> Restore(ResidentCardId id, Municipality municipality, ParkingCard card)
	{
		ArgumentNullException.ThrowIfNull(id);
		ArgumentNullException.ThrowIfNull(municipality);
		ArgumentNullException.ThrowIfNull(card);
		var code = ValueObjects.MunicipalityCode.Create(card.Municipality);
		if (code.IsFailure)
		{
			return Result.Failure<ResidentCard>(code.Error, code.ResultExceptionType);
		}

		var checks = CheckAgainst(municipality, code.Value, card);
		return checks.IsFailure
			? Result.Failure<ResidentCard>(checks.Error, checks.ResultExceptionType)
			: new ResidentCard(id, code.Value, card);
	}
	#endregion

	#region Domain Behaviours
	/// <summary>
	/// Replaces the validity dates, as when the City renews the card.
	/// </summary>
	/// <param name="period">The new dates.</param>
	/// <returns>Success, or a failure when the card would be valid from a day after the day it is valid until.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="period"/> is <see langword="null"/>.</exception>
	public Result Renew(CardPeriod period)
	{
		ArgumentNullException.ThrowIfNull(period);
		var check = CheckPeriod(period);
		if (check.IsFailure)
		{
			return check;
		}

		Period = period;
		return Result.Success();
	}

	/// <summary>
	/// Gives the card in the shape that the decision engine, the settings store and the screens use.
	/// </summary>
	/// <returns>A <see cref="ParkingCard"/> with the same values; a number or plate that was not entered is <see langword="null"/>.</returns>
	public ParkingCard ToParkingCard()
	{
		return new ParkingCard(MunicipalityCode.Value, SectorCode?.Value, Period, Tolerances, Mailles, Lims, Number?.Value, Plate?.Value);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Checks a card against the municipality it names, stopping at the first broken rule.
	/// </summary>
	/// <param name="municipality">The municipality of the reference data.</param>
	/// <param name="code">The municipality the card names.</param>
	/// <param name="card">The card to check.</param>
	/// <returns>Success, or the first broken rule.</returns>
	private static Result CheckAgainst(Municipality municipality, MunicipalityCode code, ParkingCard card)
	{
		if (!string.Equals(code.Value, municipality.Id, StringComparison.Ordinal))
		{
			return Result.Failure(ResidentCardErrors.MunicipalityMismatch);
		}

		var period = CheckPeriod(card.Period);
		if (period.IsFailure)
		{
			return period;
		}

		if (!string.IsNullOrWhiteSpace(card.Sector) && municipality.Kind != MunicipalityKind.Streets)
		{
			return Result.Failure(ResidentCardErrors.SectorNotAllowed);
		}

		var mailles = CheckMailles(municipality, card.Mailles);
		return mailles.IsFailure ? mailles : CheckLims(municipality, card.Lims);
	}

	/// <summary>
	/// Checks that a period does not start after it ends; a missing date is not checked.
	/// </summary>
	/// <param name="period">The period to check.</param>
	/// <returns>Success, or the failure for reversed dates.</returns>
	private static Result CheckPeriod(CardPeriod period)
	{
		return period.ValidFrom is { } from && period.ValidTo is { } to && from > to
			? Result.Failure(ResidentCardErrors.PeriodReversed)
			: Result.Success();
	}

	/// <summary>
	/// Checks that areas are only set for a municipality that issues cards per area, and that each one starts with one of its prefixes.
	/// </summary>
	/// <param name="municipality">The municipality of the reference data.</param>
	/// <param name="mailles">The zone codes on the card.</param>
	/// <returns>Success, or the failure for the first area that does not belong.</returns>
	private static Result CheckMailles(Municipality municipality, IReadOnlyList<string> mailles)
	{
		if (mailles.Count > 0 && municipality.Kind != MunicipalityKind.Mailles)
		{
			return Result.Failure(ResidentCardErrors.MaillesNotAllowed);
		}

		var stranger = mailles.FirstOrDefault(zoneCode => !municipality.Prefixes.Any(prefix => IsInArea(zoneCode, prefix)));
		return stranger is null ? Result.Success() : Result.Failure(ResidentCardErrors.MailleOutsideMunicipality(stranger));
	}

	/// <summary>
	/// Checks that every border zone is one the municipality lists.
	/// </summary>
	/// <param name="municipality">The municipality of the reference data.</param>
	/// <param name="lims">The zone codes on the card.</param>
	/// <returns>Success, or the failure for the first border zone that is not listed.</returns>
	private static Result CheckLims(Municipality municipality, IReadOnlyList<string> lims)
	{
		var stranger = lims.FirstOrDefault(zoneCode => !municipality.Lims.Contains(zoneCode, StringComparer.Ordinal));
		return stranger is null ? Result.Success() : Result.Failure(ResidentCardErrors.LimNotListed(stranger));
	}

	/// <summary>
	/// Tells whether a zone code is the prefix itself or continues it after an underscore, as parking.brussels writes the codes.
	/// </summary>
	/// <param name="zoneCode">The zone code of an area.</param>
	/// <param name="prefix">A zone-code prefix of the municipality.</param>
	/// <returns><see langword="true"/> when the area belongs to the prefix.</returns>
	private static bool IsInArea(string zoneCode, string prefix)
	{
		return string.Equals(zoneCode, prefix, StringComparison.Ordinal) || zoneCode.StartsWith(prefix + "_", StringComparison.Ordinal);
	}

	/// <summary>
	/// Copies codes into a list that nobody else can change.
	/// </summary>
	/// <param name="codes">The codes to keep.</param>
	/// <returns>A read-only copy in the same order.</returns>
	private static System.Collections.ObjectModel.ReadOnlyCollection<string> Copy(IReadOnlyList<string> codes) => Array.AsReadOnly([.. codes]);
	#endregion
}
