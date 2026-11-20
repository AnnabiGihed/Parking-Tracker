using System.Security.Cryptography;
using System.Text;

namespace ParkingTracker.Backend.Application.Features.Accounts.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Makes the random secrets of the sign-in: the 32-byte nonce and refresh token in base64url, and the SHA-256 hash of
///              a refresh token in lowercase hexadecimal, the only form the server stores.
/// </summary>
internal static class RandomToken
{
	#region Constants
	/// <summary>
	/// The number of random bytes of a nonce and of a refresh token: 256 bits.
	/// </summary>
	private const int ByteCount = 32;
	#endregion

	#region Internal Methods
	/// <summary>
	/// Makes a new random token.
	/// </summary>
	/// <returns>32 random bytes in base64url, without padding.</returns>
	internal static string New()
	{
		return Convert.ToBase64String(RandomNumberGenerator.GetBytes(ByteCount)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}

	/// <summary>
	/// Hashes a token the way the server stores it.
	/// </summary>
	/// <param name="token">The token.</param>
	/// <returns>SHA-256 of the UTF-8 bytes of the token as 64 lowercase hexadecimal characters.</returns>
	internal static string Hash(string token)
	{
		return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
	}
	#endregion
}
