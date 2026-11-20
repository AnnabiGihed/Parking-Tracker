namespace ParkingTracker.ViewModels.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The stack behind <see cref="IBackNavigation"/>. It is a singleton, because the host asks it from outside the Blazor
///              scope, and its list is guarded by a lock for the same reason. A close action runs outside the lock, because closing a
///              screen disposes its registration.
/// </summary>
public sealed class BackNavigation : IBackNavigation
{
	#region Fields
	/// <summary>
	/// Guards <see cref="entries"/>.
	/// </summary>
	private readonly Lock gate = new();

	/// <summary>
	/// The registered close actions, the newest last.
	/// </summary>
	private readonly List<Registration> entries = [];
	#endregion

	#region Properties
	/// <inheritdoc />
	public bool CanGoBack
	{
		get
		{
			lock (gate)
			{
				return entries.Count > 0;
			}
		}
	}
	#endregion

	#region IBackNavigation Implementation
	/// <inheritdoc />
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="close"/> is <see langword="null"/>.</exception>
	public IDisposable Register(Action close)
	{
		ArgumentNullException.ThrowIfNull(close);
		var registration = new Registration(this, close);
		lock (gate)
		{
			entries.Add(registration);
		}

		return registration;
	}

	/// <inheritdoc />
	public bool TryGoBack()
	{
		Registration newest;
		lock (gate)
		{
			if (entries.Count == 0)
			{
				return false;
			}

			newest = entries[^1];
			entries.RemoveAt(entries.Count - 1);
		}

		newest.Close();
		return true;
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Removes a registration; removing one that is gone is not an error.
	/// </summary>
	/// <param name="registration">The registration to remove.</param>
	private void Remove(Registration registration)
	{
		lock (gate)
		{
			entries.Remove(registration);
		}
	}
	#endregion

	#region Nested Types
	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : One registered close action; disposing it takes it off the stack.
	/// </summary>
	private sealed class Registration : IDisposable
	{
		#region Fields
		/// <summary>
		/// The stack the registration belongs to.
		/// </summary>
		private readonly BackNavigation owner;
		#endregion

		#region Constructors
		/// <summary>
		/// Initializes a new instance of the <see cref="Registration"/> class.
		/// </summary>
		/// <param name="owner">The stack the registration belongs to.</param>
		/// <param name="close">The action that closes the screen.</param>
		internal Registration(BackNavigation owner, Action close)
		{
			this.owner = owner;
			Close = close;
		}
		#endregion

		#region Properties
		/// <summary>
		/// Gets the action that closes the screen.
		/// </summary>
		internal Action Close { get; }
		#endregion

		#region IDisposable Implementation
		/// <inheritdoc />
		public void Dispose() => owner.Remove(this);
		#endregion
	}
	#endregion
}
