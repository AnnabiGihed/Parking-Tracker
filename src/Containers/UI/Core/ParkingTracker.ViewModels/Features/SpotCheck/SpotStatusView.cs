namespace ParkingTracker.ViewModels.Features.SpotCheck;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What the status block of the bottom sheet shows for one answer: the headline, the explanation, the colour,
///              the notes, the tariff and which actions are offered. The texts are plain; the screen escapes them when it renders.
/// </summary>
/// <param name="Title">The headline, for example <c>Paid parking</c>.</param>
/// <param name="IconName">The name of the icon next to the headline.</param>
/// <param name="Detail">The explanation under the headline.</param>
/// <param name="ColourKey">The CSS class of the block: <c>free</c>, <c>pay</c>, <c>unknown</c> or <c>idle</c>.</param>
/// <param name="Notes">Extra remarks, for example roadworks.</param>
/// <param name="Tariff">The tariff text, or <see langword="null"/> when it is not shown.</param>
/// <param name="ShowParkedHere">Whether "I parked here" is offered.</param>
/// <param name="ShowPayApps">Whether the buttons of the payment applications are offered.</param>
/// <param name="IsIdle">Whether this is the invitation shown before any check.</param>
public sealed record SpotStatusView(
	string Title,
	string IconName,
	string Detail,
	string ColourKey,
	IReadOnlyList<string> Notes,
	string? Tariff,
	bool ShowParkedHere,
	bool ShowPayApps,
	bool IsIdle)
{
	#region Static Instances
	/// <summary>
	/// The invitation shown before the first check. Its explanation has a bold part, so the screen writes it itself.
	/// </summary>
	public static readonly SpotStatusView Idle = new("Where do you want to park?", "pin", string.Empty, "idle", [], null, false, false, true);
	#endregion
}
