using Microsoft.Extensions.Options;
using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;

namespace ParkingTracker.ApiService.Features.Profiles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Registers the profile encryption of the API (ADR-0032): the settings with their start-up check, so that a missing or
///              unusable key stops the host outside Development, and the cipher port.
/// </summary>
public static class ProfileExtensions
{
	#region Public Methods
	/// <summary>
	/// Registers the profile cipher and its settings.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <param name="configuration">The configuration with the Profiles section.</param>
	/// <returns>The same service collection.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.</exception>
	public static IServiceCollection AddParkingProfiles(this IServiceCollection services, IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configuration);
		_ = services.AddOptions<ProfileOptions>().Bind(configuration.GetSection(ProfileOptions.SectionName)).ValidateOnStart();
		_ = services.AddSingleton<IValidateOptions<ProfileOptions>, ProfileOptionsValidator>();
		_ = services.AddSingleton<IProfileCipher, ProfileCipher>();
		return services;
	}
	#endregion
}
