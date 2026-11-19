namespace ParkingTracker.ServiceDefaults.Features.Health;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The names of the two health endpoints and of the tag that selects the checks of the readiness endpoint. They are
///              shared by the defaults that map the endpoints and by the hosts that register checks, so the two cannot drift apart.
///              The reverse proxy and the uptime monitor call these paths, so they are a contract with the deployment.
/// </summary>
public static class HealthEndpoints
{
	#region Constants
	/// <summary>
	/// The liveness path: it answers while the process runs and runs no check.
	/// </summary>
	public const string LivePath = "/health/live";

	/// <summary>
	/// The readiness path: it runs the checks tagged <see cref="ReadyTag"/>.
	/// </summary>
	public const string ReadyPath = "/health/ready";

	/// <summary>
	/// The tag that puts a health check in the readiness endpoint.
	/// </summary>
	public const string ReadyTag = "ready";
	#endregion
}
