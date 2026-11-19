using Microsoft.Extensions.Options;

namespace ParkingTracker.ApiService.Features.ReverseProxy;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Refuses to start in Production without a proxy key (ADR-0029): without it the API would ignore every forwarded client
///              address and put all callers in one rate-limit bucket. Development and tests may run without a key.
/// </summary>
public sealed class ReverseProxyOptionsValidator : IValidateOptions<ReverseProxyOptions>
{
	#region Fields
	/// <summary>
	/// The hosting environment.
	/// </summary>
	private readonly IHostEnvironment environment;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ReverseProxyOptionsValidator"/> class.
	/// </summary>
	/// <param name="environment">The hosting environment.</param>
	public ReverseProxyOptionsValidator(IHostEnvironment environment)
	{
		this.environment = environment;
	}
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public ValidateOptionsResult Validate(string? name, ReverseProxyOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		if (environment.IsProduction() && string.IsNullOrWhiteSpace(options.Key))
		{
			return ValidateOptionsResult.Fail(
				$"The proxy key '{ReverseProxyOptions.SectionName}:Key' is not configured: mount it as the file ReverseProxy__Key in the secrets folder.");
		}

		return ValidateOptionsResult.Success;
	}
	#endregion
}
