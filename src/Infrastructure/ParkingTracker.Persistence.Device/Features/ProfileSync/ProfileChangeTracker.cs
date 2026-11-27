using ParkingTracker.Application.Features.ProfileSync.Abstractions;
using ParkingTracker.Application.Features.Shared.Abstractions;

namespace ParkingTracker.Persistence.Device.Features.ProfileSync;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Records the changes the user makes to the synced sections (ADR-0032): it stages the moment of the change (from the
///              clock, to the millisecond, the precision the database keeps) and the changed flag in the scope of the data change, and
///              raises <see cref="Changed"/> once the store committed it.
/// </summary>
public sealed class ProfileChangeTracker : IProfileChangeTracker
{
	#region Fields
	/// <summary>
	/// Stages the writes to the sync state.
	/// </summary>
	private readonly ISyncStateRepository state;

	/// <summary>
	/// The clock that gives the moment of a change.
	/// </summary>
	private readonly TimeProvider clock;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ProfileChangeTracker"/> class.
	/// </summary>
	/// <param name="state">Stages the writes to the sync state.</param>
	/// <param name="clock">The clock that gives the moment of a change.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public ProfileChangeTracker(ISyncStateRepository state, TimeProvider clock)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(clock);
		this.state = state;
		this.clock = clock;
	}
	#endregion

	#region Events
	/// <inheritdoc />
	public event EventHandler? Changed;
	#endregion

	#region IProfileChangeTracker Implementation
	/// <inheritdoc />
	public void Stage(IDeviceChangeScope scope, SyncSection section)
	{
		ArgumentNullException.ThrowIfNull(scope);
		state.StageTouch(scope, section, DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds()));
	}

	/// <inheritdoc />
	public void NotifyCommitted() => Changed?.Invoke(this, EventArgs.Empty);
	#endregion
}
