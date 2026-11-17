using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Domain.Features.Cards.ValueObjects;

namespace ParkingTracker.Infrastructure.Tests.Features.SpotDecisions.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A settings store that holds one card, for tests.
/// </summary>
public sealed class StaticSettingsStore : ISettingsStore
{
	#region Fields
	/// <summary>
	/// The card the store returns.
	/// </summary>
	private readonly ParkingCard? card;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="StaticSettingsStore"/> class.
	/// </summary>
	/// <param name="card">The saved card, or <see langword="null"/> for none.</param>
	public StaticSettingsStore(ParkingCard? card)
	{
		this.card = card;
	}
	#endregion

	#region ISettingsStore Implementation
	/// <inheritdoc />
	public ParkingCard? GetCard() => card;
	#endregion
}
