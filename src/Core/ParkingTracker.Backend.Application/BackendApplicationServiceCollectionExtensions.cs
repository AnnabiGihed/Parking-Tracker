using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using ParkingTracker.Backend.Application.Features.Accounts.Queries.GetMe;
using Pivot.Framework.Application.Behaviors;
using Pivot.Framework.Application.Messaging;

namespace ParkingTracker.Backend.Application;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Registers the server use cases for a host: Pivot's mediator with every command and query handler of this assembly,
///              the validation behavior and the validators of this assembly.
///              The ports that the handlers need are registered by the host and the persistence layer.
/// </summary>
public static class BackendApplicationServiceCollectionExtensions
{
	#region Public Methods
	/// <summary>
	/// Registers the mediator, the handlers, the validation behavior and the validators of this assembly.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <returns>The same service collection.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
	public static IServiceCollection AddBackendApplication(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);
		_ = services.AddPivotMediator(options => options
			.RegisterServicesFromAssemblyContaining<GetMeQueryHandler>()
			.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>)));
		_ = services.AddValidatorsFromAssembly(AssemblyReference.Assembly);
		return services;
	}
	#endregion
}
