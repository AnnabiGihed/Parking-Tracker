using ParkingTracker.Application.Sources;
using ParkingTracker.Domain.Addressing;
using ParkingTracker.Domain.Decisions;

namespace ParkingTracker.Application.Decisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Checks the official sector documents one by one: the headers first, and the content (SHA-256 of the PDF) only
///              when the headers do not prove the document unchanged. The comparison rules are in
///              <see cref="OfficialListStatus"/>; this class only fetches. The last outcome is kept in memory.
/// </summary>
public sealed class OfficialListChecker : IOfficialListChecker, IDisposable
{
	#region Fields
	private readonly ICityDocumentsClient documents;
	private readonly SemaphoreSlim gate = new(1, 1);
	private string? cachedSectorId;
	private OfficialListStatus? cached;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="OfficialListChecker"/> class.
	/// </summary>
	/// <param name="documents">The port to the City's documents.</param>
	public OfficialListChecker(ICityDocumentsClient documents)
	{
		this.documents = documents;
	}
	#endregion

	#region IOfficialListChecker Implementation
	/// <inheritdoc />
	/// <exception cref="ArgumentNullException"><paramref name="sector"/> is <see langword="null"/>.</exception>
	public async Task<OfficialListStatus> EnsureFreshAsync(SectorModel sector, DateTimeOffset now, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(sector);
		await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (cached is not null && cachedSectorId == sector.Definition.Id && !OfficialListStatus.IsStale(cached, now))
			{
				return cached;
			}

			cached = await CheckAsync(sector, now, cancellationToken).ConfigureAwait(false);
			cachedSectorId = sector.Definition.Id;
			return cached;
		}
		finally
		{
			gate.Release();
		}
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		gate.Dispose();
	}
	#endregion

	#region Private Helpers
	private async Task<OfficialListStatus> CheckAsync(SectorModel sector, DateTimeOffset now, CancellationToken cancellationToken)
	{
		foreach (var source in sector.Definition.Sources)
		{
			string? lastModified = null;
			var headersSaySame = false;
			var headers = await documents.GetHeadersAsync(source.Url, cancellationToken).ConfigureAwait(false);
			if (headers.IsSuccess)
			{
				lastModified = headers.Value.LastModified;
				headersSaySame = OfficialListStatus.HeadersSaySame(source, headers.Value.HttpStatus, headers.Value.ETag, lastModified);
			}

			if (headersSaySame)
			{
				continue;
			}

			var download = await documents.DownloadAsync(source.Url, cancellationToken).ConfigureAwait(false);
			if (download.IsFailure)
			{
				return OfficialListStatus.CouldNotCheck(download.Error.Message, now);
			}

			if (!download.Value.IsPdf)
			{
				return OfficialListStatus.CouldNotCheck(OfficialListStatus.NotAPdfReason, now);
			}

			if (!OfficialListStatus.IsVerifiedContent(source, download.Value.Sha256))
			{
				return OfficialListStatus.ChangedDocument(source.Url, lastModified, now);
			}
		}

		return OfficialListStatus.Unchanged(now);
	}
	#endregion
}
