using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using ParkingTracker.Application.ReferenceData;
using ParkingTracker.Application.Sources;
using ParkingTracker.Infrastructure.ReferenceData;
using ParkingTracker.Infrastructure.Sources;
using Pivot.Framework.Tools.DependencyInjection;
using Pivot.Framework.Tools.DependencyInjection.Abstractions;

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
	}
	#endregion

	#region Private Helpers
	private static void AddSourceClient<TPort, TClient>(IServiceCollection services)
		where TPort : class
		where TClient : class, TPort
	{
		services.AddHttpClient<TPort, TClient>(client =>
		{
			// The limit of time is applied per call by the client, so that it can tell a timeout from a cancellation.
			client.Timeout = Timeout.InfiniteTimeSpan;
			client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ParkingTracker", "1.0"));
			client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
		});
	}
	#endregion
}
