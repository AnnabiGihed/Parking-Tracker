using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.ViewModels.Features.Shared;

namespace ParkingTracker.ViewModels.Tests.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="NavigationLinks"/>: the addresses of the first version of the app, the
///              six-decimal positions and the renewal e-mail.
/// </summary>
public sealed class NavigationLinksTests
{
	#region Static Instances
	/// <summary>
	/// The place the tests navigate to.
	/// </summary>
	private static readonly GeoPoint Place = new(50.825, 4.3606);
	#endregion

	#region Link Tests
	/// <summary>
	/// Verifies the Waze and Google Maps addresses.
	/// </summary>
	[Fact]
	public void Links_Place_ShouldKeepTheAddressesOfTheFirstVersion()
	{
		NavigationLinks.Waze(Place).AbsoluteUri.Should().Be("https://waze.com/ul?ll=50.825000,4.360600&navigate=yes");
		NavigationLinks.GoogleMapsDriving(Place).AbsoluteUri.Should().Be("https://www.google.com/maps/dir/?api=1&destination=50.825000,4.360600&travelmode=driving");
		NavigationLinks.GoogleMapsWalking(Place).AbsoluteUri.Should().Be("https://www.google.com/maps/dir/?api=1&destination=50.825000,4.360600&travelmode=walking");
	}

	/// <summary>
	/// Verifies the payment applications and the renewal e-mail without a number.
	/// </summary>
	[Fact]
	public void Links_PayAppsAndRenewal_ShouldKeepTheAddressesOfTheFirstVersion()
	{
		NavigationLinks.PayApps.Select(app => app.Name).Should().Equal("4411", "EasyPark", "Seety");
		NavigationLinks.PayApps.Select(app => app.Address.Host).Should().Equal("www.4411.io", "www.easypark.com", "seety.co");
		NavigationLinks.Renewal(null).AbsoluteUri.Should().Be("mailto:circulation.dem@brucity.be?subject=Renouvellement%20carte%20riverain%20");
	}
	#endregion
}
