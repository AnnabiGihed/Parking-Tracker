using System.Text;
using Microsoft.Extensions.Logging;

namespace ParkingTracker.Infrastructure.Features.Shared.Legacy.LevelDb;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Reads the <c>localStorage</c> of one origin out of the files of Chromium's <c>Local Storage/leveldb</c> directory, the
///              way the old web view wrote it, without starting a web view (ADR-0008). A key of the database is <c>_</c>, the origin,
///              a zero byte, then a format byte and the name; a value is a format byte and the text. Format byte 1 means the text is
///              Latin-1 (one byte per character), 0 means UTF-16 little-endian, which Chromium picks when a character does not fit in
///              Latin-1. The newest write of a key, by sequence number across the log and the tables, decides; a deleted key is absent.
/// </summary>
public static class ChromiumLocalStorageParser
{
	#region Constants
	/// <summary>
	/// The format byte of a UTF-16 little-endian text.
	/// </summary>
	private const byte Utf16Format = 0;

	/// <summary>
	/// The format byte of a Latin-1 text.
	/// </summary>
	private const byte Latin1Format = 1;

	/// <summary>
	/// The byte that ends the origin in a key.
	/// </summary>
	private const byte OriginEnd = 0;
	#endregion

	#region Public Methods
	/// <summary>
	/// Reads the values of an origin whose key starts with a prefix.
	/// </summary>
	/// <param name="files">The files of the directory; other kinds of file (<c>CURRENT</c>, <c>MANIFEST-*</c>, <c>LOCK</c>) are ignored.</param>
	/// <param name="origin">The origin as Chromium serializes it, for example <c>https://localhost</c>.</param>
	/// <param name="keyPrefix">Only keys that start with this text are returned, for example <c>pt.</c>.</param>
	/// <param name="logger">The log, which hears of a log file that was read only in part.</param>
	/// <returns>The text of each key that exists, by key name.</returns>
	/// <exception cref="InvalidDataException">Thrown when a file of the database is corrupt or uses a compression that is not supported.</exception>
	public static IReadOnlyDictionary<string, string> Read(IEnumerable<LevelDbFile> files, string origin, string keyPrefix, ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(files);
		ArgumentException.ThrowIfNullOrEmpty(origin);
		ArgumentNullException.ThrowIfNull(keyPrefix);
		ArgumentNullException.ThrowIfNull(logger);

		var newest = new Dictionary<string, LevelDbEntry>(StringComparer.Ordinal);
		var prefixes = new[] { Encoding.UTF8.GetBytes("_" + origin).Append(OriginEnd).ToArray(), Encoding.UTF8.GetBytes("_" + origin + "/").Append(OriginEnd).ToArray() };
		foreach (var entry in files.SelectMany(file => ReadEntries(file, logger)))
		{
			if (TryReadName(entry.Key, prefixes) is { } name && name.StartsWith(keyPrefix, StringComparison.Ordinal)
				&& (!newest.TryGetValue(name, out var known) || entry.Sequence > known.Sequence))
			{
				newest[name] = entry;
			}
		}

		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var (name, entry) in newest)
		{
			if (!entry.IsDeletion && DecodeText(entry.Value) is { } text)
			{
				result[name] = text;
			}
		}

		return result;
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Reads the writes of a file by its extension.
	/// </summary>
	/// <param name="file">The file.</param>
	/// <param name="logger">The log.</param>
	/// <returns>The writes; none for a file that is neither a log nor a table.</returns>
	private static List<LevelDbEntry> ReadEntries(LevelDbFile file, ILogger logger)
	{
		if (file.Name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
		{
			return LevelDbLogReader.ReadEntries(file.Name, file.Content, logger);
		}

		return file.Name.EndsWith(".ldb", StringComparison.OrdinalIgnoreCase) || file.Name.EndsWith(".sst", StringComparison.OrdinalIgnoreCase)
			? LevelDbTableReader.ReadEntries(file.Content)
			: [];
	}

	/// <summary>
	/// Reads the name of a key that belongs to the origin.
	/// </summary>
	/// <param name="key">The key of the database.</param>
	/// <param name="prefixes">The accepted beginnings: the underscore, the origin and the zero byte.</param>
	/// <returns>The name, or <see langword="null"/> for a key of another origin or another kind (<c>META:</c>, <c>VERSION</c>).</returns>
	private static string? TryReadName(byte[] key, byte[][] prefixes)
	{
		foreach (var prefix in prefixes)
		{
			if (key.Length > prefix.Length && key.AsSpan().StartsWith(prefix))
			{
				return DecodeText(key.AsSpan(prefix.Length).ToArray());
			}
		}

		return null;
	}

	/// <summary>
	/// Decodes a text that starts with its format byte.
	/// </summary>
	/// <param name="bytes">The format byte and the text.</param>
	/// <returns>The text, or <see langword="null"/> when the format is unknown or empty.</returns>
	private static string? DecodeText(byte[] bytes)
	{
		if (bytes.Length == 0)
		{
			return null;
		}

		return bytes[0] switch
		{
			Latin1Format => Encoding.Latin1.GetString(bytes, 1, bytes.Length - 1),
			Utf16Format when (bytes.Length - 1) % 2 == 0 => Encoding.Unicode.GetString(bytes, 1, bytes.Length - 1),
			_ => null,
		};
	}
	#endregion
}
