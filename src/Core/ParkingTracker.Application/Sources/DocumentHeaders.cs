namespace ParkingTracker.Application.Sources;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The headers of the City of Brussels' answer to a request for the headers of an official document.
/// </summary>
/// <param name="HttpStatus">The HTTP status code.</param>
/// <param name="ETag">The <c>ETag</c> header, or <see langword="null"/>.</param>
/// <param name="LastModified">The <c>Last-Modified</c> header as sent, or <see langword="null"/>.</param>
public sealed record DocumentHeaders(int HttpStatus, string? ETag, string? LastModified);
