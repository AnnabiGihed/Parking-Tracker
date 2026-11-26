using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using ParkingTracker.ApiService;

namespace ParkingTracker.Api.E2E.Tests;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Starts the real API composition in memory for one test, in the environment and on the connection string the test
///              names. The connection string, the proxy key and the three secrets are set explicitly, even when empty, so a
///              variable on the developer machine never reaches the test. A test issuer can replace Google as the source of the
///              signing keys, through the container and not through configuration (ADR-0025); the log of the host is captured.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
	#region Constants
	/// <summary>
	/// The proxy key the tests configure unless they name another or none.
	/// </summary>
	public const string DefaultProxyKey = "test-proxy-key";

	/// <summary>
	/// The issuer of the access tokens the tests configure unless they name another.
	/// </summary>
	public const string TokenIssuer = "https://api.test.example";
	#endregion

	#region Fields
	/// <summary>
	/// The hosting environment of the run.
	/// </summary>
	private readonly string environment;

	/// <summary>
	/// The connection string of the parking database; empty for none.
	/// </summary>
	private readonly string connectionString;

	/// <summary>
	/// The proxy key; empty for none.
	/// </summary>
	private readonly string proxyKey;

	/// <summary>
	/// The test issuer that replaces Google, or <see langword="null"/> to keep the real key source.
	/// </summary>
	private readonly TestGoogleIssuer? googleIssuer;

	/// <summary>
	/// Settings that override the defaults of the factory.
	/// </summary>
	private readonly IReadOnlyDictionary<string, string?> settings;

	/// <summary>
	/// Changes to the container after the host registered its own.
	/// </summary>
	private readonly Action<IServiceCollection>? configureServices;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ApiFactory"/> class.
	/// </summary>
	/// <param name="environment">The hosting environment, for example Development or Production.</param>
	/// <param name="connectionString">The connection string of the parking database; empty for none.</param>
	/// <param name="proxyKey">The proxy key; empty for none. Production refuses to start without one.</param>
	/// <param name="secrets">The signing keys and the subject hash key; generated when omitted.</param>
	/// <param name="googleIssuer">The test issuer that replaces Google; none keeps the real key source.</param>
	/// <param name="settings">Configuration values that override the defaults of the factory.</param>
	/// <param name="configureServices">Changes to the container after the host registered its own.</param>
	/// <param name="useKestrel">Whether to serve through a real Kestrel server on a local port, for what the in-memory server does not do, such as the limit of the request body.</param>
	public ApiFactory(
		string environment,
		string connectionString,
		string proxyKey = DefaultProxyKey,
		ApiSecrets? secrets = null,
		TestGoogleIssuer? googleIssuer = null,
		IReadOnlyDictionary<string, string?>? settings = null,
		Action<IServiceCollection>? configureServices = null,
		bool useKestrel = false)
	{
		this.environment = environment;
		this.connectionString = connectionString;
		this.proxyKey = proxyKey;
		Secrets = secrets ?? ApiSecrets.Generate();
		this.googleIssuer = googleIssuer;
		this.settings = settings ?? new Dictionary<string, string?>(StringComparer.Ordinal);
		this.configureServices = configureServices;
		if (useKestrel)
		{
			UseKestrel();
		}
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the secrets the host runs with.
	/// </summary>
	public ApiSecrets Secrets { get; }

	/// <summary>
	/// Gets the log of the host.
	/// </summary>
	public CapturingLoggerProvider Log { get; } = new();
	#endregion

	#region Protected Methods
	/// <inheritdoc />
	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		_ = builder.UseEnvironment(environment);
		_ = builder.UseSetting("ConnectionStrings:ParkingData", connectionString);
		_ = builder.UseSetting("ReverseProxy:Key", proxyKey);
		_ = builder.UseSetting("Tokens:Issuer", TokenIssuer);
		_ = builder.UseSetting("Tokens:SigningKeys:Current", Secrets.CurrentKeyPem);
		_ = builder.UseSetting("Tokens:SigningKeys:Previous", Secrets.PreviousKeyPem);
		_ = builder.UseSetting("Accounts:SubjectHashKey", Secrets.SubjectHashKey);
		_ = builder.UseSetting("Profiles:Keys:Current", Secrets.ProfileKeyCurrent);
		_ = builder.UseSetting("Profiles:Keys:Previous", Secrets.ProfileKeyPrevious);
		_ = builder.UseSetting("Authentication:Google:Audiences:0", TestGoogleIssuer.Audience);
		foreach (var (key, value) in settings)
		{
			_ = builder.UseSetting(key, value);
		}

		_ = builder.ConfigureLogging(logging => logging.AddProvider(Log));
		_ = builder.ConfigureTestServices(services =>
		{
			if (googleIssuer is not null)
			{
				services.RemoveAll<IConfigurationManager<OpenIdConnectConfiguration>>();
				_ = services.AddSingleton(googleIssuer.CreateConfigurationManager());
			}

			configureServices?.Invoke(services);
		});
	}
	#endregion
}
