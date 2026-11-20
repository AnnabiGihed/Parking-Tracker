using ParkingTracker.Backend.Application.Features.Accounts.Services;

namespace ParkingTracker.Backend.Application.Tests.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="RandomToken"/>: the shape of the random secrets and the stored hash of a refresh token.
/// </summary>
public sealed class RandomTokenTests
{
	#region Random Tests
	/// <summary>
	/// Verifies that a token is 32 random bytes in base64url without padding, and that two tokens differ.
	/// </summary>
	[Fact]
	public void New_ShouldBe32BytesOfBase64UrlAndUnique()
	{
		var tokens = Enumerable.Range(0, 100).Select(_ => RandomToken.New()).ToList();

		tokens.Should().OnlyContain(token => token.Length == 43 && token.All(character => char.IsAsciiLetterOrDigit(character) || character == '-' || character == '_'));
		tokens.Distinct(StringComparer.Ordinal).Should().HaveCount(100);
	}
	#endregion

	#region Hash Tests
	/// <summary>
	/// Verifies the hash against the known SHA-256 of "abc": lowercase hexadecimal, 64 characters.
	/// </summary>
	[Fact]
	public void Hash_ShouldBeTheLowercaseHexSha256()
	{
		var hash = RandomToken.Hash("abc");

		hash.Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
	}
	#endregion
}
