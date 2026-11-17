using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.Cards.ValueObjects;
using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.Domain.Features.MapLayers.ValueObjects;

namespace ParkingTracker.ViewModels.Features.Shared.Settings;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The <see cref="ISettingsStore"/> of the app (ADR-0004): the card goes to the encrypted storage because it holds
///              personal data, every other value to the plain preferences, all as the JSON of <see cref="SettingsJson"/>. The card is
///              kept in memory after the first read, because every spot check asks for it and decrypting is slow; the phone's storage
///              is reached only through <see cref="IKeyValueStorage"/>, so this class runs in tests without a phone.
/// </summary>
public sealed class SettingsStore : ISettingsStore
{
	#region Fields
	/// <summary>
	/// The encrypted storage that holds the card.
	/// </summary>
	private readonly IKeyValueStorage secure;

	/// <summary>
	/// The plain storage that holds everything else.
	/// </summary>
	private readonly IKeyValueStorage plain;

	/// <summary>
	/// Guards <see cref="cardTask"/>; the view models and the tracker read the card from different threads.
	/// </summary>
	private readonly Lock gate = new();

	/// <summary>
	/// The read or saved card, kept after the first read; <see langword="null"/> while nothing was read yet or after a failed read.
	/// </summary>
	private Task<ParkingCard?>? cardTask;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SettingsStore"/> class.
	/// </summary>
	/// <param name="secure">The encrypted storage that holds the card.</param>
	/// <param name="plain">The plain storage that holds everything else.</param>
	public SettingsStore(IKeyValueStorage secure, IKeyValueStorage plain)
	{
		ArgumentNullException.ThrowIfNull(secure);
		ArgumentNullException.ThrowIfNull(plain);
		this.secure = secure;
		this.plain = plain;
	}
	#endregion

	#region ISettingsStore Implementation
	/// <inheritdoc />
	public async Task<ParkingCard?> GetCardAsync()
	{
		Task<ParkingCard?> load;
		lock (gate)
		{
			load = cardTask ??= LoadCardAsync();
		}

		try
		{
			return await load.ConfigureAwait(false);
		}
		catch (Exception)
		{
			// A failed read must not be remembered: the next check reads again.
			lock (gate)
			{
				if (ReferenceEquals(cardTask, load))
				{
					cardTask = null;
				}
			}

			throw;
		}
	}

	/// <inheritdoc />
	public async Task SaveCardAsync(ParkingCard card)
	{
		ArgumentNullException.ThrowIfNull(card);
		var json = SettingsJson.SerializeCard(card);
		await secure.WriteAsync(SettingsJson.CardKey, json).ConfigureAwait(false);

		// Read back through the parser, so that the cache holds exactly what the next start would read.
		var saved = SettingsJson.ParseCard(json);
		lock (gate)
		{
			cardTask = Task.FromResult(saved);
		}
	}

	/// <inheritdoc />
	public async Task ClearCardAsync()
	{
		await secure.DeleteAsync(SettingsJson.CardKey).ConfigureAwait(false);
		lock (gate)
		{
			cardTask = Task.FromResult<ParkingCard?>(null);
		}
	}

	/// <inheritdoc />
	public async Task<ParkedCar?> GetCarAsync() => SettingsJson.ParseCar(await plain.ReadAsync(SettingsJson.CarKey).ConfigureAwait(false));

	/// <inheritdoc />
	public Task SaveCarAsync(ParkedCar car)
	{
		ArgumentNullException.ThrowIfNull(car);
		return plain.WriteAsync(SettingsJson.CarKey, SettingsJson.SerializeCar(car));
	}

	/// <inheritdoc />
	public Task ClearCarAsync() => plain.DeleteAsync(SettingsJson.CarKey);

	/// <inheritdoc />
	public async Task<ParkingTimer?> GetTimerAsync() => SettingsJson.ParseTimer(await plain.ReadAsync(SettingsJson.TimerKey).ConfigureAwait(false));

	/// <inheritdoc />
	public Task SaveTimerAsync(ParkingTimer timer)
	{
		ArgumentNullException.ThrowIfNull(timer);
		return plain.WriteAsync(SettingsJson.TimerKey, SettingsJson.SerializeTimer(timer));
	}

	/// <inheritdoc />
	public Task ClearTimerAsync() => plain.DeleteAsync(SettingsJson.TimerKey);

	/// <inheritdoc />
	public async Task<bool> GetAlertsEnabledAsync() => SettingsJson.ParseAlertsEnabled(await plain.ReadAsync(SettingsJson.AlertsKey).ConfigureAwait(false));

	/// <inheritdoc />
	public Task SaveAlertsEnabledAsync(bool enabled) => plain.WriteAsync(SettingsJson.AlertsKey, SettingsJson.SerializeAlertsEnabled(enabled));

	/// <inheritdoc />
	public async Task<LayerSettings> GetLayersAsync() => SettingsJson.ParseLayers(await plain.ReadAsync(SettingsJson.LayersKey).ConfigureAwait(false));

	/// <inheritdoc />
	public Task SaveLayersAsync(LayerSettings layers)
	{
		ArgumentNullException.ThrowIfNull(layers);
		return plain.WriteAsync(SettingsJson.LayersKey, SettingsJson.SerializeLayers(layers));
	}

	/// <inheritdoc />
	public async Task<bool> GetWelcomeSeenAsync() => string.Equals(await plain.ReadAsync(SettingsJson.WelcomeSeenKey).ConfigureAwait(false), "true", StringComparison.Ordinal);

	/// <inheritdoc />
	public Task SaveWelcomeSeenAsync() => plain.WriteAsync(SettingsJson.WelcomeSeenKey, "true");

	/// <inheritdoc />
	public async Task<DateTimeOffset?> GetPermissionSnoozeUntilAsync() => SettingsJson.ParsePermissionSnooze(await plain.ReadAsync(SettingsJson.PermissionSnoozeKey).ConfigureAwait(false));

	/// <inheritdoc />
	public Task SavePermissionSnoozeUntilAsync(DateTimeOffset until) => plain.WriteAsync(SettingsJson.PermissionSnoozeKey, SettingsJson.SerializePermissionSnooze(until));
	#endregion

	#region Private Helpers
	/// <summary>
	/// Reads the card from the encrypted storage.
	/// </summary>
	/// <returns>A task with the card, or <see langword="null"/> when none is saved.</returns>
	private async Task<ParkingCard?> LoadCardAsync()
	{
		return SettingsJson.ParseCard(await secure.ReadAsync(SettingsJson.CardKey).ConfigureAwait(false));
	}
	#endregion
}
