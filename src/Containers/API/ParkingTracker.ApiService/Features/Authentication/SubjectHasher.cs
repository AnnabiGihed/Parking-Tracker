using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;

namespace ParkingTracker.ApiService.Features.Authentication;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Hashes the Google subject with HMAC-SHA256 under the secret subject hash key, so the server keeps no subject and a
///              leaked database cannot be matched against Google subjects without the key. Without a configured key (Development
///              only) it uses a random key for the life of the process; the options validator refuses that anywhere else.
/// </summary>
public sealed class SubjectHasher : ISubjectHasher
{
	#region Fields
	/// <summary>
	/// The secret key.
	/// </summary>
	private readonly byte[] key;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SubjectHasher"/> class.
	/// </summary>
	/// <param name="options">The account settings holding the key as base64.</param>
	/// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
	/// <exception cref="FormatException">The configured key is not base64.</exception>
	public SubjectHasher(IOptions<AccountOptions> options)
	{
		ArgumentNullException.ThrowIfNull(options);
		var configured = options.Value.SubjectHashKey;
		key = string.IsNullOrWhiteSpace(configured)
			? RandomNumberGenerator.GetBytes(AccountOptions.SubjectHashKeyBytes)
			: Convert.FromBase64String(configured);
	}
	#endregion

	#region ISubjectHasher Implementation
	/// <inheritdoc />
	public string Hash(string subject)
	{
		ArgumentException.ThrowIfNullOrEmpty(subject);
		return Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(subject)));
	}
	#endregion
}
