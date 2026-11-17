using Microsoft.Maui.Storage;
using ParkingTracker.ViewModels.Features.Shared.Settings;

namespace ParkingTracker.App.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The plain storage of the settings store over MAUI <c>Preferences</c>. It uses its own preferences file, so that the
///              Android backup rules can exclude the encrypted card file without losing these values.
/// </summary>
public sealed class PreferencesValueStorage : IKeyValueStorage
{
	#region Constants
	/// <summary>
	/// The name of the preferences file; not the default one, which the encrypted storage may share.
	/// </summary>
	private const string SharedName = "parkingtracker";
	#endregion

	#region IKeyValueStorage Implementation
	/// <inheritdoc />
	public string? Read(string key)
	{
		return Preferences.Default.ContainsKey(key, SharedName) ? Preferences.Default.Get(key, string.Empty, SharedName) : null;
	}

	/// <inheritdoc />
	public void Write(string key, string value)
	{
		Preferences.Default.Set(key, value, SharedName);
	}

	/// <inheritdoc />
	public void Delete(string key)
	{
		Preferences.Default.Remove(key, SharedName);
	}
	#endregion
}
