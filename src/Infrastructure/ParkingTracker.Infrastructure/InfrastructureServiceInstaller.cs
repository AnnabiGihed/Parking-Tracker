using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParkingTracker.Application.ReferenceData;
using ParkingTracker.Infrastructure.ReferenceData;
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
	}
	#endregion
}
