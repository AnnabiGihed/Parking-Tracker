using System.Security.Cryptography;
using ParkingTracker.Application.Features.Sectors.Abstractions;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Features.Shared.Http;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The HTTP calls every live source client makes, with the rules of the first version of the app: one attempt, a
///              limit of time, no cache, and a failure instead of an exception for everything the network can do. The caller's
///              cancellation is the one exception: it is never turned into a failure.
/// </summary>
internal static class SourceHttp
{
	#region Constants
	/// <summary>
	/// The number of bytes that tell a PDF.
	/// </summary>
	private const int PdfSignatureLength = 4;

	/// <summary>
	/// The size of the read buffer, in bytes.
	/// </summary>
	private const int ReadBufferLength = 81920;
	#endregion

	#region Static Instances
	/// <summary>
	/// The first bytes of every PDF.
	/// </summary>
	private static readonly byte[] PdfSignature = "%PDF"u8.ToArray();
	#endregion

	#region Internal Methods
	/// <summary>
	/// Asks for data and reads it with a parser when the status is a success.
	/// </summary>
	/// <typeparam name="T">The parsed type.</typeparam>
	/// <param name="client">The client of the service.</param>
	/// <param name="call">The service, the name of the data and the limit of time.</param>
	/// <param name="address">The request address.</param>
	/// <param name="parse">Reads the body.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The parsed value, or a <c>Timeout</c>, <c>Unavailable</c> or <c>BadResponse</c> failure.</returns>
	internal static Task<Result<T>> GetAsync<T>(HttpClient client, SourceCall call, string address, Func<string, Result<T>> parse, CancellationToken cancellationToken)
	{
		return RunAsync(call, async token =>
		{
			using var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				return Result.Failure<T>(SourceFailures.HttpStatus(call.Source, call.What, (int)response.StatusCode));
			}

			var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
			return parse(body);
		}, cancellationToken);
	}

	/// <summary>
	/// Asks for the headers of a document. Any HTTP status is a result: the check of the document decides what it means.
	/// </summary>
	/// <param name="client">The client of the service.</param>
	/// <param name="call">The service, the name of the data and the limit of time.</param>
	/// <param name="address">The address of the document.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The status and the two headers, or a <c>Timeout</c> or <c>Unavailable</c> failure.</returns>
	internal static Task<Result<DocumentHeaders>> HeadAsync(HttpClient client, SourceCall call, string address, CancellationToken cancellationToken)
	{
		return RunAsync(call, async token =>
		{
			using var request = new HttpRequestMessage(HttpMethod.Head, address);
			using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
			return Result.Success(new DocumentHeaders((int)response.StatusCode, HeaderText(response, "ETag"), HeaderText(response, "Last-Modified")));
		}, cancellationToken);
	}

	/// <summary>
	/// Downloads a document and fingerprints it while it is read, without keeping the bytes.
	/// </summary>
	/// <param name="client">The client of the service.</param>
	/// <param name="call">The service, the name of the data and the limit of time.</param>
	/// <param name="address">The address of the document.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>Whether the content is a PDF and its SHA-256, or a <c>Timeout</c> or <c>Unavailable</c> failure.</returns>
	internal static Task<Result<DownloadedDocument>> DownloadAsync(HttpClient client, SourceCall call, string address, CancellationToken cancellationToken)
	{
		return RunAsync(call, async token =>
		{
			using var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				return Result.Failure<DownloadedDocument>(SourceFailures.HttpStatus(call.Source, call.What, (int)response.StatusCode));
			}

			var body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
			await using var bodyDisposal = body.ConfigureAwait(false);
			return Result.Success(await FingerprintAsync(body, token).ConfigureAwait(false));
		}, cancellationToken);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Runs a call with its timeout and turns every failure into a result.
	/// </summary>
	/// <typeparam name="T">The type of the converted value.</typeparam>
	/// <param name="call">Describes the source and the request, for error messages and the time limit.</param>
	/// <param name="send">Performs the request with the token that enforces the time limit.</param>
	/// <param name="cancellationToken">Cancels the request when the time limit is reached.</param>
	/// <returns>The result of the call.</returns>
	private static async Task<Result<T>> RunAsync<T>(SourceCall call, Func<CancellationToken, Task<Result<T>>> send, CancellationToken cancellationToken)
	{
		using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		limit.CancelAfter(call.Timeout);
		try
		{
			return await send(limit.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// Only the limit fired, not the caller: that is a slow service, not a cancelled check.
			return Result.Failure<T>(SourceFailures.TimedOut(call.Source, call.What));
		}
		catch (Exception exception) when (exception is HttpRequestException or IOException)
		{
			// A connection torn down by the limit surfaces as a network exception, but it is still a slow service.
			return limit.IsCancellationRequested && !cancellationToken.IsCancellationRequested
				? Result.Failure<T>(SourceFailures.TimedOut(call.Source, call.What))
				: Result.Failure<T>(SourceFailures.Unreachable(call.Source, call.What));
		}
	}

	/// <summary>
	/// Reads the document stream once, to hash it and to check its first bytes.
	/// </summary>
	/// <param name="body">The response content stream, read once from start to end.</param>
	/// <param name="cancellationToken">Cancels the request when the time limit is reached.</param>
	/// <returns>The hash, the size and whether the bytes start like a PDF.</returns>
	private static async Task<DownloadedDocument> FingerprintAsync(Stream body, CancellationToken cancellationToken)
	{
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		var signature = new byte[PdfSignatureLength];
		var buffer = new byte[ReadBufferLength];
		var length = 0;
		int read;
		while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
		{
			if (length < PdfSignatureLength)
			{
				buffer.AsSpan(0, Math.Min(read, PdfSignatureLength - length)).CopyTo(signature.AsSpan(length));
			}

			hash.AppendData(buffer, 0, read);
			length += read;
		}

		var isPdf = length > PdfSignatureLength && signature.AsSpan().SequenceEqual(PdfSignature);
		return new DownloadedDocument(isPdf, Convert.ToHexStringLower(hash.GetHashAndReset()));
	}

	/// <summary>
	/// Reads one header of a response as text.
	/// </summary>
	/// <param name="response">The HTTP response.</param>
	/// <param name="name">Name of the HTTP header, for example ETag.</param>
	/// <returns>The header value, or <see langword="null"/> when the header is absent.</returns>
	private static string? HeaderText(HttpResponseMessage response, string name)
	{
		if (response.Headers.NonValidated.TryGetValues(name, out var values) || response.Content.Headers.NonValidated.TryGetValues(name, out values))
		{
			return values.ToString();
		}

		return null;
	}
	#endregion
}
