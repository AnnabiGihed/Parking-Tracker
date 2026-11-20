using Microsoft.Extensions.Options;

namespace ParkingTracker.ApiService.Features.Authentication;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Refuses to start with unusable token settings: outside Development the issuer must be an https address and the
///              current signing key must be present; a key that is given, in any environment, must be a P-256 private key in PEM.
///              Messages name the configuration key and never carry the key.
/// </summary>
public sealed class TokenOptionsValidator : IValidateOptions<TokenOptions>
{
	#region Fields
	/// <summary>
	/// The hosting environment.
	/// </summary>
	private readonly IHostEnvironment environment;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="TokenOptionsValidator"/> class.
	/// </summary>
	/// <param name="environment">The hosting environment.</param>
	public TokenOptionsValidator(IHostEnvironment environment)
	{
		this.environment = environment;
	}
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public ValidateOptionsResult Validate(string? name, TokenOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		var failures = new List<string>();
		var strict = !environment.IsDevelopment();
		if (strict && !(Uri.TryCreate(options.Issuer, UriKind.Absolute, out var issuer) && string.Equals(issuer.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)))
		{
			failures.Add($"The issuer {TokenOptions.SectionName}:Issuer must be an https address.");
		}

		if (strict && string.IsNullOrWhiteSpace(options.SigningKeys.Current))
		{
			failures.Add($"The signing key {TokenOptions.SectionName}:SigningKeys:Current is not configured: mount it as the file Tokens__SigningKeys__Current in the secrets folder.");
		}

		CheckKey(options.SigningKeys.Current, "Current", failures);
		CheckKey(options.SigningKeys.Previous, "Previous", failures);
		return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Adds a failure when a given key is not a usable P-256 private key.
	/// </summary>
	/// <param name="pem">The key text; blank means not given.</param>
	/// <param name="which">The name of the key in the configuration.</param>
	/// <param name="failures">The failures found so far.</param>
	private static void CheckKey(string? pem, string which, List<string> failures)
	{
		if (string.IsNullOrWhiteSpace(pem))
		{
			return;
		}

		var problem = SigningKeyRing.Check(pem);
		if (problem is not null)
		{
			failures.Add($"The signing key {TokenOptions.SectionName}:SigningKeys:{which} is not usable: {problem}");
		}
	}
	#endregion
}
