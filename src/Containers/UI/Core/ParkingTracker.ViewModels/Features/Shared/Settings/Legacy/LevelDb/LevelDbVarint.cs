namespace ParkingTracker.ViewModels.Features.Shared.Settings.Legacy.LevelDb;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Reads the variable-length integers of LevelDB and Snappy: seven bits per byte, least significant group first, the high
///              bit set while more bytes follow. A truncated or overlong number is a corrupt file and throws.
/// </summary>
internal static class LevelDbVarint
{
	#region Public Methods
	/// <summary>
	/// Reads an unsigned number of up to 64 bits.
	/// </summary>
	/// <param name="data">The bytes.</param>
	/// <param name="position">Where the number starts; moved behind it.</param>
	/// <returns>The number.</returns>
	/// <exception cref="InvalidDataException">Thrown when the data ends inside the number or the number is longer than 10 bytes.</exception>
	public static ulong Read(ReadOnlySpan<byte> data, ref int position)
	{
		ulong result = 0;
		for (var shift = 0; shift <= 63; shift += 7)
		{
			if (position >= data.Length)
			{
				throw new InvalidDataException("A number ends outside the data.");
			}

			var next = data[position++];
			result |= (ulong)(next & 0x7F) << shift;
			if ((next & 0x80) == 0)
			{
				return result;
			}
		}

		throw new InvalidDataException("A number is too long.");
	}

	/// <summary>
	/// Reads a length or count that must fit in the data.
	/// </summary>
	/// <param name="data">The bytes.</param>
	/// <param name="position">Where the number starts; moved behind it.</param>
	/// <returns>The number as an <see cref="int"/>.</returns>
	/// <exception cref="InvalidDataException">Thrown when the number is corrupt or larger than the data.</exception>
	public static int ReadLength(ReadOnlySpan<byte> data, ref int position)
	{
		var value = Read(data, ref position);
		return value > (ulong)data.Length ? throw new InvalidDataException("A length is larger than the data.") : (int)value;
	}
	#endregion
}
