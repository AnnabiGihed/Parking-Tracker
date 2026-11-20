using Microsoft.Extensions.Logging;

namespace ParkingTracker.Backend.Application.Tests;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A logger that keeps the formatted lines it is given, so a test can check what a handler logged and what it must not.
/// </summary>
/// <typeparam name="T">The category of the logger.</typeparam>
public sealed class ListLogger<T> : ILogger<T>
{
	#region Properties
	/// <summary>
	/// Gets the lines logged so far.
	/// </summary>
	public IList<string> Lines { get; } = new List<string>();
	#endregion

	#region ILogger Implementation
	/// <inheritdoc />
	public IDisposable? BeginScope<TState>(TState state)
		where TState : notnull
	{
		return null;
	}

	/// <inheritdoc />
	public bool IsEnabled(LogLevel logLevel) => true;

	/// <inheritdoc />
	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		Lines.Add($"{logLevel}: {formatter(state, exception)}");
	}
	#endregion
}
