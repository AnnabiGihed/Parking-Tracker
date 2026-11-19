using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ParkingTracker.Persistence.EntityFrameworkCore.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Maps <see cref="SignInChallenge"/> to the table <c>sign_in_challenges</c>: the nonce is the key, the expiry a
///              <c>timestamptz</c> that the purge of expired rows filters on.
/// </summary>
internal sealed class SignInChallengeConfiguration : IEntityTypeConfiguration<SignInChallenge>
{
	#region Configuration
	/// <inheritdoc />
	public void Configure(EntityTypeBuilder<SignInChallenge> builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder.ToTable("sign_in_challenges");
		builder.HasKey(challenge => challenge.Nonce);
		builder.Property(challenge => challenge.Nonce).HasColumnType("text").ValueGeneratedNever();
		builder.Property(challenge => challenge.ExpiresAtUtc).HasColumnType("timestamp with time zone");
		builder.HasIndex(challenge => challenge.ExpiresAtUtc);
	}
	#endregion
}
