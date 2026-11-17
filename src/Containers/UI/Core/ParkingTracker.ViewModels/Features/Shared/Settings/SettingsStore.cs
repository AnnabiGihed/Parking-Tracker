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
	/// Guards the cached card; the view models and the tracker read the card from different threads.
	/// </summary>
	private readonly Lock gate = new();

	/// <summary>
	/// The card as last read or saved; only meaningful when <see cref="cardLoaded"/> is <see langword="true"/>.
	/// </summary>
	private ParkingCard? cachedCard;

	/// <summary>
	/// Whether <see cref="cachedCard"/> holds what the storage holds.
	/// </summary>
	private bool cardLoaded;
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
	public ParkingCard? GetCard()
	{
		lock (gate)
		{
			if (!cardLoaded)
			{
				cachedCard = SettingsJson.ParseCard(secure.Read(SettingsJson.CardKey));
				cardLoaded = true;
			}

			return cachedCard;
		}
	}

	/// <inheritdoc />
	public void SaveCard(ParkingCard card)
	{
		ArgumentNullException.ThrowIfNull(card);
		var json = SettingsJson.SerializeCard(card);
		lock (gate)
		{
			secure.Write(SettingsJson.CardKey, json);

			// Read back through the parser, so that the cache holds exactly what the next start would read.
			cachedCard = SettingsJson.ParseCard(json);
			cardLoaded = true;
		}
	}

	/// <inheritdoc />
	public void ClearCard()
	{
		lock (gate)
		{
			secure.Delete(SettingsJson.CardKey);
			cachedCard = null;
			cardLoaded = true;
		}
	}

	/// <inheritdoc />
	public ParkedCar? GetCar() => SettingsJson.ParseCar(plain.Read(SettingsJson.CarKey));

	/// <inheritdoc />
	public void SaveCar(ParkedCar car)
	{
		ArgumentNullException.ThrowIfNull(car);
		plain.Write(SettingsJson.CarKey, SettingsJson.SerializeCar(car));
	}

	/// <inheritdoc />
	public void ClearCar() => plain.Delete(SettingsJson.CarKey);

	/// <inheritdoc />
	public LayerSettings GetLayers() => SettingsJson.ParseLayers(plain.Read(SettingsJson.LayersKey));

	/// <inheritdoc />
	public void SaveLayers(LayerSettings layers)
	{
		ArgumentNullException.ThrowIfNull(layers);
		plain.Write(SettingsJson.LayersKey, SettingsJson.SerializeLayers(layers));
	}

	/// <inheritdoc />
	public bool GetWelcomeSeen() => string.Equals(plain.Read(SettingsJson.WelcomeSeenKey), "true", StringComparison.Ordinal);

	/// <inheritdoc />
	public void SaveWelcomeSeen() => plain.Write(SettingsJson.WelcomeSeenKey, "true");

	/// <inheritdoc />
	public DateTimeOffset? GetPermissionSnoozeUntil() => SettingsJson.ParsePermissionSnooze(plain.Read(SettingsJson.PermissionSnoozeKey));

	/// <inheritdoc />
	public void SavePermissionSnoozeUntil(DateTimeOffset until) => plain.Write(SettingsJson.PermissionSnoozeKey, SettingsJson.SerializePermissionSnooze(until));
	#endregion
}
