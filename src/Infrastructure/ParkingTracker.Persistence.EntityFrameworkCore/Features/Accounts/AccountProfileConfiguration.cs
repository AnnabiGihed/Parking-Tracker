using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkingTracker.Domain.Features.Accounts.Aggregates;
using ParkingTracker.Domain.Features.Accounts.ValueObjects;

namespace ParkingTracker.Persistence.EntityFrameworkCore.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Maps <see cref="AccountProfile"/> to <c>account_profiles</c> (ADR-0032): the account identifier is the primary key and
///              a foreign key to <c>user_accounts</c> that cascades on delete, the revision is the concurrency token (so a save that is
///              based on a revision that was replaced meanwhile fails), the payload is <c>bytea</c> and the day is <c>date</c>. The
///              aggregate's own <c>Version</c> is not stored, because the revision does that job.
/// </summary>
internal sealed class AccountProfileConfiguration : IEntityTypeConfiguration<AccountProfile>
{
	#region Configuration
	/// <inheritdoc />
	public void Configure(EntityTypeBuilder<AccountProfile> builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder.ToTable("account_profiles");
		builder.HasKey(profile => profile.Id);
		builder.Property(profile => profile.Id).HasConversion(id => id.Value, value => new UserAccountId(value)).ValueGeneratedNever();
		builder.Property(profile => profile.Revision).IsConcurrencyToken();
		builder.Property(profile => profile.SchemaVersion);
		builder.Property(profile => profile.KeyId).HasColumnType("text").HasMaxLength(EncryptedProfile.KeyIdLength);
		builder.Property(profile => profile.Payload).HasColumnType("bytea");
		builder.Property(profile => profile.UpdatedOn).HasColumnType("date");
		builder.Ignore(profile => profile.Version);
		builder.Ignore(profile => profile.NextRevision);
		builder.HasOne<UserAccount>().WithOne().HasForeignKey<AccountProfile>(profile => profile.Id).IsRequired().OnDelete(DeleteBehavior.Cascade);
	}
	#endregion
}
