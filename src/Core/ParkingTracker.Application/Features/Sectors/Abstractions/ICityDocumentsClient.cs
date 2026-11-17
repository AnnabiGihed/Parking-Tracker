using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.Sectors.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the official sector documents (PDFs) of the City of Brussels, used to confirm that the street lists
///              the app ships with are still the published ones.
/// </summary>
public interface ICityDocumentsClient
{
	#region Methods
	/// <summary>
	/// Asks for the headers of a document without downloading it.
	/// </summary>
	/// <param name="url">The address of the document.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The headers, or a failure with a <c>City.*</c> code when the server cannot be reached in time.</returns>
	Task<Result<DocumentHeaders>> GetHeadersAsync(string url, CancellationToken cancellationToken);

	/// <summary>
	/// Downloads a document and reports whether it is a PDF and its SHA-256.
	/// </summary>
	/// <param name="url">The address of the document.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The fingerprint of the content, or a failure with a <c>City.*</c> code and a message such as <c>bruxelles.be HTTP 404</c>.</returns>
	Task<Result<DownloadedDocument>> DownloadAsync(string url, CancellationToken cancellationToken);
	#endregion
}
