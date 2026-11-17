namespace ParkingTracker.Domain.Features.Sectors.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Fingerprint of an official document a street list was verified against; a changed document means the list must be re-verified.
/// </summary>
/// <param name="Url">Address of the official document.</param>
/// <param name="ETag">The <c>ETag</c> header of the verified file, or <see langword="null"/>.</param>
/// <param name="Length">Size in bytes of the verified file.</param>
/// <param name="LastModified">The <c>Last-Modified</c> header of the verified file, or <see langword="null"/>.</param>
/// <param name="Sha256">Lower-case hexadecimal SHA-256 of the verified file.</param>
public sealed record SourceFingerprint(
	string Url,
	string? ETag,
	long Length,
	string? LastModified,
	string Sha256);
