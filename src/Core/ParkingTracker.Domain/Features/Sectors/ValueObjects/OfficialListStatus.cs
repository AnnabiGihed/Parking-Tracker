using System.Globalization;

namespace ParkingTracker.Domain.Features.Sectors.ValueObjects;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Whether the City of Brussels' official sector documents are unchanged, and when that was checked. The rules to
///              compare one document with the verified one are here too, so that the check itself is only fetching.
/// </summary>
/// <param name="State">Outcome of the official-list check.</param>
/// <param name="CheckedAt">When the check was made.</param>
/// <param name="Document">The address of the changed document, for <see cref="OfficialListState.Changed"/>.</param>
/// <param name="DocumentDate">The <c>Last-Modified</c> value the City sent for the changed document, or <see langword="null"/>.</param>
/// <param name="Why">Why the check failed, for <see cref="OfficialListState.Unverified"/>.</param>
public sealed record OfficialListStatus(OfficialListState State, DateTimeOffset CheckedAt, string? Document, string? DocumentDate, string? Why)
{
	#region Constants
	/// <summary>
	/// The reason given when the City answers a document request with something that is not a PDF.
	/// </summary>
	public const string NotAPdfReason = "the City sent something that is not the PDF";

	/// <summary>
	/// How long an unchanged result is trusted.
	/// </summary>
	public static readonly TimeSpan RecheckInterval = TimeSpan.FromHours(6);

	/// <summary>
	/// How long a result that is not "unchanged" is kept before the next attempt.
	/// </summary>
	public static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(1);
	#endregion

	#region Properties
	/// <summary>
	/// Gets the sentence that explains why the street lists cannot be used, or <see langword="null"/> when the documents are unchanged.
	/// </summary>
	public string? Problem => State switch
	{
		OfficialListState.Ok => null,
		OfficialListState.Changed => $"The City of Brussels changed its official sector document{(string.IsNullOrEmpty(DocumentDate) ? string.Empty : " on " + DocumentDate)}. This app's street list may be outdated: update the app before relying on it.",
		_ => $"Could not confirm that the City's official sector list is unchanged ({(string.IsNullOrEmpty(Why) ? "not checked yet" : Why)}).",
	};
	#endregion

	#region Factory Methods
	/// <summary>
	/// Creates the result "every document is unchanged".
	/// </summary>
	/// <param name="now">When the check was made.</param>
	/// <returns>Status stating that the official list is unchanged.</returns>
	public static OfficialListStatus Unchanged(DateTimeOffset now) => new(OfficialListState.Ok, now, null, null, null);

	/// <summary>
	/// Creates the result "a document changed".
	/// </summary>
	/// <param name="document">The address of the changed document.</param>
	/// <param name="documentDate">The <c>Last-Modified</c> value the City sent, or <see langword="null"/>.</param>
	/// <param name="now">When the check was made.</param>
	/// <returns>Status carrying the new document and its date.</returns>
	public static OfficialListStatus ChangedDocument(string document, string? documentDate, DateTimeOffset now)
	{
		return new OfficialListStatus(OfficialListState.Changed, now, document, documentDate, null);
	}

	/// <summary>
	/// Creates the result "could not check".
	/// </summary>
	/// <param name="why">The reason, in words.</param>
	/// <param name="now">When the check was attempted.</param>
	/// <returns>Status stating that the check could not be completed.</returns>
	public static OfficialListStatus CouldNotCheck(string why, DateTimeOffset now) => new(OfficialListState.Unverified, now, null, null, why);
	#endregion

	#region Public Methods
	/// <summary>
	/// Determines whether the check has to be made again: an unchanged result is trusted for 6 hours, any other result for 1 minute.
	/// </summary>
	/// <param name="status">The last status, or <see langword="null"/> when none was made yet.</param>
	/// <param name="now">The current instant.</param>
	/// <returns><see langword="true"/> when there is no status or it is too old.</returns>
	public static bool IsStale(OfficialListStatus? status, DateTimeOffset now)
	{
		return status is null || now - status.CheckedAt > (status.State == OfficialListState.Ok ? RecheckInterval : RetryInterval);
	}

	/// <summary>
	/// Tells whether the headers of a response prove that a document is the verified one: the response succeeded and its
	/// <c>Last-Modified</c> date or its <c>ETag</c> equals the recorded one. Headers can be reshaped on the way, so a
	/// mismatch only means the content has to be compared.
	/// </summary>
	/// <param name="source">The recorded fingerprint of the document.</param>
	/// <param name="httpStatus">The HTTP status code.</param>
	/// <param name="eTag">The <c>ETag</c> header, or <see langword="null"/>.</param>
	/// <param name="lastModified">The <c>Last-Modified</c> header, or <see langword="null"/>.</param>
	/// <returns><see langword="true"/> when the headers say the document is unchanged.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
	public static bool HeadersSaySame(SourceFingerprint source, int httpStatus, string? eTag, string? lastModified)
	{
		ArgumentNullException.ThrowIfNull(source);
		var date = ParseDate(lastModified);
		var dateSame = date is not null && date == ParseDate(source.LastModified);
		var tagSame = !string.IsNullOrEmpty(eTag) && string.Equals(NormalizeTag(eTag), NormalizeTag(source.ETag), StringComparison.Ordinal);
		return httpStatus is >= 200 and < 300 && (dateSame || tagSame);
	}

	/// <summary>
	/// Tells whether a downloaded document is the verified one.
	/// </summary>
	/// <param name="source">The recorded fingerprint of the document.</param>
	/// <param name="sha256">The SHA-256 of the downloaded bytes in lower-case hexadecimal.</param>
	/// <returns><see langword="true"/> when the content equals the verified content.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
	public static bool IsVerifiedContent(SourceFingerprint source, string sha256)
	{
		ArgumentNullException.ThrowIfNull(source);
		return string.Equals(sha256, source.Sha256, StringComparison.Ordinal);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Normalizes an entity tag so that two spellings of the same tag compare equal.
	/// </summary>
	/// <param name="tag">Entity tag value, possibly weak or quoted.</param>
	/// <returns>The normalized tag.</returns>
	private static string NormalizeTag(string? tag)
	{
		var value = (tag ?? string.Empty).Trim();
		if (value.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
		{
			value = value[2..];
		}

		return value.Replace("\"", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();
	}

	/// <summary>
	/// Parses an HTTP date.
	/// </summary>
	/// <param name="text">The text to read.</param>
	/// <returns>The date, or <see langword="null"/> when the text is empty or not a date.</returns>
	private static DateTimeOffset? ParseDate(string? text)
	{
		return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;
	}
	#endregion
}
