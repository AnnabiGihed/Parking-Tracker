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
	public Task<ParkingCard?> GetCardAsync() => Task.FromResult(card);

	/// <inheritdoc />
	public Task SaveCardAsync(ParkingCard card) => throw new NotSupportedException("This store is read-only.");

	/// <inheritdoc />
	public Task ClearCardAsync() => throw new NotSupportedException("This store is read-only.");

	/// <inheritdoc />
	public Task<ParkedCar?> GetCarAsync() => Task.FromResult<ParkedCar?>(null);

	/// <inheritdoc />
	public Task SaveCarAsync(ParkedCar car) => throw new NotSupportedException("This store is read-only.");

	/// <inheritdoc />
	public Task ClearCarAsync() => throw new NotSupportedException("This store is read-only.");

	/// <inheritdoc />
	public Task<ParkingTimer?> GetTimerAsync() => Task.FromResult<ParkingTimer?>(null);

	/// <inheritdoc />
	public Task SaveTimerAsync(ParkingTimer timer) => throw new NotSupportedException("This store is read-only.");

	/// <inheritdoc />
	public Task ClearTimerAsync() => throw new NotSupportedException("This store is read-only.");

	/// <inheritdoc />
	public Task<bool> GetAlertsEnabledAsync() => Task.FromResult(true);

	/// <inheritdoc />
	public Task SaveAlertsEnabledAsync(bool enabled) => throw new NotSupportedException("This store is read-only.");

	/// <inheritdoc />
	public Task<LayerSettings> GetLayersAsync() => Task.FromResult(new LayerSettings());

	/// <inheritdoc />
	public Task SaveLayersAsync(LayerSettings layers) => throw new NotSupportedException("This store is read-only.");

	/// <inheritdoc />
	public Task<bool> GetWelcomeSeenAsync() => Task.FromResult(true);

	/// <inheritdoc />
	public Task SaveWelcomeSeenAsync() => throw new NotSupportedException("This store is read-only.");

	/// <inheritdoc />
	public Task<DateTimeOffset?> GetPermissionSnoozeUntilAsync() => Task.FromResult<DateTimeOffset?>(null);

	/// <inheritdoc />
	public Task SavePermissionSnoozeUntilAsync(DateTimeOffset until) => throw new NotSupportedException("This store is read-only.");
	#endregion
}
