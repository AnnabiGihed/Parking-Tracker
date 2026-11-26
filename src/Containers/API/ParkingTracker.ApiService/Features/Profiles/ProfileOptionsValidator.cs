using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace ParkingTracker.ApiService.Features.Profiles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Refuses to start without a profile key outside Development, and with a current or previous key that is not base64 of
///              32 bytes in any environment, and a previous key that is the current key (the same key identifier, so a rotation that
///              changed nothing). Messages name the configuration key and never carry the key.
/// </summary>
public sealed class ProfileOptionsValidator : IValidateOptions<ProfileOptions>
{
	#region Fields
	/// <summary>
	/// The hosting environment.
	/// </summary>
	private readonly IHostEnvironment environment;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ProfileOptionsValidator"/> class.
	/// </summary>
	/// <param name="environment">The hosting environment.</param>
	public ProfileOptionsValidator(IHostEnvironment environment)
	{
		this.environment = environment;
	}
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public ValidateOptionsResult Validate(string? name, ProfileOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		var failures = new List<string>();
		if (string.IsNullOrWhiteSpace(options.Keys.Current) && !environment.IsDevelopment())
		{
			failures.Add($"The profile key {ProfileOptions.SectionName}:Keys:Current is not configured: mount it as the file Profiles__Keys__Current in the secrets folder.");
		}

		CheckKey(options.Keys.Current, "Current", failures);
		CheckKey(options.Keys.Previous, "Previous", failures);
		if (failures.Count == 0 && IsSameKey(options.Keys.Current, options.Keys.Previous))
		{
			failures.Add($"The profile key {ProfileOptions.SectionName}:Keys:Previous is the same key as {ProfileOptions.SectionName}:Keys:Current; leave it empty or give the key of the rotation before.");
		}

		return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Tells whether two configured keys have the same key identifier, which is what the cipher tells them apart by.
	/// </summary>
	/// <param name="current">The current key; blank for none.</param>
	/// <param name="previous">The previous key; blank for none.</param>
	/// <returns><see langword="true"/> when both are present, valid and the same key.</returns>
	private static bool IsSameKey(string? current, string? previous)
	{
		return !string.IsNullOrWhiteSpace(current) && !string.IsNullOrWhiteSpace(previous)
			&& SHA256.HashData(Convert.FromBase64String(current)).AsSpan(0, 4).SequenceEqual(SHA256.HashData(Convert.FromBase64String(previous)).AsSpan(0, 4));
	}

	/// <summary>
	/// Checks that a key that is present is base64 of 32 bytes.
	/// </summary>
	/// <param name="key">The configured key; blank for none.</param>
	/// <param name="which">The name of the key in the configuration.</param>
	/// <param name="failures">Receives the problem, without the key.</param>
	private static void CheckKey(string? key, string which, List<string> failures)
	{
		if (string.IsNullOrWhiteSpace(key))
		{
			return;
		}

		var bytes = new byte[ProfileOptions.KeyBytes];
		if (!Convert.TryFromBase64String(key, bytes, out var written) || written != ProfileOptions.KeyBytes)
		{
			failures.Add($"The profile key {ProfileOptions.SectionName}:Keys:{which} must be base64 of {ProfileOptions.KeyBytes} bytes.");
		}
	}
	#endregion
}
