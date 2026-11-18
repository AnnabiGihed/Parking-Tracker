using System.Collections.Concurrent;
using ParkingTracker.ViewModels.Features.Shared.Settings;

namespace ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A storage of the phone in memory, so that a test of the settings store or of a migration can see exactly which key received which
///              text, and read it back to prove that old values were never changed or deleted. It is safe for concurrent use, because the
///              tests of the migrations race writers.
/// </summary>
internal sealed class InMemoryKeyValueStorage : IKeyValueStorage
{
	#region Fields
	/// <summary>
	/// How many times a value was read.
	/// </summary>
	private int reads;
	#endregion

	#region Properties
	/// <summary>
	/// Gets everything that is saved, by key.
	/// </summary>
	public ConcurrentDictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

	/// <summary>
	/// Gets how many times a value was read.
	/// </summary>
	public int Reads => Volatile.Read(ref reads);
	#endregion

	#region IKeyValueStorage Implementation
	/// <inheritdoc />
	public Task<string?> ReadAsync(string key)
	{
		_ = Interlocked.Increment(ref reads);
		return Task.FromResult<string?>(Values.TryGetValue(key, out var value) ? value : null);
	}

	/// <inheritdoc />
	public Task WriteAsync(string key, string value)
	{
		Values[key] = value;
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task DeleteAsync(string key)
	{
		_ = Values.TryRemove(key, out _);
		return Task.CompletedTask;
	}
	#endregion
}
