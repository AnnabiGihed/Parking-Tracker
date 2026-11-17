namespace ParkingTracker.ViewModels.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The short message that appears at the top of the screen and goes away by itself, shared by every view model that
///              has something to tell the user. The time it stays comes from the injected clock, so tests do not wait.
/// </summary>
public sealed class ToastViewModel : ViewModelBase, IDisposable
{
	#region Static Instances
	/// <summary>
	/// How long a message stays when the caller does not say.
	/// </summary>
	public static readonly TimeSpan DefaultDuration = TimeSpan.FromMilliseconds(3500);
	#endregion

	#region Fields
	/// <summary>
	/// Clock that times the message.
	/// </summary>
	private readonly TimeProvider clock;

	/// <summary>
	/// The timer that hides the current message.
	/// </summary>
	private ITimer? timer;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ToastViewModel"/> class.
	/// </summary>
	/// <param name="clock">Clock that times the message.</param>
	public ToastViewModel(TimeProvider clock)
	{
		this.clock = clock;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the text of the current message; empty when none was shown yet.
	/// </summary>
	public string Message { get; private set; } = string.Empty;

	/// <summary>
	/// Gets a value indicating whether the message is on screen.
	/// </summary>
	public bool IsVisible { get; private set; }
	#endregion

	#region Public Methods
	/// <summary>
	/// Shows a message, replacing the one on screen, and hides it after a time.
	/// </summary>
	/// <param name="message">The text to show.</param>
	/// <param name="duration">How long it stays, or <see langword="null"/> for <see cref="DefaultDuration"/>.</param>
	public void Show(string message, TimeSpan? duration = null)
	{
		Message = message;
		IsVisible = true;
		timer?.Dispose();
		timer = clock.CreateTimer(_ => Hide(), null, duration ?? DefaultDuration, Timeout.InfiniteTimeSpan);
		NotifyChanged();
	}

	/// <summary>
	/// Hides the message.
	/// </summary>
	public void Hide()
	{
		IsVisible = false;
		NotifyChanged();
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		timer?.Dispose();
	}
	#endregion
}
