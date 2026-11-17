using ParkingTracker.ViewModels.Features.Shared.Settings.Legacy.LevelDb;

namespace ParkingTracker.ViewModels.Features.Shared.Settings.Legacy;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The <see cref="ILegacyStorageReader"/> of the app (ADR-0004): reads the <c>pt.*</c> values that the Capacitor version
///              saved in the <c>localStorage</c> of <c>https://localhost</c> straight from the files of the web view's
///              <c>Local Storage/leveldb</c> directory, which Android keeps across an update of the same package. No web view is started,
///              so nothing depends on the old origin being served. A missing directory means a new install and gives no values; an
///              unreadable or unsupported file gives a failure, so that the migration tries again. The host only supplies the
///              directory, which is why this class is plain C# that tests run against a temporary directory.
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
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="LegacyLocalStorageReader"/> class.
	/// </summary>
	/// <param name="directory">The <c>Local Storage/leveldb</c> directory of the old web view; it may not exist.</param>
	/// <exception cref="ArgumentException">Thrown when <paramref name="directory"/> is empty.</exception>
	public LegacyLocalStorageReader(string directory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);
		this.directory = directory;
	}
	#endregion

	#region ILegacyStorageReader Implementation
	/// <inheritdoc />
	public async Task<LegacyStorageReading> ReadAsync(CancellationToken cancellationToken)
	{
		if (!Directory.Exists(directory))
		{
			return LegacyStorageReading.Success(new Dictionary<string, string>(StringComparer.Ordinal));
		}

		try
		{
			var files = new List<LevelDbFile>();
			foreach (var path in Directory.EnumerateFiles(directory).Where(IsDatabaseFile))
			{
				files.Add(new LevelDbFile(Path.GetFileName(path), await ReadFileAsync(path, cancellationToken).ConfigureAwait(false)));
			}

			return LegacyStorageReading.Success(ChromiumLocalStorageParser.Read(files, CapacitorOrigin, KeyPrefix));
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
		{
			return LegacyStorageReading.Failure($"{exception.GetType().Name}: {exception.Message}");
		}
	}
	#endregion

	#region Private Helpers
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
