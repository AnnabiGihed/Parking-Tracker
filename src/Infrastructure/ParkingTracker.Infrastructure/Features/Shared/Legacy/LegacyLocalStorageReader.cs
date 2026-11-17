using Microsoft.Extensions.Logging;
using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Infrastructure.Features.Shared.Legacy.LevelDb;

namespace ParkingTracker.Infrastructure.Features.Shared.Legacy;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The primary <see cref="ILegacyStorageReader"/> of the app (ADR-0008): reads the <c>pt.*</c> values that the Capacitor
///              version saved in the <c>localStorage</c> of <c>https://localhost</c> straight from the files of the web view's
///              <c>Local Storage/leveldb</c> directory, which Android keeps across an update of the same package. No web view is
///              started, so nothing depends on the old origin being served, and the files are only read, never changed. A missing
///              directory, or one without database files, means a new install and gives no values. An unreadable or unsupported file
///              gives a failure, and so do database files from which no <c>pt.*</c> key could be decoded (the format may have
///              changed): both are cases for the fallback reader. The host only supplies the directory, which is why this class is
///              plain C# that tests run against a temporary directory.
/// </summary>
public sealed class LegacyLocalStorageReader : ILegacyStorageReader
{
	#region Constants
	/// <summary>
	/// The origin of a Capacitor app on Android.
	/// </summary>
	public const string CapacitorOrigin = "https://localhost";

	/// <summary>
	/// The keys of the old app all start with this text.
	/// </summary>
	public const string KeyPrefix = "pt.";
	#endregion

	#region Fields
	/// <summary>
	/// The <c>Local Storage/leveldb</c> directory of the old web view.
	/// </summary>
	private readonly string directory;

	/// <summary>
	/// The log, which hears of a log file that was read only in part.
	/// </summary>
	private readonly ILogger<LegacyLocalStorageReader> logger;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="LegacyLocalStorageReader"/> class.
	/// </summary>
	/// <param name="directory">The <c>Local Storage/leveldb</c> directory of the old web view; it may not exist.</param>
	/// <param name="logger">The log.</param>
	/// <exception cref="ArgumentException">Thrown when <paramref name="directory"/> is empty.</exception>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
	public LegacyLocalStorageReader(string directory, ILogger<LegacyLocalStorageReader> logger)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);
		ArgumentNullException.ThrowIfNull(logger);
		this.directory = directory;
		this.logger = logger;
	}
	#endregion

	#region ILegacyStorageReader Implementation
	/// <inheritdoc />
	public async Task<LegacyStorageReading> ReadAsync(CancellationToken cancellationToken)
	{
		if (!Directory.Exists(directory))
		{
			return NothingThere();
		}

		try
		{
			var files = new List<LevelDbFile>();
			foreach (var path in Directory.EnumerateFiles(directory).Where(IsDatabaseFile))
			{
				files.Add(new LevelDbFile(Path.GetFileName(path), await ReadFileAsync(path, cancellationToken).ConfigureAwait(false)));
			}

			if (files.Count == 0)
			{
				return NothingThere();
			}

			var values = ChromiumLocalStorageParser.Read(files, CapacitorOrigin, KeyPrefix, logger);
			return values.Count > 0
				? LegacyStorageReading.Success(values, LegacyStorageReading.LevelDbReader)
				: LegacyStorageReading.Failure($"{files.Count} database file(s) are present but no {KeyPrefix}* key could be decoded");
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			return LegacyStorageReading.Failure($"{exception.GetType().Name}: {exception.Message}");
		}
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Creates the answer for an old web view that stored nothing: a new install.
	/// </summary>
	/// <returns>A successful reading without values.</returns>
	private static LegacyStorageReading NothingThere()
	{
		return LegacyStorageReading.Success(new Dictionary<string, string>(StringComparer.Ordinal), LegacyStorageReading.LevelDbReader);
	}

	/// <summary>
	/// Tells whether a file holds data of the database; <c>LOCK</c>, <c>CURRENT</c>, <c>LOG</c> and <c>MANIFEST-*</c> do not.
	/// </summary>
	/// <param name="path">The path of the file.</param>
	/// <returns><see langword="true"/> for a write-ahead log or a table.</returns>
	private static bool IsDatabaseFile(string path)
	{
		var extension = Path.GetExtension(path);
		return string.Equals(extension, ".log", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(extension, ".ldb", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(extension, ".sst", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Reads a whole file without blocking a web view that may still have it open.
	/// </summary>
	/// <param name="path">The path of the file.</param>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task with the bytes.</returns>
	private static async Task<byte[]> ReadFileAsync(string path, CancellationToken cancellationToken)
	{
		var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
		await using (stream.ConfigureAwait(false))
		{
			var bytes = new byte[stream.Length];
			await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
			return bytes;
		}
	}
	#endregion
}
