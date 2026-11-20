using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ParkingTracker.ViewModels.Features.Car;
using ParkingTracker.ViewModels.Features.Card;
using ParkingTracker.ViewModels.Features.MapLayers;
using ParkingTracker.ViewModels.Features.Menu;
using ParkingTracker.ViewModels.Features.Places;
using ParkingTracker.ViewModels.Features.Shared;
using ParkingTracker.ViewModels.Features.SpotCheck;
using ParkingTracker.ViewModels.Features.Welcome;
using Pivot.Framework.Tools.DependencyInjection.Abstractions;

namespace ParkingTracker.ViewModels;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Registers the view models of the screens, called from the composition root. The view models are scoped: a
///              Blazor Hybrid app has one scope for its whole life, so every screen shares the same instance of each.
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

		services.TryAddSingleton(TimeProvider.System);

		// A singleton, unlike the view models: the host asks it when the back button is pressed, outside the Blazor scope.
		services.TryAddSingleton<IBackNavigation, BackNavigation>();
		services.AddScoped<ToastViewModel>();
		services.AddScoped<LocationPermissionViewModel>();
		services.AddScoped<WelcomeViewModel>();
		services.AddScoped<CardViewModel>();
		services.AddScoped<NearestSpotViewModel>();
		services.AddScoped<SpotCheckViewModel>();
		services.AddScoped<SearchViewModel>();
		services.AddScoped<CarViewModel>();
		services.AddScoped<PlacesViewModel>();
		services.AddScoped<PlacePickerViewModel>();
		services.AddScoped<FavouritesViewModel>();
		services.AddScoped<MenuViewModel>();
		services.AddScoped<ParkedOnScreenResponder>();
		services.AddScoped<MapLayersViewModel>();
	}
	#endregion
}
