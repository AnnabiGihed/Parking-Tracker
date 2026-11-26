namespace ParkingTracker.ApiService.Features.RateLimiting;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The names and limits of the rate limits, in one place: the health policy, the per-address policy of the session endpoints,
///              the per-address policy of the refresh endpoint, the per-account policy of the authenticated endpoints and the per-account policy of
///              the profile endpoints.
/// </summary>
public static class RateLimitSettings
{
	#region Constants
	/// <summary>
	/// The name of the policy of the health endpoints.
	/// </summary>
	public const string HealthPolicyName = "health";

	/// <summary>
	/// The name of the policy of the session endpoints (challenge and sign-in), per client address.
	/// </summary>
	public const string SessionPolicyName = "session-ip";

	/// <summary>
	/// The name of the policy of the refresh endpoint, per client address; separate from <see cref="SessionPolicyName"/> so that a
	/// device that refreshes often cannot starve the sign-ins.
	/// </summary>
	public const string RefreshPolicyName = "refresh-ip";

	/// <summary>
	/// The name of the policy of the authenticated endpoints, per account.
	/// </summary>
	public const string AccountPolicyName = "account";

	/// <summary>
	/// The name of the policy of the profile endpoints, per account.
	/// </summary>
	public const string ProfilePolicyName = "profile";

	/// <summary>
	/// The number of requests the whole API handles at once; more are refused, none waits.
	/// </summary>
	public const int GlobalConcurrencyLimit = 64;

	/// <summary>
	/// The number of health requests one client address may make per window.
	/// </summary>
	public const int HealthPermitsPerWindow = 60;

	/// <summary>
	/// The number of session requests one client address may make per window.
	/// </summary>
	public const int SessionPermitsPerWindow = 10;

	/// <summary>
	/// The number of segments the window of the session policy slides in.
	/// </summary>
	public const int SessionSegmentsPerWindow = 6;

	/// <summary>
	/// The number of refresh requests one client address may make per window.
	/// </summary>
	public const int RefreshPermitsPerWindow = 30;

	/// <summary>
	/// The number of segments the window of the refresh policy slides in.
	/// </summary>
	public const int RefreshSegmentsPerWindow = 6;

	/// <summary>
	/// The most requests one account may make at once: the capacity of its token bucket.
	/// </summary>
	public const int AccountBurst = 30;

	/// <summary>
	/// The requests per minute one account may sustain.
	/// </summary>
	public const int AccountPermitsPerMinute = 120;

	/// <summary>
	/// The seconds to wait that a refusal advises when its limiter gives no hint, as the global concurrency limiter does.
	/// </summary>
	public const int DefaultRetryAfterSeconds = 1;

	/// <summary>
	/// The tokens an account bucket gets back per replenishment period: the sustained rate per second.
	/// </summary>
	public const int AccountTokensPerPeriod = AccountPermitsPerMinute / 60;

	/// <summary>
	/// The most requests one account may make at once to the profile endpoints: the capacity of its token bucket.
	/// </summary>
	public const int ProfileBurst = 10;

	/// <summary>
	/// The requests per minute one account may sustain on the profile endpoints.
	/// </summary>
	public const int ProfilePermitsPerMinute = 30;

	/// <summary>
	/// The tokens a profile bucket gets back per replenishment period: one.
	/// </summary>
	public const int ProfileTokensPerPeriod = 1;
	#endregion

	#region Fields
	/// <summary>
	/// The window of the health policy.
	/// </summary>
	public static readonly TimeSpan HealthWindow = TimeSpan.FromMinutes(1);

	/// <summary>
	/// The window of the session policy.
	/// </summary>
	public static readonly TimeSpan SessionWindow = TimeSpan.FromMinutes(1);

	/// <summary>
	/// The window of the refresh policy.
	/// </summary>
	public static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(1);

	/// <summary>
	/// The interval at which the bucket of an account is refilled, with the permits of that interval.
	/// </summary>
	public static readonly TimeSpan AccountReplenishmentPeriod = TimeSpan.FromSeconds(1);

	/// <summary>
	/// The interval at which the bucket of a profile policy is refilled by one token: 30 per minute is one every two seconds.
	/// </summary>
	public static readonly TimeSpan ProfileReplenishmentPeriod = TimeSpan.FromSeconds(60.0 / ProfilePermitsPerMinute);
	#endregion
}
