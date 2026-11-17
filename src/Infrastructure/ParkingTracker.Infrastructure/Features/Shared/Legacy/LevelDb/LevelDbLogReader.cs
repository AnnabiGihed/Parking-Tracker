using System.Buffers.Binary;
using Microsoft.Extensions.Logging;

namespace ParkingTracker.Infrastructure.Features.Shared.Legacy.LevelDb;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Reads the writes out of a LevelDB write-ahead log (<c>*.log</c>), where the newest writes of the old web view's local
///              storage live until LevelDB moves them into a table. The file is a series of 32 KB blocks of records (4 bytes checksum, 2
///              bytes length, 1 byte type: full, first, middle, last); a record or a series of records is one write batch (8 bytes first
///              sequence number, 4 bytes count, then per write a type byte, a length-prefixed key and, for a put, a length-prefixed
///              value). A log that ends in a half-written or unknown record, as after a crash, gives the writes before it, and the early stop is
///              logged with its position, so that a log that was cut by a format change can be told from a clean one. Checksums are not
///              checked: the data is only read, and a damaged value would not parse as the JSON that the settings parsers expect.
/// </summary>
internal static partial class LevelDbLogReader
{
	#region Constants
	/// <summary>
	/// The size of a block of the log.
	/// </summary>
	private const int BlockSize = 32768;

	/// <summary>
	/// The size of the header of a record.
	/// </summary>
	private const int HeaderSize = 7;

	/// <summary>
	/// The record type of a whole batch.
	/// </summary>
	private const byte FullRecord = 1;

	/// <summary>
	/// The record type of the first part of a batch.
	/// </summary>
	private const byte FirstRecord = 2;

	/// <summary>
	/// The record type of a part between the first and the last.
	/// </summary>
	private const byte MiddleRecord = 3;

	/// <summary>
	/// The record type of the last part of a batch.
	/// </summary>
	private const byte LastRecord = 4;

	/// <summary>
	/// The size of the header of a batch: the first sequence number and the count.
	/// </summary>
	private const int BatchHeaderSize = 12;
	#endregion

	#region Public Methods
	/// <summary>
	/// Reads every write of a log.
	/// </summary>
	/// <param name="fileName">The name of the file, for the log.</param>
	/// <param name="log">The bytes of the file.</param>
	/// <param name="logger">The log, which receives a warning when the reading stops before the end of the file.</param>
	/// <returns>The writes in file order.</returns>
	/// <exception cref="InvalidDataException">Thrown when a complete batch does not parse.</exception>
	public static List<LevelDbEntry> ReadEntries(string fileName, byte[] log, ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(log);
		ArgumentNullException.ThrowIfNull(logger);
		var entries = new List<LevelDbEntry>();
		var batches = ReadBatches(log, out var stoppedAt);
		foreach (var batch in batches)
		{
			ReadBatch(batch, entries);
		}

		if (stoppedAt >= 0)
		{
			LogStoppedEarly(logger, fileName, stoppedAt, log.Length);
		}

		return entries;
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Joins the records of the blocks into batches; stops quietly at a half-written or unknown record.
	/// </summary>
	/// <param name="log">The bytes of the file.</param>
	/// <param name="stoppedAt">The position where the reading stopped before the end of the file, or -1 when it read to the end.</param>
	/// <returns>The batches.</returns>
	private static List<byte[]> ReadBatches(byte[] log, out int stoppedAt)
	{
		var batches = new List<byte[]>();
		var pending = new List<byte>();
		stoppedAt = -1;
		var position = 0;
		while (position < log.Length)
		{
			if (BlockSize - (position % BlockSize) < HeaderSize)
			{
				position += BlockSize - (position % BlockSize);
				continue;
			}

			if (position + HeaderSize > log.Length)
			{
				stoppedAt = position;
				break;
			}

			var length = BinaryPrimitives.ReadUInt16LittleEndian(log.AsSpan(position + 4));
			var type = log[position + 6];
			var payload = position + HeaderSize;
			if (type == 0 && length == 0)
			{
				position += BlockSize - (position % BlockSize);
				continue;
			}

			if (payload + length > log.Length || payload + length > ((position / BlockSize) + 1) * BlockSize || !Accept(type, log.AsSpan(payload, length), pending, batches))
			{
				stoppedAt = position;
				break;
			}

			position = payload + length;
		}

		if (stoppedAt < 0 && pending.Count > 0)
		{
			stoppedAt = log.Length;
		}

		return batches;
	}

	/// <summary>
	/// Adds the payload of a record to the batch that is being joined, and completes the batch when this is its last part.
	/// </summary>
	/// <param name="type">The record type.</param>
	/// <param name="payload">The payload.</param>
	/// <param name="pending">The parts of the batch so far.</param>
	/// <param name="batches">The completed batches.</param>
	/// <returns><see langword="false"/> when the record type is unknown or does not fit the sequence.</returns>
	private static bool Accept(byte type, ReadOnlySpan<byte> payload, List<byte> pending, List<byte[]> batches)
	{
		switch (type)
		{
			case FullRecord:
				batches.Add(payload.ToArray());
				return true;
			case FirstRecord:
				pending.Clear();
				pending.AddRange(payload);
				return true;
			case MiddleRecord:
				pending.AddRange(payload);
				return true;
			case LastRecord:
				pending.AddRange(payload);
				batches.Add(pending.ToArray());
				pending.Clear();
				return true;
			default:
				return false;
		}
	}

	/// <summary>
	/// Reads the writes of one batch.
	/// </summary>
	/// <param name="batch">The bytes of the batch.</param>
	/// <param name="entries">Receives the writes.</param>
	/// <exception cref="InvalidDataException">Thrown when the batch does not parse.</exception>
	private static void ReadBatch(byte[] batch, List<LevelDbEntry> entries)
	{
		if (batch.Length < BatchHeaderSize)
		{
			throw new InvalidDataException("A write batch is shorter than its header.");
		}

		var sequence = BinaryPrimitives.ReadInt64LittleEndian(batch.AsSpan(0, 8));
		var count = BinaryPrimitives.ReadUInt32LittleEndian(batch.AsSpan(8, 4));
		var position = BatchHeaderSize;
		for (long index = 0; index < count; index++)
		{
			if (position >= batch.Length)
			{
				throw new InvalidDataException("A write batch has fewer writes than it announces.");
			}

			var tag = batch[position++];
			var key = ReadBytes(batch, ref position);
			switch (tag)
			{
				case 1:
					entries.Add(new LevelDbEntry(key, sequence + index, false, ReadBytes(batch, ref position)));
					break;
				case 0:
					entries.Add(new LevelDbEntry(key, sequence + index, true, []));
					break;
				default:
					throw new InvalidDataException("A write batch has an unknown kind of write.");
			}
		}
	}

	/// <summary>
	/// Reads a length-prefixed byte string.
	/// </summary>
	/// <param name="data">The bytes.</param>
	/// <param name="position">Where the length starts; moved behind the string.</param>
	/// <returns>The bytes of the string.</returns>
	/// <exception cref="InvalidDataException">Thrown when the string does not fit.</exception>
	private static byte[] ReadBytes(byte[] data, ref int position)
	{
		var length = LevelDbVarint.ReadLength(data, ref position);
		if (position + length > data.Length)
		{
			throw new InvalidDataException("A string is cut off.");
		}

		var result = data.AsSpan(position, length).ToArray();
		position += length;
		return result;
	}
	#endregion

	#region Logging
	/// <summary>
	/// Logs that a log file was read only up to a record that is cut off or not understood.
	/// </summary>
	/// <param name="logger">The log.</param>
	/// <param name="file">The name of the file.</param>
	/// <param name="position">The position of the record where the reading stopped.</param>
	/// <param name="length">The length of the file.</param>
	[LoggerMessage(Level = LogLevel.Warning, Message = "The old log {File} was read only up to byte {Position} of {Length}: a record is cut off or not understood")]
	private static partial void LogStoppedEarly(ILogger logger, string file, int position, int length);
	#endregion
}
