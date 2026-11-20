using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using Pivot.Framework.Application.Abstractions;
using Pivot.Framework.Authentication.Models;

namespace ParkingTracker.ApiService.Features.Authentication;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Registers the sign-in of the API (ADR-0014): the settings with their start-up checks, the ports of the sign-in
///              (Google ID token validator, access token issuer, subject hasher), JWT bearer authentication against the own keys,
///              the fallback policy that requires an authenticated user on every endpoint that does not allow anonymous access, and
///              Pivot current-user contracts from the <c>sub</c> claim.
/// </summary>
public static class AuthenticationExtensions
{
	#region Public Methods
	/// <summary>
	/// Registers the authentication of the API.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <param name="configuration">The configuration with the Authentication, Tokens and Accounts sections.</param>
	/// <returns>The same service collection.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.</exception>
	public static IServiceCollection AddParkingAuthentication(this IServiceCollection services, IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configuration);
		AddOptions(services, configuration);
		services.TryAddSingleton(TimeProvider.System);
		_ = services.AddHttpClient();
		_ = services.AddSingleton(provider => new SigningKeyRing(provider.GetRequiredService<IOptions<TokenOptions>>().Value.SigningKeys));
		_ = services.AddSingleton<IAccessTokenIssuer, AccessTokenIssuer>();
		_ = services.AddSingleton<ISubjectHasher, SubjectHasher>();
		_ = services.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(CreateGoogleConfigurationManager);
		_ = services.AddSingleton<IGoogleIdTokenValidator, GoogleIdTokenValidator>();
		_ = services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ApiJwtBearerConfigurator>();
		_ = services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
		_ = services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
		_ = services.AddHttpContextAccessor();
		_ = services.AddScoped<AccountCurrentUser>();
		_ = services.AddScoped<ICurrentUser>(provider => provider.GetRequiredService<AccountCurrentUser>());
		_ = services.AddScoped<ICurrentUserProvider>(provider => provider.GetRequiredService<AccountCurrentUser>());
		return services;
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Binds the three settings sections and checks them when the host starts.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <param name="configuration">The configuration.</param>
	private static void AddOptions(IServiceCollection services, IConfiguration configuration)
	{
		_ = services.AddOptions<GoogleAuthenticationOptions>().Bind(configuration.GetSection(GoogleAuthenticationOptions.SectionName)).ValidateOnStart();
		_ = services.AddOptions<TokenOptions>().Bind(configuration.GetSection(TokenOptions.SectionName)).ValidateOnStart();
		_ = services.AddOptions<AccountOptions>().Bind(configuration.GetSection(AccountOptions.SectionName)).ValidateOnStart();
		_ = services.AddSingleton<IValidateOptions<GoogleAuthenticationOptions>, GoogleAuthenticationOptionsValidator>();
		_ = services.AddSingleton<IValidateOptions<TokenOptions>, TokenOptionsValidator>();
		_ = services.AddSingleton<IValidateOptions<AccountOptions>, AccountOptionsValidator>();
	}

	/// <summary>
	/// Creates the manager that reads and caches the metadata document of Google with its signing keys.
	/// </summary>
	/// <param name="provider">The service provider.</param>
	/// <returns>The manager.</returns>
	private static ConfigurationManager<OpenIdConnectConfiguration> CreateGoogleConfigurationManager(IServiceProvider provider)
	{
		var address = provider.GetRequiredService<IOptions<GoogleAuthenticationOptions>>().Value.MetadataAddress;
		var retriever = new HttpDocumentRetriever(provider.GetRequiredService<IHttpClientFactory>().CreateClient())
		{
			RequireHttps = address.StartsWith(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase),
		};
		return new ConfigurationManager<OpenIdConnectConfiguration>(address, new OpenIdConnectConfigurationRetriever(), retriever);
	}
	#endregion
}
