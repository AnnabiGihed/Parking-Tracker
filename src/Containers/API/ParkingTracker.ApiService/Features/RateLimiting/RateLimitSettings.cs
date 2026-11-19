namespace ParkingTracker.ApiService.Features.RateLimiting;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The names and limits of the rate limits, in one place. The session and per-account policies of later stages join here.
/// </summary>
public static class RateLimitSettings
{
	#region Constants
	/// <summary>
	/// The name of the policy of the health endpoints.
	/// </summary>
	public const string HealthPolicyName = "health";

	/// <summary>
	/// The number of requests the whole API handles at once; more are refused, none waits.
	/// </summary>
	public const int GlobalConcurrencyLimit = 64;

	/// <summary>
	/// The number of health requests one client address may make per window.
	/// </summary>
	public const int HealthPermitsPerWindow = 60;

	/// <summary>
	/// The seconds to wait that a refusal advises when its limiter gives no hint, as the global concurrency limiter does.
	/// </summary>
	public const int DefaultRetryAfterSeconds = 1;
	#endregion

	#region Fields
	/// <summary>
	/// The window of the health policy.
	/// </summary>
	public static readonly TimeSpan HealthWindow = TimeSpan.FromMinutes(1);
	#endregion
}
