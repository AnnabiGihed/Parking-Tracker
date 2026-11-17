using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParkingTracker.Application.Features.ParkingDetection.Services;
using ParkingTracker.Application.Features.Sectors.Services;
using ParkingTracker.Application.Features.SpotDecisions.Services;
using Pivot.Framework.Application.Behaviors;
using Pivot.Framework.Application.Messaging;
using Pivot.Framework.Tools.DependencyInjection.Abstractions;
using Pivot.Framework.Tools.DependencyInjection;

namespace ParkingTracker.Application;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Registers the Application layer: the parking watcher, alert coordinator and timer restorer, the Pivot mediator with
///              every query and command handler of this assembly, the FluentValidation validators of this assembly and the
///              validation pipeline behavior, called from the composition root.
/// </summary>
public sealed class ApplicationServiceInstaller : BaseServiceInstaller, IServiceInstaller
{
	#region IServiceInstaller Implementation
	/// <inheritdoc />
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.</exception>
	public void Install(IServiceCollection services, IConfiguration configuration, bool includeConventionBasedRegistration = true)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configuration);

		services.AddPivotMediator(options => options
			.RegisterServicesFromAssemblyContaining<ApplicationServiceInstaller>()
			.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>)));

		services.AddValidatorsFromAssembly(AssemblyReference.Assembly);

		// One instance: the outcome of the official-document check is kept in memory between decisions.
		services.AddSingleton<IOfficialListChecker, OfficialListChecker>();
		services.AddTransient<IDecisionContextFactory, DecisionContextFactory>();
		services.AddTransient<IDecisionStepRunner, DecisionStepRunner>();
		services.AddTransient<IAreaStatusProvider, AreaStatusProvider>();

		// One instance each: the watcher holds the detector of the background service, the coordinator and the restorer are
		// stateless; the host reaches all three from the root provider, outside the scope of the web view.
		services.AddSingleton<ParkingWatcher>();
		services.AddSingleton<IParkingAlertCoordinator, ParkingAlertCoordinator>();
		services.AddSingleton<TimerRestorer>();

		if (includeConventionBasedRegistration)
		{
			IncludeConventionBasedRegistrations(services, configuration, [AssemblyReference.Assembly]);
		}
	}
	#endregion
}
