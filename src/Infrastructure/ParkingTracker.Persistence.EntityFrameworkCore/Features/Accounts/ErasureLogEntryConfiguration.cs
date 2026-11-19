using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ParkingTracker.Persistence.EntityFrameworkCore.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Maps <see cref="ErasureLogEntry"/> to the table <c>erasure_log</c>: the subject hash is the key, the day a
///              <c>date</c>.
/// </summary>
internal sealed class ErasureLogEntryConfiguration : IEntityTypeConfiguration<ErasureLogEntry>
{
	#region Configuration
	/// <inheritdoc />
	public void Configure(EntityTypeBuilder<ErasureLogEntry> builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder.ToTable("erasure_log");
		builder.HasKey(entry => entry.SubjectHash);
		builder.Property(entry => entry.SubjectHash).HasColumnType("text").ValueGeneratedNever();
		builder.Property(entry => entry.ErasedOn).HasColumnType("date");
	}
	#endregion
}
