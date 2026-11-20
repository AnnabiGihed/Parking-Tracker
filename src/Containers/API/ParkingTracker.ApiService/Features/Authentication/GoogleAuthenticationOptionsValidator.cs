using Microsoft.Extensions.Options;

namespace ParkingTracker.ApiService.Features.Authentication;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Refuses to start outside Development when the Google settings point anywhere but Google: the metadata address and
///              the issuers must be Google, and at least one audience must be configured. A test issuer is therefore possible only
///              in Development and in tests, never in a deployed environment (ADR-0025).
/// </summary>
public sealed class GoogleAuthenticationOptionsValidator : IValidateOptions<GoogleAuthenticationOptions>
{
	#region Fields
	/// <summary>
	/// The hosting environment.
	/// </summary>
	private readonly IHostEnvironment environment;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="GoogleAuthenticationOptionsValidator"/> class.
	/// </summary>
	/// <param name="environment">The hosting environment.</param>
	public GoogleAuthenticationOptionsValidator(IHostEnvironment environment)
	{
		this.environment = environment;
	}
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public ValidateOptionsResult Validate(string? name, GoogleAuthenticationOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		if (environment.IsDevelopment())
		{
			return ValidateOptionsResult.Success;
		}

		var failures = new List<string>();
		if (!string.Equals(options.MetadataAddress, GoogleAuthenticationOptions.GoogleMetadataAddress, StringComparison.Ordinal))
		{
			failures.Add($"{GoogleAuthenticationOptions.SectionName}:MetadataAddress must be the metadata address of Google outside Development.");
		}

		var expectedIssuers = new[] { GoogleAuthenticationOptions.GoogleIssuer, GoogleAuthenticationOptions.GoogleIssuerWithScheme };
		if (!options.ValidIssuers.Order(StringComparer.Ordinal).SequenceEqual(expectedIssuers.Order(StringComparer.Ordinal), StringComparer.Ordinal))
		{
			failures.Add($"{GoogleAuthenticationOptions.SectionName}:ValidIssuers must be the issuers of Google outside Development.");
		}

		if (!options.Audiences.Any(audience => !string.IsNullOrWhiteSpace(audience)))
		{
			failures.Add($"{GoogleAuthenticationOptions.SectionName}:Audiences needs at least one client identifier outside Development.");
		}

		return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
	}
	#endregion
}
