using System.Buffers.Binary;
using System.Text;

namespace ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Builds the bytes of LevelDB files as the old web view's local storage wrote them, from the documented layouts: a
///              write-ahead log of batches in 32 KB blocks and a table with one data block, an index block and the footer. The
///              keys and values use Chromium's encoding (a format byte, then Latin-1 or UTF-16 text), so the parser tests start from
///              sample bytes and not from the parser's own idea of the format.
/// </summary>
internal static class LevelDbBuilder
{
	#region Constants
	/// <summary>
	/// The size of a block of the log.
	/// </summary>
	public const int LogBlockSize = 32768;

	/// <summary>
	/// The magic number at the end of a table.
	/// </summary>
	private const ulong Magic = 0xdb4775248b80fb57UL;
	#endregion

	#region Chromium Encoding
	/// <summary>
	/// Builds the key of the database for a name of the origin <c>https://localhost</c>: <c>_origin</c>, a zero byte, the format byte 1
	/// and the Latin-1 name.
	/// </summary>
	/// <param name="name">The name of the key, for example <c>pt.card</c>.</param>
	/// <param name="origin">The origin.</param>
	/// <returns>The key.</returns>
	public static byte[] Key(string name, string origin = "https://localhost")
	{
		return [.. Encoding.UTF8.GetBytes("_" + origin), (byte)0, (byte)1, .. Encoding.Latin1.GetBytes(name)];
	}

	/// <summary>
	/// Builds a value of format 1 (Latin-1) or, when a character does not fit, of format 0 (UTF-16 little-endian).
	/// </summary>
	/// <param name="text">The text.</param>
	/// <returns>The format byte and the encoded text.</returns>
	public static byte[] Value(string text)
	{
		return text.All(c => c <= 0xFF)
			? [(byte)1, .. Encoding.Latin1.GetBytes(text)]
			: [(byte)0, .. Encoding.Unicode.GetBytes(text)];
	}
	#endregion

	#region Log
	/// <summary>
	/// Builds a log that holds one write batch per call argument, each as a full record.
	/// </summary>
	/// <param name="batches">The batches: the first sequence number and the writes.</param>
	/// <returns>The bytes of the log.</returns>
	public static byte[] Log(params (long Sequence, (byte[] Key, byte[]? Value)[] Writes)[] batches)
	{
		var file = new List<byte>();
		foreach (var (sequence, writes) in batches)
		{
			AddRecord(file, 1, Batch(sequence, writes));
		}

		return file.ToArray();
	}

	/// <summary>
	/// Builds a log with one batch that is split over several records, as a batch larger than a block is.
	/// </summary>
	/// <param name="sequence">The first sequence number.</param>
	/// <param name="writes">The writes.</param>
	/// <returns>The bytes of the log.</returns>
	public static byte[] SplitLog(long sequence, params (byte[] Key, byte[]? Value)[] writes)
	{
		var batch = Batch(sequence, writes);
		var file = new List<byte>();
		var offset = 0;
		var first = true;
		while (offset < batch.Length)
		{
			var room = LogBlockSize - (file.Count % LogBlockSize) - 7;
			var take = Math.Min(room, batch.Length - offset);
			var last = offset + take == batch.Length;
			byte type = (first, last) switch
			{
				(true, true) => 1,
				(true, false) => 2,
				(false, true) => 4,
				_ => 3,
			};
			AddRecord(file, type, batch.AsSpan(offset, take).ToArray());
			offset += take;
			first = false;
			var remaining = LogBlockSize - (file.Count % LogBlockSize);
			if (remaining < 7)
			{
				file.AddRange(new byte[remaining]);
			}
		}

		return file.ToArray();
	}

	/// <summary>
	/// Builds the bytes of one write batch.
	/// </summary>
	/// <param name="sequence">The first sequence number.</param>
	/// <param name="writes">The writes; a <see langword="null"/> value is a deletion.</param>
	/// <returns>The batch.</returns>
	public static byte[] Batch(long sequence, (byte[] Key, byte[]? Value)[] writes)
	{
		var batch = new List<byte>();
		batch.AddRange(BitConverter.GetBytes(sequence));
		batch.AddRange(BitConverter.GetBytes((uint)writes.Length));
		foreach (var (key, value) in writes)
		{
			batch.Add(value is null ? (byte)0 : (byte)1);
			batch.AddRange(Varint((ulong)key.Length));
			batch.AddRange(key);
			if (value is not null)
			{
				batch.AddRange(Varint((ulong)value.Length));
				batch.AddRange(value);
			}
		}

		return batch.ToArray();
	}
	#endregion

	#region Table
	/// <summary>
	/// Builds a table with one data block that holds the given writes, an index block and the footer.
	/// </summary>
	/// <param name="entries">The writes: key, sequence number, value (<see langword="null"/> for a deletion).</param>
	/// <param name="compress">Compresses the data block (block type 1), or <see langword="null"/> to store it (block type 0).</param>
	/// <param name="blockType">The block type byte to write when <paramref name="compress"/> is given.</param>
	/// <returns>The bytes of the table.</returns>
	public static byte[] Table(IEnumerable<(byte[] Key, long Sequence, byte[]? Value)> entries, Func<byte[], byte[]>? compress = null, byte blockType = 1)
	{
		var block = Block(entries.Select(e => (InternalKey(e.Key, e.Sequence, e.Value is not null), e.Value ?? [])));
		var file = new List<byte>();
		var dataType = compress is null ? (byte)0 : blockType;
		var data = compress is null ? block : compress(block);
		file.AddRange(data);
		AddTrailer(file, dataType);

		var handle = new List<byte>();
		handle.AddRange(Varint(0));
		handle.AddRange(Varint((ulong)data.Length));
		var index = Block([([(byte)'k'], handle.ToArray())]);
		var indexOffset = file.Count;
		file.AddRange(index);
		AddTrailer(file, 0);

		var footer = new List<byte>();
		footer.AddRange(Varint(0));
		footer.AddRange(Varint(0));
		footer.AddRange(Varint((ulong)indexOffset));
		footer.AddRange(Varint((ulong)index.Length));
		footer.AddRange(new byte[40 - footer.Count]);
		footer.AddRange(BitConverter.GetBytes(Magic));
		file.AddRange(footer);
		return file.ToArray();
	}

	/// <summary>
	/// Compresses data into a Snappy stream with a greedy search for repeats: literals, and copy elements with a one-byte (4 to 11
	/// bytes, offset below 2048) or two-byte offset (up to 64 bytes) where earlier bytes repeat.
	/// </summary>
	/// <param name="data">The data.</param>
	/// <returns>The Snappy stream.</returns>
	public static byte[] SnappyCompress(byte[] data)
	{
		var stream = new List<byte>(Varint((ulong)data.Length));
		var literalStart = 0;
		var position = 0;
		while (position < data.Length)
		{
			var (offset, length) = FindRepeat(data, position);
			if (length < 4)
			{
				position++;
				continue;
			}

			AddLiteral(stream, data.AsSpan(literalStart, position - literalStart));
			if (length <= 11 && offset < 2048)
			{
				stream.Add((byte)(1 | ((length - 4) << 2) | ((offset >> 8) << 5)));
				stream.Add((byte)(offset & 0xFF));
			}
			else
			{
				stream.Add((byte)(2 | ((length - 1) << 2)));
				stream.AddRange(BitConverter.GetBytes((ushort)offset));
			}

			position += length;
			literalStart = position;
		}

		AddLiteral(stream, data.AsSpan(literalStart));
		return stream.ToArray();
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Finds the longest earlier run that repeats the bytes at a position.
	/// </summary>
	/// <param name="data">The data.</param>
	/// <param name="position">The position.</param>
	/// <returns>How far back the run starts and how long it is; a length of 0 when nothing repeats.</returns>
	private static (int Offset, int Length) FindRepeat(byte[] data, int position)
	{
		var best = (Offset: 0, Length: 0);
		for (var start = Math.Max(0, position - 2000); start < position; start++)
		{
			var length = 0;
			while (length < 64 && position + length < data.Length && data[start + length] == data[position + length])
			{
				length++;
			}

			if (length > best.Length)
			{
				best = (position - start, length);
			}
		}

		return best;
	}

	/// <summary>
	/// Adds a literal element to a Snappy stream; nothing for an empty run.
	/// </summary>
	/// <param name="stream">The stream.</param>
	/// <param name="literal">The bytes.</param>
	private static void AddLiteral(List<byte> stream, ReadOnlySpan<byte> literal)
	{
		if (literal.Length == 0)
		{
			return;
		}

		var length = literal.Length - 1;
		if (length < 60)
		{
			stream.Add((byte)(length << 2));
		}
		else
		{
			stream.Add(61 << 2); // two length bytes follow
			stream.AddRange(BitConverter.GetBytes((ushort)length));
		}

		stream.AddRange(literal.ToArray());
	}

	/// <summary>
	/// Writes a record into a log.
	/// </summary>
	/// <param name="file">The log so far.</param>
	/// <param name="type">The record type.</param>
	/// <param name="payload">The payload.</param>
	private static void AddRecord(List<byte> file, byte type, byte[] payload)
	{
		file.AddRange(new byte[4]);
		file.AddRange(BitConverter.GetBytes((ushort)payload.Length));
		file.Add(type);
		file.AddRange(payload);
	}

	/// <summary>
	/// Adds the type byte and the (unchecked) checksum after a block.
	/// </summary>
	/// <param name="file">The table so far.</param>
	/// <param name="type">The block type.</param>
	private static void AddTrailer(List<byte> file, byte type)
	{
		file.Add(type);
		file.AddRange(new byte[4]);
	}

	/// <summary>
	/// Builds a block where every entry is a restart point.
	/// </summary>
	/// <param name="entries">The key and value pairs.</param>
	/// <returns>The block.</returns>
	private static byte[] Block(IEnumerable<(byte[] Key, byte[] Value)> entries)
	{
		var block = new List<byte>();
		var restarts = new List<uint>();
		foreach (var (key, value) in entries)
		{
			restarts.Add((uint)block.Count);
			block.AddRange(Varint(0));
			block.AddRange(Varint((ulong)key.Length));
			block.AddRange(Varint((ulong)value.Length));
			block.AddRange(key);
			block.AddRange(value);
		}

		foreach (var restart in restarts)
		{
			block.AddRange(BitConverter.GetBytes(restart));
		}

		block.AddRange(BitConverter.GetBytes((uint)restarts.Count));
		return block.ToArray();
	}

	/// <summary>
	/// Adds the sequence tag that LevelDB appends to a key in a table.
	/// </summary>
	/// <param name="key">The key.</param>
	/// <param name="sequence">The sequence number.</param>
	/// <param name="isPut"><see langword="true"/> for a put, <see langword="false"/> for a deletion.</param>
	/// <returns>The internal key.</returns>
	private static byte[] InternalKey(byte[] key, long sequence, bool isPut)
	{
		var tag = new byte[8];
		BinaryPrimitives.WriteUInt64LittleEndian(tag, ((ulong)sequence << 8) | (isPut ? 1UL : 0UL));
		return [.. key, .. tag];
	}

	/// <summary>
	/// Writes a variable-length integer.
	/// </summary>
	/// <param name="value">The number.</param>
	/// <returns>The bytes.</returns>
	private static byte[] Varint(ulong value)
	{
		var bytes = new List<byte>();
		while (value >= 0x80)
		{
			bytes.Add((byte)(value | 0x80));
			value >>= 7;
		}

		bytes.Add((byte)value);
		return bytes.ToArray();
	}
	#endregion
}
