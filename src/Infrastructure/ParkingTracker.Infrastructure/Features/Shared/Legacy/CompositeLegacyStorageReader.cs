using Microsoft.Extensions.Logging;
using ParkingTracker.Application.Features.Shared.Abstractions;

namespace ParkingTracker.Infrastructure.Features.Shared.Legacy;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The <see cref="ILegacyStorageReader"/> the migration uses (ADR-0008): it asks the primary reader (the LevelDB files)
///              first and only when that one fails (files present but unsupported, corrupt or without a <c>pt.*</c> key) it asks the
///              fallback reader (a hidden web view). A successful answer of either reader is final, including "no values": a primary
///              reader that finds no database at all is a new install, and a fallback that sees an empty <c>localStorage</c> settles a
///              primary that could not decode the files. Only when both fail is the read a failure, with both reasons, so that the
///              migration tries again at the next launch. Cancelling stops the whole read.
/// </summary>
public sealed partial class CompositeLegacyStorageReader : ILegacyStorageReader
{
	#region Fields
	/// <summary>
	/// The reader that is asked first.
	/// </summary>
	private readonly ILegacyStorageReader primary;

	/// <summary>
	/// The reader that is asked when the primary one fails.
	/// </summary>
	private readonly ILegacyStorageReader fallback;

	/// <summary>
	/// The log.
	/// </summary>
	private readonly ILogger<CompositeLegacyStorageReader> logger;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="CompositeLegacyStorageReader"/> class.
	/// </summary>
	/// <param name="primary">The reader that is asked first.</param>
	/// <param name="fallback">The reader that is asked when the primary one fails.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
	public CompositeLegacyStorageReader(ILegacyStorageReader primary, ILegacyStorageReader fallback, ILogger<CompositeLegacyStorageReader> logger)
	{
		ArgumentNullException.ThrowIfNull(primary);
		ArgumentNullException.ThrowIfNull(fallback);
		ArgumentNullException.ThrowIfNull(logger);
		this.primary = primary;
		this.fallback = fallback;
		this.logger = logger;
	}
	#endregion

	#region ILegacyStorageReader Implementation
	/// <inheritdoc />
	public async Task<LegacyStorageReading> ReadAsync(CancellationToken cancellationToken)
	{
		var first = await TryReadAsync(primary, cancellationToken).ConfigureAwait(false);
		if (first.Succeeded)
		{
			return first;
		}

		LogFallbackUsed(logger, first.Problem!);
		var second = await TryReadAsync(fallback, cancellationToken).ConfigureAwait(false);
		return second.Succeeded
			? second
			: LegacyStorageReading.Failure($"{first.Problem}; fallback: {second.Problem}");
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Asks a reader and turns an exception of it into a failed reading; a cancellation is not turned.
	/// </summary>
	/// <param name="reader">The reader.</param>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task with the reading.</returns>
	private static async Task<LegacyStorageReading> TryReadAsync(ILegacyStorageReader reader, CancellationToken cancellationToken)
	{
		try
		{
			return await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			return LegacyStorageReading.Failure($"{exception.GetType().Name}: {exception.Message}");
		}
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs that the primary reader failed and the fallback reader is asked.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="problem">Why the primary reader failed.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The old storage files could not be read ({Problem}); asking a hidden web view instead")]
	private static partial void LogFallbackUsed(ILogger logger, string problem);
	#endregion
}
