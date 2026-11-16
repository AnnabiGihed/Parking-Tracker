using System.Text.Json;
using ParkingTracker.Infrastructure.Sources;

namespace ParkingTracker.Infrastructure.Tests.Parity;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : One HTTP answer recorded by <c>tools/parity/record.mjs</c>: status, the two headers the City check reads, the
///              body, and for a downloaded document its SHA-256 and whether it is a PDF.
/// </summary>
/// <param name="Status">The HTTP status code.</param>
/// <param name="ETag">The <c>ETag</c> header, or <see langword="null"/>.</param>
/// <param name="LastModified">The <c>Last-Modified</c> header, or <see langword="null"/>.</param>
/// <param name="Body">The body text, or <see langword="null"/> for a HEAD request or a document.</param>
/// <param name="BodySha256">The SHA-256 of a downloaded document, or <see langword="null"/>.</param>
/// <param name="IsPdf">Whether a downloaded document starts with <c>%PDF</c>.</param>
internal sealed record RecordedResponse(int Status, string? ETag, string? LastModified, string? Body, string? BodySha256, bool IsPdf)
{
	#region Factory Methods
	/// <summary>
	/// Reads a recorded response from the JSON file the recorder wrote.
	/// </summary>
	/// <param name="root">The root of the file.</param>
	/// <returns>The response.</returns>
	public static RecordedResponse From(JsonElement root)
	{
		var headers = root.TryGetProperty("headers", out var h) ? h : default;
		var body = root.TryGetProperty("body", out var json) ? json.GetRawText() : null;
		body ??= root.TryGetProperty("bodyText", out var text) ? text.GetString() : null;
		return new RecordedResponse(
			root.GetProperty("status").GetInt32(),
			Header(headers, "etag"),
			Header(headers, "last-modified"),
			body,
			root.TryGetProperty("bodySha256", out var sha) ? sha.GetString() : null,
			root.TryGetProperty("isPdf", out var pdf) && pdf.GetBoolean());
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Turns the response into what the parsers read.
	/// </summary>
	/// <returns>The status and the body.</returns>
	public SourceResponse ToSourceResponse() => new(Status, Body ?? string.Empty);
	#endregion

	#region Private Helpers
	private static string? Header(JsonElement headers, string name)
	{
		return headers.ValueKind == JsonValueKind.Object && headers.TryGetProperty(name, out var value) ? value.GetString() : null;
	}
	#endregion
}
