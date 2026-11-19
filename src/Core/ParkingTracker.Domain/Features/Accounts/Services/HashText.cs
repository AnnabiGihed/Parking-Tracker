namespace ParkingTracker.Domain.Features.Accounts.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Tells whether a text has the shape of a 256-bit hash, the shape of the subject hash (HMAC-SHA256) and of the
///              refresh token hash (SHA-256): 64 lowercase hexadecimal characters.
/// </summary>
internal static class HashText
{
	#region Constants
	/// <summary>
	/// The length of a 256-bit hash written in hexadecimal.
	/// </summary>
	private const int HexLength = 64;
	#endregion

	#region Internal Methods
	/// <summary>
	/// Tells whether a text is 64 lowercase hexadecimal characters.
	/// </summary>
	/// <param name="text">The text to test; may be <see langword="null"/>.</param>
	/// <returns><see langword="true"/> when the text has the shape of a 256-bit hash.</returns>
	internal static bool IsLowercaseHex256(string? text)
	{
		return text is { Length: HexLength } && text.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
	}
	#endregion
}
