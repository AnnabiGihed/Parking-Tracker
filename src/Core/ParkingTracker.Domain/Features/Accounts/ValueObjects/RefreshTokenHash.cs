using ParkingTracker.Domain.Features.Accounts.Errors;
using ParkingTracker.Domain.Features.Accounts.Services;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Domain.Features.Accounts.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The SHA-256 hash of a refresh token; the token itself is never stored. It is written as 64 lowercase hexadecimal characters.
/// </summary>
public sealed record RefreshTokenHash
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="RefreshTokenHash"/> class.
	/// </summary>
	/// <param name="value">The 64 lowercase hexadecimal characters.</param>
	private RefreshTokenHash(string value)
	{
		Value = value;
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the hash as 64 lowercase hexadecimal characters.
	/// </summary>
	public string Value { get; }
	#endregion

	#region Factory Methods
	/// <summary>
	/// Creates the hash from its hexadecimal text.
	/// </summary>
	/// <param name="text">The hash as 64 lowercase hexadecimal characters.</param>
	/// <returns>The hash, or a failure when the text is missing or does not have that shape.</returns>
	public static Result<RefreshTokenHash> Create(string? text)
	{
		return HashText.IsLowercaseHex256(text)
			? new RefreshTokenHash(text!)
			: Result.Failure<RefreshTokenHash>(AccountErrors.RefreshTokenHashInvalid);
	}
	#endregion
}
