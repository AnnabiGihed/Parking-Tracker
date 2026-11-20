using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParkingTracker.Application.Features.AddressSearch.Abstractions;
using ParkingTracker.Application.Features.Sectors.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Infrastructure.Features.AddressSearch;
using ParkingTracker.Infrastructure.Features.PaidHours;
using ParkingTracker.Infrastructure.Features.Sectors;
using ParkingTracker.Infrastructure.Features.Shared.Http;
using ParkingTracker.Infrastructure.Features.Zones;
using Pivot.Framework.Tools.DependencyInjection.Abstractions;
using Pivot.Framework.Tools.DependencyInjection;

namespace ParkingTracker.Infrastructure;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Registers the Infrastructure layer, the implementations of the Application ports, by convention
///              (each concrete class against its matching interface), called from the composition root.
/// </summary>
public sealed class InfrastructureServiceInstaller : BaseServiceInstaller, IServiceInstaller
{
	#region IServiceInstaller Implementation
	/// <inheritdoc />
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.</exception>
	public void Install(IServiceCollection services, IConfiguration configuration, bool includeConventionBasedRegistration = true)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configuration);

		// One instance: the reference data is loaded once and shared.
		services.AddSingleton<IReferenceData, ReferenceDataProvider>();

		if (includeConventionBasedRegistration)
		{
			IncludeConventionBasedRegistrations(services, configuration, [AssemblyReference.Assembly]);
		}

		// After the convention-based registration, so that the typed clients below are the ones that resolve.
		services.AddSingleton(SourceTimeouts.Default);
		AddSourceClient<IParkingBrusselsClient, ParkingBrusselsClient>(services);
		AddSourceClient<IAddressPointsClient, AddressPointsClient>(services);
		AddSourceClient<IParkingRulesClient, ParkingRulesClient>(services);
		AddSourceClient<IMobilityLayersClient, MobilityLayersClient>(services);
		AddSourceClient<ICityDocumentsClient, CityDocumentsClient>(services);

		// One gate for every search: the usage policy of Nominatim is per application, not per client instance.
		services.AddSingleton(_ => new AddressSearchGate(TimeProvider.System));

		// Nominatim answers in the language of the request: French, as the first version of the app asked.
		AddSourceClient<IAddressSearch, NominatimClient>(services, client => client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("fr"));
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Registers a source client as a typed HTTP client behind its port.
	/// </summary>
	/// <typeparam name="TPort">The port the screens and handlers depend on.</typeparam>
	/// <typeparam name="TClient">The HTTP client that implements the port.</typeparam>
	/// <param name="services">The service collection.</param>
	/// <param name="configure">Extra settings of the HTTP client of this source, or <see langword="null"/> for none.</param>
	private static void AddSourceClient<TPort, TClient>(IServiceCollection services, Action<HttpClient>? configure = null)
		where TPort : class
		where TClient : class, TPort
	{
		services.AddHttpClient<TPort, TClient>(client =>
		{
			// The limit of time is applied per call by the client, so that it can tell a timeout from a cancellation.
			client.Timeout = Timeout.InfiniteTimeSpan;
			client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ParkingTracker", "1.0"));
			client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
			configure?.Invoke(client);
		});
	}
	#endregion
}
