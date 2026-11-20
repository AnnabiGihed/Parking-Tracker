using ParkingTracker.Domain.Features.AddressSearch.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Features.AddressSearch;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Keeps the app inside the usage policy of Nominatim: at most one request per second, and a question that was
///              already answered successfully is answered again from memory for the rest of the session. It is a singleton, because the
///              search client is created for each use.
/// </summary>
internal sealed class AddressSearchGate : IDisposable
{
	#region Constants
	/// <summary>
	/// The most answers kept; the memory is emptied when it is full, as a session asks few different questions.
	/// </summary>
	internal const int MaxCached = 32;
	#endregion

	#region Static Instances
	/// <summary>
	/// The shortest time between two requests to the service, as its usage policy asks.
	/// </summary>
	private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);
	#endregion

	#region Fields
	/// <summary>
	/// The answers already received, by question.
	/// </summary>
	private readonly Dictionary<string, IReadOnlyList<AddressMatch>> answers = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Guards <see cref="answers"/>, which is read without waiting for the turn.
	/// </summary>
	private readonly Lock answersLock = new();

	/// <summary>
	/// Lets one request at a time through.
	/// </summary>
	private readonly SemaphoreSlim turn = new(1, 1);

	/// <summary>
	/// The clock that measures the time between requests.
	/// </summary>
	private readonly TimeProvider clock;

	/// <summary>
	/// The moment the last request was sent, or <see langword="null"/> before the first.
	/// </summary>
	private DateTimeOffset? lastRequestAt;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="AddressSearchGate"/> class.
	/// </summary>
	/// <param name="clock">The clock that measures the time between requests.</param>
	public AddressSearchGate(TimeProvider clock)
	{
		this.clock = clock;
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		turn.Dispose();
	}
	#endregion

	#region Internal Methods
	/// <summary>
	/// Answers a question from memory, or waits for the turn and sends it.
	/// </summary>
	/// <param name="text">What the user typed.</param>
	/// <param name="limit">The most matches asked for.</param>
	/// <param name="send">Sends the request to the service.</param>
	/// <param name="cancellationToken">Cancels the wait.</param>
	/// <returns>The remembered matches, or the answer of the service; only a success is remembered.</returns>
	internal async Task<Result<IReadOnlyList<AddressMatch>>> RunAsync(string text, int limit, Func<Task<Result<IReadOnlyList<AddressMatch>>>> send, CancellationToken cancellationToken)
	{
		var key = $"{limit}|{text}";
		if (Recall(key) is { } early)
		{
			return Result.Success(early);
		}

		await turn.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			// The question may have been answered while this call waited for its turn.
			if (Recall(key) is { } remembered)
			{
				return Result.Success(remembered);
			}

			await WaitForIntervalAsync(cancellationToken).ConfigureAwait(false);
			lastRequestAt = clock.GetUtcNow();
			var result = await send().ConfigureAwait(false);
			Remember(key, result);
			return result;
		}
		finally
		{
			turn.Release();
		}
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Waits until a second has passed since the last request.
	/// </summary>
	/// <param name="cancellationToken">Cancels the wait.</param>
	/// <returns>A task that completes when the request may be sent.</returns>
	private async Task WaitForIntervalAsync(CancellationToken cancellationToken)
	{
		if (lastRequestAt is not { } last)
		{
			return;
		}

		var remaining = last + MinInterval - clock.GetUtcNow();
		if (remaining > TimeSpan.Zero)
		{
			await Task.Delay(remaining, clock, cancellationToken).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Looks a question up in memory.
	/// </summary>
	/// <param name="key">The question.</param>
	/// <returns>The remembered matches, or <see langword="null"/> when the question was not answered yet.</returns>
	private IReadOnlyList<AddressMatch>? Recall(string key)
	{
		lock (answersLock)
		{
			return answers.GetValueOrDefault(key);
		}
	}

	/// <summary>
	/// Keeps the answer of a question when it succeeded.
	/// </summary>
	/// <param name="key">The question.</param>
	/// <param name="result">The answer of the service.</param>
	private void Remember(string key, Result<IReadOnlyList<AddressMatch>> result)
	{
		if (result.IsFailure)
		{
			return;
		}

		lock (answersLock)
		{
			if (answers.Count >= MaxCached)
			{
				answers.Clear();
			}

			answers[key] = result.Value;
		}
	}
	#endregion
}
