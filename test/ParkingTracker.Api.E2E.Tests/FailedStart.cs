namespace ParkingTracker.Api.E2E.Tests;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Starts an API that is expected to refuse to start, and lets the refusal of the API reach the test. The test host
///              of ASP.NET Core resolves a service of the host after the entry point of the API has started it on another thread;
///              when the start fails fast, the entry point has already disposed the host, and the test host throws an
///              <see cref="ObjectDisposedException"/> instead of the refusal. Such an attempt is repeated with a new factory.
/// </summary>
internal static class FailedStart
{
	#region Constants
	/// <summary>
	/// The number of attempts before the disposed host is reported as it is.
	/// </summary>
	private const int MaxAttempts = 5;
	#endregion

	#region Public Methods
	/// <summary>
	/// Starts the API of a new factory and disposes the factory, repeating the start while the test host loses the refusal
	/// to a disposed host.
	/// </summary>
	/// <param name="createFactory">Creates the factory of one attempt.</param>
	public static void Start(Func<ApiFactory> createFactory)
	{
		var attempt = 1;
		while (!TryStart(createFactory, attempt))
		{
			attempt++;
		}
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Makes one attempt to start the API of a new factory.
	/// </summary>
	/// <param name="createFactory">Creates the factory of the attempt.</param>
	/// <param name="attempt">The number of the attempt, from one.</param>
	/// <returns><see langword="true"/> when the API started; <see langword="false"/> when the test host lost the refusal and another attempt may follow.</returns>
	private static bool TryStart(Func<ApiFactory> createFactory, int attempt)
	{
		using var factory = createFactory();
		try
		{
			using var client = factory.CreateClient();
			return true;
		}
		catch (ObjectDisposedException) when (attempt < MaxAttempts)
		{
			// The race described in the type summary hid the refusal; the next attempt starts a new host.
			return false;
		}
	}
	#endregion
}
