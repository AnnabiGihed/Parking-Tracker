using ParkingTracker.Domain.Zones;

namespace ParkingTracker.Domain.Decisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The answer to "may I park here with my card?": a verdict with its headline, an explanation in plain text,
///              notes about reserved spaces and roadworks, and the tariff of the zone. The texts are the English sentences of
///              the first version of the app, without markup.
/// </summary>
/// <param name="Verdict">The headline answer.</param>
/// <param name="Detail">The explanation.</param>
/// <param name="Notes">Extra remarks, most important first; empty when there are none.</param>
/// <param name="Tariff">The tariff of the zone as one line, or <see langword="null"/> when unknown.</param>
/// <param name="Zone">The street surface and rule the answer is based on, or <see langword="null"/> when none was found.</param>
/// <param name="ReservedSpace">The reserved space you are standing on, for <see cref="Verdict.Reserved"/>; otherwise <see langword="null"/>.</param>
public sealed record SpotDecision(
	Verdict Verdict,
	string Detail,
	IReadOnlyList<string> Notes,
	string? Tariff,
	ZoneReading? Zone,
	ReservedSpace? ReservedSpace)
{
	#region Properties
	/// <summary>
	/// Gets the headline shown to the user, for example <c>FREE ✓</c> or <c>PAY</c>.
	/// </summary>
	public string Title => Verdict switch
	{
		Verdict.Free => "FREE ✓",
		Verdict.FreeNow => "FREE NOW",
		Verdict.Pay => "PAY",
		Verdict.Reserved => "RESERVED",
		Verdict.TimeLimited => "TIME LIMITED",
		Verdict.ParkingDisc => "PARKING DISC",
		Verdict.CheckNumber => "CHECK THE NUMBER",
		Verdict.CheckCard => "CHECK YOUR CARD",
		Verdict.CheckSigns => "CHECK THE SIGNS",
		_ => "CAN'T VERIFY",
	};

	/// <summary>
	/// Gets the colour of the answer: green for free, red for pay, grey for everything uncertain.
	/// </summary>
	public SpotStatus Status => Verdict switch
	{
		Verdict.Free or Verdict.FreeNow => SpotStatus.Free,
		Verdict.Pay or Verdict.Reserved => SpotStatus.Pay,
		_ => SpotStatus.Unknown,
	};
	#endregion

	#region Factory Methods
	/// <summary>
	/// Creates an answer without notes and tariff.
	/// </summary>
	/// <param name="verdict">The headline answer.</param>
	/// <param name="detail">The explanation.</param>
	/// <param name="zone">The street surface and rule the answer is based on, or <see langword="null"/>.</param>
	/// <returns>The answer.</returns>
	public static SpotDecision Create(Verdict verdict, string detail, ZoneReading? zone = null)
	{
		return new SpotDecision(verdict, detail, [], null, zone, null);
	}

	/// <summary>
	/// Creates the answer "can't verify".
	/// </summary>
	/// <param name="detail">Why the answer could not be confirmed.</param>
	/// <param name="zone">The street surface and rule found so far, or <see langword="null"/>.</param>
	/// <returns>The answer.</returns>
	public static SpotDecision CantVerify(string detail, ZoneReading? zone = null)
	{
		return Create(Verdict.CantVerify, detail, zone);
	}
	#endregion
}
