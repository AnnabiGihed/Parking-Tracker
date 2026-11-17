namespace ParkingTracker.Application.Features.Sectors.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What the check of an official document needs to know about its downloaded content, without keeping the bytes.
/// </summary>
/// <param name="IsPdf">Whether the content starts with the PDF signature <c>%PDF</c>.</param>
/// <param name="Sha256">The SHA-256 of the content in lower-case hexadecimal.</param>
public sealed record DownloadedDocument(bool IsPdf, string Sha256);
