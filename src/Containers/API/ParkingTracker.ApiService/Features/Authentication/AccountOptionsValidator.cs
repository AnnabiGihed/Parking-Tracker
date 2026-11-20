using Microsoft.Extensions.Options;

namespace ParkingTracker.ApiService.Features.Authentication;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Refuses to start without a usable subject hash key outside Development, and with a key that is not base64 of 32
///              bytes in any environment. Messages name the configuration key and never carry the key.
/// </summary>
public sealed class AccountOptionsValidator : IValidateOptions<AccountOptions>
{
	#region Fields
	/// <summary>
	/// The hosting environment.
	/// </summary>
	private readonly IHostEnvironment environment;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="AccountOptionsValidator"/> class.
	/// </summary>
	/// <param name="environment">The hosting environment.</param>
	public AccountOptionsValidator(IHostEnvironment environment)
	{
		this.environment = environment;
	}
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public ValidateOptionsResult Validate(string? name, AccountOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		if (string.IsNullOrWhiteSpace(options.SubjectHashKey))
		{
			return environment.IsDevelopment()
				? ValidateOptionsResult.Success
				: ValidateOptionsResult.Fail($"The subject hash key {AccountOptions.SectionName}:SubjectHashKey is not configured: mount it as the file Accounts__SubjectHashKey in the secrets folder.");
		}

		var bytes = new byte[AccountOptions.SubjectHashKeyBytes];
		return Convert.TryFromBase64String(options.SubjectHashKey, bytes, out var written) && written == AccountOptions.SubjectHashKeyBytes
			? ValidateOptionsResult.Success
			: ValidateOptionsResult.Fail($"The subject hash key {AccountOptions.SectionName}:SubjectHashKey must be base64 of {AccountOptions.SubjectHashKeyBytes} bytes.");
	}
	#endregion
}
