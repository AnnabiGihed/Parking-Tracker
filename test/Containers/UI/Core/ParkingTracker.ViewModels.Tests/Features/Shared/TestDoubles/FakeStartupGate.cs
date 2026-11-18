using ParkingTracker.ViewModels.Features.Shared.Settings;

namespace ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A start-up gate that a test controls: it counts how often it was waited for, can stay unfinished until the test lets it
///              finish (a background reader that runs before the screens), and says whether the one-time move into the database is done.
/// </summary>
internal sealed class FakeStartupGate : IStartupGate
{
	#region Fields
	/// <summary>
	/// The run that callers wait for.
	/// </summary>
	private readonly TaskCompletionSource run = new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>
	/// How many callers waited for the gate.
	/// </summary>
	private int waits;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="FakeStartupGate"/> class, finished by default.
	/// </summary>
	/// <param name="finished">Whether the run is finished from the start.</param>
	public FakeStartupGate(bool finished = true)
	{
		if (finished)
		{
			Finish();
		}
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets or sets a value indicating whether the one-time move into the database has finished.
	/// </summary>
	public bool MoveFinished { get; set; } = true;

	/// <summary>
	/// Gets how many callers waited for the gate.
	/// </summary>
	public int Waits => Volatile.Read(ref waits);
	#endregion

	#region Public Methods
	/// <summary>
	/// Lets the run finish, so that the callers that wait for it continue.
	/// </summary>
	public void Finish() => _ = run.TrySetResult();
	#endregion

	#region IStartupGate Implementation
	/// <inheritdoc />
	public Task RunOnceAsync()
	{
		_ = Interlocked.Increment(ref waits);
		return run.Task;
	}

	/// <inheritdoc />
	public Task<bool> IsMoveFinishedAsync() => Task.FromResult(MoveFinished);
	#endregion
}
