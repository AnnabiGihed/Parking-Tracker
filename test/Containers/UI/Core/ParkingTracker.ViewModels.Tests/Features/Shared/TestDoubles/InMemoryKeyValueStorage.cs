using ParkingTracker.ViewModels.Features.Shared.Settings;

namespace ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A storage of the phone in memory, so that a test of the settings store can see exactly which key received which text.
/// </summary>
internal sealed class InMemoryKeyValueStorage : IKeyValueStorage
{
	#region Properties
	/// <summary>
	/// Gets everything that is saved, by key.
	/// </summary>
	public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

	/// <summary>
	/// Gets how many times a value was read.
	/// </summary>
	public int Reads { get; private set; }
	#endregion

	#region IKeyValueStorage Implementation
	/// <inheritdoc />
	public string? Read(string key)
	{
		Reads++;
		return Values.TryGetValue(key, out var value) ? value : null;
	}

	/// <inheritdoc />
	public void Write(string key, string value) => Values[key] = value;

	/// <inheritdoc />
	public void Delete(string key) => Values.Remove(key);
	#endregion
}
