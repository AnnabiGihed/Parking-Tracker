namespace ParkingTracker.Infrastructure.Tests.Sources;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A test that calls the real public services. It is skipped unless the environment variable
///              <c>PARKING_LIVE_TESTS</c> is <c>1</c>, so that the default test run never depends on the network.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveFactAttribute : FactAttribute
{
	#region Constants
	/// <summary>
	/// The name of the environment variable that switches the live smoke tests on.
	/// </summary>
	public const string SwitchName = "PARKING_LIVE_TESTS";
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="LiveFactAttribute"/> class.
	/// </summary>
	public LiveFactAttribute()
	{
		if (Environment.GetEnvironmentVariable(SwitchName) != "1")
		{
			Skip = $"Live smoke test: set {SwitchName}=1 to call the real services.";
		}
	}
	#endregion
}
