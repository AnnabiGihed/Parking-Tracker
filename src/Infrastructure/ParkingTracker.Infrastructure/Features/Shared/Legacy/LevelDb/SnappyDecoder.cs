namespace ParkingTracker.Infrastructure.Features.Shared.Legacy.LevelDb;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Decompresses the Snappy raw format that LevelDB uses for table blocks. A stream is the uncompressed length as a varint
///              followed by elements: a literal (tag 0, length in the tag or in 1 to 4 following bytes) or a copy of earlier output (tag
///              1 with an 11-bit offset, tag 2 with a 16-bit offset, tag 3 with a 32-bit offset). Anything that does not fit is a corrupt
///              block and throws.
/// </summary>
internal static class SnappyDecoder
{
	#region Constants
	/// <summary>
	/// The largest block that is decompressed; table blocks are about 4 KB (Chromium may grow one to a few hundred KB), so a claim above 4 MB is a corrupt length.
	/// </summary>
	private const int MaxOutput = 4 * 1024 * 1024;
	#endregion

	#region Public Methods
	/// <summary>
	/// Decompresses a block.
	/// </summary>
	/// <param name="input">The compressed bytes.</param>
	/// <returns>The original bytes.</returns>
	/// <exception cref="InvalidDataException">Thrown when the stream is corrupt.</exception>
	public static byte[] Decompress(ReadOnlySpan<byte> input)
	{
		var position = 0;
		var expected = LevelDbVarint.Read(input, ref position);
		if (expected > MaxOutput)
		{
			throw new InvalidDataException("The decompressed size is not plausible.");
		}

		var output = new byte[(int)expected];
		var written = 0;
		while (position < input.Length)
		{
			var tag = input[position++];
			written = (tag & 0x03) == 0
				? CopyLiteral(input, ref position, tag, output, written)
				: CopyEarlier(input, ref position, tag, output, written);
		}

		return written == output.Length ? output : throw new InvalidDataException("The decompressed size does not match.");
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Copies a literal run from the input to the output.
	/// </summary>
	/// <param name="input">The compressed bytes.</param>
	/// <param name="position">Behind the tag; moved behind the literal.</param>
	/// <param name="tag">The tag byte.</param>
	/// <param name="output">The output.</param>
	/// <param name="written">How much of the output is filled.</param>
	/// <returns>The new fill level of the output.</returns>
	private static int CopyLiteral(ReadOnlySpan<byte> input, ref int position, byte tag, byte[] output, int written)
	{
		long length = tag >> 2;
		if (length >= 60)
		{
			var extra = (int)length - 59;
			if (position + extra > input.Length)
			{
				throw new InvalidDataException("A literal length is cut off.");
			}

			length = 0;
			for (var i = 0; i < extra; i++)
			{
				length |= (long)input[position + i] << (8 * i);
			}

			position += extra;
		}

		length++;
		if (position + length > input.Length || written + length > output.Length)
		{
			throw new InvalidDataException("A literal does not fit.");
		}

		input.Slice(position, (int)length).CopyTo(output.AsSpan(written));
		position += (int)length;
		return written + (int)length;
	}

	/// <summary>
	/// Copies bytes that were written earlier to the end of the output; the source may overlap the destination, which repeats a pattern.
	/// </summary>
	/// <param name="input">The compressed bytes.</param>
	/// <param name="position">Behind the tag; moved behind the offset.</param>
	/// <param name="tag">The tag byte.</param>
	/// <param name="output">The output.</param>
	/// <param name="written">How much of the output is filled.</param>
	/// <returns>The new fill level of the output.</returns>
	private static int CopyEarlier(ReadOnlySpan<byte> input, ref int position, byte tag, byte[] output, int written)
	{
		int length;
		long offset;
		switch (tag & 0x03)
		{
			case 1:
				length = 4 + ((tag >> 2) & 0x07);
				offset = ((long)(tag >> 5) << 8) | ReadOffset(input, ref position, 1);
				break;
			case 2:
				length = 1 + (tag >> 2);
				offset = ReadOffset(input, ref position, 2);
				break;
			default:
				length = 1 + (tag >> 2);
				offset = ReadOffset(input, ref position, 4);
				break;
		}

		if (offset <= 0 || offset > written || written + length > output.Length)
		{
			throw new InvalidDataException("A copy points outside the output.");
		}

		for (var i = 0; i < length; i++)
		{
			output[written + i] = output[written + i - (int)offset];
		}

		return written + length;
	}

	/// <summary>
	/// Reads a little-endian offset of one to four bytes.
	/// </summary>
	/// <param name="input">The compressed bytes.</param>
	/// <param name="position">Where the offset starts; moved behind it.</param>
	/// <param name="size">How many bytes the offset has.</param>
	/// <returns>The offset.</returns>
	private static long ReadOffset(ReadOnlySpan<byte> input, ref int position, int size)
	{
		if (position + size > input.Length)
		{
			throw new InvalidDataException("A copy offset is cut off.");
		}

		long value = 0;
		for (var i = 0; i < size; i++)
		{
			value |= (long)input[position + i] << (8 * i);
		}

		position += size;
		return value;
	}
	#endregion
}
