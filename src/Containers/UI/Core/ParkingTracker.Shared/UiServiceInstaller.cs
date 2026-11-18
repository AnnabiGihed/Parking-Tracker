using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParkingTracker.Shared.Features.MapLayers.Services;
using Pivot.Framework.Tools.DependencyInjection.Abstractions;
using Radzen;

namespace ParkingTracker.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Registers the services of the screens, called from the composition root. The host also calls the installer of
///              the ViewModels project, which holds the view models these screens render.
/// </summary>
public sealed class UiServiceInstaller : IServiceInstaller
{
	#region IServiceInstaller Implementation
	/// <inheritdoc />
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.</exception>
	public void Install(IServiceCollection services, IConfiguration configuration, bool includeConventionBasedRegistration = true)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configuration);

		// One script wrapper per map component: it owns the JavaScript module of that map and is disposed with it.
		services.AddTransient<MapScript>();

		// The Radzen services (dialog, notification, tooltip, theme) that the Radzen components of the screens and the hosts in the layout use.
		services.AddRadzenComponents();
	}
	#endregion
}
