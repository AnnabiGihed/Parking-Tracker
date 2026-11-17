#if TEST_HOOKS
using Microsoft.JSInterop;
using ParkingTracker.ViewModels.Features.Shared.Settings.Legacy;
using ParkingTracker.ViewModels.Features.Shared.TestHooks;

namespace ParkingTracker.App.Features.Shared.TestHooks;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The <c>[JSInvokable]</c> face of <see cref="TestHookService"/>, called through <c>window.ptTest</c> (see
///              <see cref="TestHookScript"/>, which lists the same names). It only forwards; the logic is in the tested service. The
///              csproj leaves this file out of a build made without <c>-p:EnableTestHooks=true</c>.
/// </summary>
public sealed class TestHookBridge
{
	#region Fields
	/// <summary>
	/// The service that does the work.
	/// </summary>
	private readonly TestHookService hooks;

	/// <summary>
	/// The migration of the saved data of the previous version.
	/// </summary>
	private readonly LegacyDataMigration migration;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="TestHookBridge"/> class.
	/// </summary>
	/// <param name="hooks">The service that does the work.</param>
	/// <param name="migration">The migration of the saved data of the previous version.</param>
	/// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
	public TestHookBridge(TestHookService hooks, LegacyDataMigration migration)
	{
		ArgumentNullException.ThrowIfNull(hooks);
		ArgumentNullException.ThrowIfNull(migration);
		this.hooks = hooks;
		this.migration = migration;
	}
	#endregion

	#region Hooks
	/// <summary>
	/// Saves a card given as the JSON of <c>pt.card</c>.
	/// </summary>
	/// <param name="json">The card JSON.</param>
	/// <returns>A task that completes when the card is saved.</returns>
	[JSInvokable("SetCard")]
	public Task SetCard(string json) => hooks.SetCardAsync(json);

	/// <summary>
	/// Reads the saved card as the JSON of <c>pt.card</c>.
	/// </summary>
	/// <returns>A task with the JSON, or <see langword="null"/> when none is saved.</returns>
	[JSInvokable("GetCard")]
	public Task<string?> GetCard() => hooks.GetCardAsync();

	/// <summary>
	/// Decides a place.
	/// </summary>
	/// <param name="latitude">Degrees north.</param>
	/// <param name="longitude">Degrees east.</param>
	/// <param name="accuracyMetres">The accuracy in metres, or <see langword="null"/>.</param>
	/// <returns>The answer.</returns>
	[JSInvokable("Decide")]
	public Task<DecisionAnswer> Decide(double latitude, double longitude, double? accuracyMetres) => hooks.DecideAsync(latitude, longitude, accuracyMetres);

	/// <summary>
	/// Searches the nearest spots where the card works.
	/// </summary>
	/// <param name="latitude">Degrees north.</param>
	/// <param name="longitude">Degrees east.</param>
	/// <param name="count">How many spots at most.</param>
	/// <returns>The spots, nearest first.</returns>
	[JSInvokable("FreeSpots")]
	public Task<IReadOnlyList<FreeSpotAnswer>> FreeSpots(double latitude, double longitude, int count) => hooks.FreeSpotsAsync(latitude, longitude, count);

	/// <summary>
	/// Reads the outcome of the official-list check.
	/// </summary>
	/// <returns>The state and problem.</returns>
	[JSInvokable("OfficialList")]
	public Task<OfficialListAnswer> OfficialList() => hooks.OfficialListAsync();

	/// <summary>
	/// Reads the permissions and the connection.
	/// </summary>
	/// <returns>The platform state.</returns>
	[JSInvokable("PlatformStatus")]
	public Task<PlatformAnswer> PlatformStatus() => hooks.PlatformStatusAsync();

	/// <summary>
	/// Starts a parking timer of the given length in minutes, below the 5-minute minimum of the form.
	/// </summary>
	/// <param name="minutes">The length in minutes, at least 1.</param>
	/// <returns>A task with <see langword="true"/> when the timer started.</returns>
	[JSInvokable("StartTimer")]
	public Task<bool> StartTimer(int minutes) => hooks.StartTimerAsync(minutes);

	/// <summary>
	/// Reads the card fields in a text, as the scan does after the photo was recognised.
	/// </summary>
	/// <param name="text">The text to read.</param>
	/// <returns>The fields that were found.</returns>
	[JSInvokable("ParseCardText")]
	public Task<CardTextAnswer> ParseCardText(string text) => hooks.ParseCardTextAsync(text);

	/// <summary>
	/// Reads the saved parked car as the JSON of <c>pt.car</c>.
	/// </summary>
	/// <returns>A task with the JSON, or <see langword="null"/> when none is saved.</returns>
	[JSInvokable("GetCar")]
	public Task<string?> GetCar() => hooks.GetCarAsync();

	/// <summary>
	/// Reads the marker <c>pt.migration</c> that records the outcome of the migration of the previous version's data.
	/// </summary>
	/// <returns>A task with the JSON of the marker, or <see langword="null"/> when none is saved.</returns>
	[JSInvokable("Migration")]
	public Task<string?> Migration() => migration.ReadMarkerAsync();

	/// <summary>
	/// Reads what the main screen holds: tracking, position, headline and busy state.
	/// </summary>
	/// <returns>The state of the screen.</returns>
	[JSInvokable("ScreenState")]
	public ScreenStateAnswer ScreenState() => hooks.ScreenState();
	#endregion
}
#endif
