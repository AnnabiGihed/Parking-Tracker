using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ParkingTracker.Application.Features.Shared.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The value the phone stores to remember which account its data belongs to (ADR-0032): the lowercase hexadecimal SHA-256 of
///              the account id written with the "N" format (32 digits, no dashes). The id is itself a random identifier of the API, so
///              the hash only keeps it out of the database in plain text; it is not a secret.
/// </summary>
public static class DeviceOwnerHash
{
	#region Public Methods
	/// <summary>
	/// Computes the hash of an account id.
	/// </summary>
	/// <param name="accountId">The account id the API returned.</param>
	/// <returns>The 64 lowercase hexadecimal characters of the hash.</returns>
	public static string Of(Guid accountId)
	{
		var text = accountId.ToString("N", CultureInfo.InvariantCulture);
		return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
	}
	#endregion
}
