using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pivot.Framework.Application.Behaviors;
using Pivot.Framework.Application.Messaging;
using Pivot.Framework.Tools.DependencyInjection;
using Pivot.Framework.Tools.DependencyInjection.Abstractions;

namespace ParkingTracker.Application;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Registers the Application layer: the Pivot mediator with every query and command handler of this
///              assembly, the FluentValidation validators of this assembly and the validation pipeline behavior, called from the composition root.
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

		if (includeConventionBasedRegistration)
		{
			IncludeConventionBasedRegistrations(services, configuration, [AssemblyReference.Assembly]);
		}
	}
	#endregion
}
