using Microsoft.Maui.Devices;

namespace ParkingTracker.App.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Plays a vibration pattern on the phone over the MAUI <c>IVibration</c>, which can only vibrate for a given time, so a
///              pattern is played as a series of pulses with pauses in between. A phone without a vibrator is skipped silently.
/// </summary>
public sealed class CoverageVibrator
{
	#region Fields
	/// <summary>
	/// The vibrator of the phone.
	/// </summary>
	private readonly IVibration vibration;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="CoverageVibrator"/> class.
	/// </summary>
	/// <param name="vibration">The vibrator of the phone.</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="vibration"/> is <see langword="null"/>.</exception>
	public CoverageVibrator(IVibration vibration)
	{
		ArgumentNullException.ThrowIfNull(vibration);
		this.vibration = vibration;
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Plays a pattern: the first number is a pulse, the second a pause, and so on.
	/// </summary>
	/// <param name="pattern">Milliseconds that alternate between vibrating and pausing, starting with vibrating.</param>
	/// <returns>A task that completes when the pattern has played.</returns>
	public async Task VibrateAsync(IReadOnlyList<int> pattern)
	{
		ArgumentNullException.ThrowIfNull(pattern);
		try
		{
			for (var index = 0; index < pattern.Count; index++)
			{
				var duration = TimeSpan.FromMilliseconds(pattern[index]);
				if (index % 2 == 0)
				{
					vibration.Vibrate(duration);
				}

				await Task.Delay(duration).ConfigureAwait(false);
			}
		}
		catch (FeatureNotSupportedException)
		{
			// No vibrator on this device: the toast already told the user.
		}
	}
	#endregion
}
