using ParkingTracker.Application.Sources;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Sources;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Reads the official documents of the City of Brussels over HTTP. It only fetches: the headers (HEAD), or the
///              document with its SHA-256. Headers are a hint; deciding whether the document changed is the job of the
///              official-list checker.
/// </summary>
internal sealed class CityDocumentsClient : ICityDocumentsClient
{
	#region Constants
	private const string What = "bruxelles.be";
	#endregion

	#region Fields
	private readonly HttpClient client;
	private readonly SourceTimeouts timeouts;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="CityDocumentsClient"/> class.
	/// </summary>
	/// <param name="client">The HTTP client of the service.</param>
	/// <param name="timeouts">The limits of time.</param>
	public CityDocumentsClient(HttpClient client, SourceTimeouts timeouts)
	{
		this.client = client;
		this.timeouts = timeouts;
	}
	#endregion

	#region ICityDocumentsClient Implementation
	/// <inheritdoc />
	public Task<Result<DocumentHeaders>> GetHeadersAsync(string url, CancellationToken cancellationToken)
	{
		return SourceHttp.HeadAsync(client, new SourceCall(SourceKind.City, What, timeouts.Request), url, cancellationToken);
	}

	/// <inheritdoc />
	public Task<Result<DownloadedDocument>> DownloadAsync(string url, CancellationToken cancellationToken)
	{
		return SourceHttp.DownloadAsync(client, new SourceCall(SourceKind.City, What, timeouts.Download), url, cancellationToken);
	}
	#endregion
}
