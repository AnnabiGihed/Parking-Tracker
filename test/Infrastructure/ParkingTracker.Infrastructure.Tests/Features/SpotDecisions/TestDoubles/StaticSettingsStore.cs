using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.MapLayers.ValueObjects;

namespace ParkingTracker.Infrastructure.Tests.Features.SpotDecisions.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A settings store that holds one card and nothing else, for tests that only need the card the decisions read.
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

	/// <inheritdoc />
	public void SaveCard(ParkingCard card) => throw new NotSupportedException("This store is read-only.");

	/// <inheritdoc />
	public void ClearCard() => throw new NotSupportedException("This store is read-only.");

	/// <inheritdoc />
	public ParkedCar? GetCar() => null;

	/// <inheritdoc />
	public void SaveCar(ParkedCar car) => throw new NotSupportedException("This store is read-only.");

	/// <inheritdoc />
	public void ClearCar() => throw new NotSupportedException("This store is read-only.");

	/// <inheritdoc />
	public LayerSettings GetLayers() => new();

	/// <inheritdoc />
	public void SaveLayers(LayerSettings layers) => throw new NotSupportedException("This store is read-only.");

	/// <inheritdoc />
	public bool GetWelcomeSeen() => true;

	/// <inheritdoc />
	public void SaveWelcomeSeen() => throw new NotSupportedException("This store is read-only.");

	/// <inheritdoc />
	public DateTimeOffset? GetPermissionSnoozeUntil() => null;

	/// <inheritdoc />
	public void SavePermissionSnoozeUntil(DateTimeOffset until) => throw new NotSupportedException("This store is read-only.");
	#endregion
}
