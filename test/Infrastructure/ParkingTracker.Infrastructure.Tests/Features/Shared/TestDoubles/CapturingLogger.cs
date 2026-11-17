using Microsoft.Extensions.Logging;

namespace ParkingTracker.Infrastructure.Tests.Features.Shared.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A logger that keeps what it is given, so that a test can check that a warning was written and what it says.
/// </summary>
internal sealed class CapturingLogger : ILogger
{
	#region Properties
	/// <summary>
	/// Gets the entries written so far.
	/// </summary>
	public List<(LogLevel Level, string Message)> Entries { get; } = [];
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
		ArgumentNullException.ThrowIfNull(formatter);
		Entries.Add((logLevel, formatter(state, exception)));
	}
	#endregion
}
