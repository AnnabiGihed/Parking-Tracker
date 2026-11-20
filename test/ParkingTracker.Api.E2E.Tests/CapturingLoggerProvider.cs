using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ParkingTracker.Api.E2E.Tests;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Keeps every formatted log line of a test host, so a test can check what the API logged (a reason code) and what it
///              must never log (a token, a subject, a nonce).
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
	#region Properties
	/// <summary>
	/// Gets the lines logged so far, formatted with their level and category.
	/// </summary>
	public ConcurrentQueue<string> Lines { get; } = new();
	#endregion

	#region ILoggerProvider Implementation
	/// <inheritdoc />
	public ILogger CreateLogger(string categoryName)
	{
		return new CapturingLogger(categoryName, Lines);
	}

	/// <inheritdoc />
	public void Dispose()
	{
	}
	#endregion

	#region Nested Types
	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : Writes the lines of one category into the shared queue.
	/// </summary>
	private sealed class CapturingLogger : ILogger
	{
		#region Fields
		/// <summary>
		/// The category of the logger.
		/// </summary>
		private readonly string category;

		/// <summary>
		/// The shared queue.
		/// </summary>
		private readonly ConcurrentQueue<string> lines;
		#endregion

		#region Constructors
		/// <summary>
		/// Initializes a new instance of the <see cref="CapturingLogger"/> class.
		/// </summary>
		/// <param name="category">The category.</param>
		/// <param name="lines">The shared queue.</param>
		public CapturingLogger(string category, ConcurrentQueue<string> lines)
		{
			this.category = category;
			this.lines = lines;
		}
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
			lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)}{exception}");
		}
		#endregion
	}
	#endregion
}
