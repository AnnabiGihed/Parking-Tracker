using System.Buffers.Binary;

namespace ParkingTracker.ViewModels.Features.Shared.Settings.Legacy.LevelDb;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Reads the writes out of a LevelDB table file (<c>*.ldb</c>, <c>*.sst</c>), where LevelDB keeps older writes of the old
///              web view's local storage. The last 48 bytes are the footer (handles of the metaindex and the index block, padding, the
///              magic number); the index block lists the data blocks; a block holds entries whose keys share a prefix with the entry
///              before, followed by restart points; after each block come a type byte (0 stored, 1 Snappy) and a checksum. The key of
///              an entry is the key of the application plus 8 bytes: the sequence number shifted by 8 bits, with the kind of write
///              (1 put, 0 delete) in the low byte. Another compression type, or any structure that does not fit, is a corrupt or
///              unsupported file and throws, so that the migration tries again instead of copying part of the data.
/// </summary>
internal static class LevelDbTableReader
{
	#region Constants
	/// <summary>
	/// The size of the footer.
	/// </summary>
	private const int FooterSize = 48;

	/// <summary>
	/// The magic number at the end of every table file.
	/// </summary>
	private const ulong TableMagic = 0xdb4775248b80fb57UL;

	/// <summary>
	/// The size of the type byte and the checksum after each block.
	/// </summary>
	private const int TrailerSize = 5;

	/// <summary>
	/// The block type of a stored block.
	/// </summary>
	private const byte StoredBlock = 0;

	/// <summary>
	/// The block type of a Snappy-compressed block.
	/// </summary>
	private const byte SnappyBlock = 1;

	/// <summary>
	/// The size of the sequence tag at the end of an internal key.
	/// </summary>
	private const int TagSize = 8;
	#endregion

	#region Public Methods
	/// <summary>
	/// Reads every write of a table.
	/// </summary>
	/// <param name="table">The bytes of the file.</param>
	/// <returns>The writes in key order.</returns>
	/// <exception cref="InvalidDataException">Thrown when the file is corrupt or uses a compression that is not supported.</exception>
	public static List<LevelDbEntry> ReadEntries(byte[] table)
	{
		ArgumentNullException.ThrowIfNull(table);
		if (table.Length < FooterSize || BinaryPrimitives.ReadUInt64LittleEndian(table.AsSpan(table.Length - 8)) != TableMagic)
		{
			throw new InvalidDataException("The file is not a LevelDB table.");
		}

		var footer = table.AsSpan(table.Length - FooterSize, FooterSize);
		var position = 0;
		LevelDbVarint.Read(footer, ref position);
		LevelDbVarint.Read(footer, ref position);
		var indexOffset = LevelDbVarint.Read(footer, ref position);
		var indexSize = LevelDbVarint.Read(footer, ref position);

		var entries = new List<LevelDbEntry>();
		foreach (var (_, handle) in ReadBlockEntries(ReadBlock(table, indexOffset, indexSize)))
		{
			var at = 0;
			var offset = LevelDbVarint.Read(handle, ref at);
			var size = LevelDbVarint.Read(handle, ref at);
			foreach (var (internalKey, value) in ReadBlockEntries(ReadBlock(table, offset, size)))
			{
				entries.Add(ToEntry(internalKey, value));
			}
		}

		return entries;
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Reads a block and decompresses it when needed.
	/// </summary>
	/// <param name="table">The bytes of the file.</param>
	/// <param name="offset">Where the block starts.</param>
	/// <param name="size">The size of the block without its trailer.</param>
	/// <returns>The content of the block.</returns>
	/// <exception cref="InvalidDataException">Thrown when the block lies outside the file or has an unsupported type.</exception>
	private static byte[] ReadBlock(byte[] table, ulong offset, ulong size)
	{
		if (offset > (ulong)table.Length || size > (ulong)table.Length || offset + size + TrailerSize > (ulong)table.Length)
		{
			throw new InvalidDataException("A block lies outside the file.");
		}

		var content = table.AsSpan((int)offset, (int)size);
		return table[(int)(offset + size)] switch
		{
			StoredBlock => content.ToArray(),
			SnappyBlock => SnappyDecoder.Decompress(content),
			var type => throw new InvalidDataException($"A block uses the unsupported compression type {type}."),
		};
	}

	/// <summary>
	/// Reads the entries of a block: key and value pairs, with the shared key prefixes expanded.
	/// </summary>
	/// <param name="block">The content of the block.</param>
	/// <returns>The pairs in order.</returns>
	/// <exception cref="InvalidDataException">Thrown when the block does not parse.</exception>
	private static List<(byte[] Key, byte[] Value)> ReadBlockEntries(byte[] block)
	{
		if (block.Length < 4)
		{
			throw new InvalidDataException("A block is too short.");
		}

		var restarts = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(block.Length - 4));
		var end = block.Length - 4 - (long)restarts * 4;
		if (end < 0)
		{
			throw new InvalidDataException("A block announces more restart points than it can hold.");
		}

		var result = new List<(byte[], byte[])>();
		var position = 0;
		var key = Array.Empty<byte>();
		while (position < end)
		{
			var shared = LevelDbVarint.ReadLength(block, ref position);
			var unshared = LevelDbVarint.ReadLength(block, ref position);
			var valueLength = LevelDbVarint.ReadLength(block, ref position);
			if (shared > key.Length || position + (long)unshared + valueLength > end)
			{
				throw new InvalidDataException("An entry of a block does not fit.");
			}

			key = [.. key.AsSpan(0, shared), .. block.AsSpan(position, unshared)];
			position += unshared;
			result.Add((key, block.AsSpan(position, valueLength).ToArray()));
			position += valueLength;
		}

		return result;
	}

	/// <summary>
	/// Splits the internal key of an entry into the key and the sequence tag.
	/// </summary>
	/// <param name="internalKey">The key with its 8-byte tag.</param>
	/// <param name="value">The value.</param>
	/// <returns>The write.</returns>
	/// <exception cref="InvalidDataException">Thrown when the key is shorter than its tag.</exception>
	private static LevelDbEntry ToEntry(byte[] internalKey, byte[] value)
	{
		if (internalKey.Length < TagSize)
		{
			throw new InvalidDataException("A key is shorter than its sequence tag.");
		}

		var tag = BinaryPrimitives.ReadUInt64LittleEndian(internalKey.AsSpan(internalKey.Length - TagSize));
		return new LevelDbEntry(internalKey.AsSpan(0, internalKey.Length - TagSize).ToArray(), (long)(tag >> 8), (tag & 0xFF) == 0, value);
	}
	#endregion
}
