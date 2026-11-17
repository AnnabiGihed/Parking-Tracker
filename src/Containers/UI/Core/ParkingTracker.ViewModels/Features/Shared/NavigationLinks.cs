using System.Globalization;
using ParkingTracker.Domain.Features.Shared.ValueObjects;

namespace ParkingTracker.ViewModels.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Builds the addresses the screen opens outside the app: Waze, Google Maps for driving and walking, the payment
///              applications and the e-mail to renew a card. The addresses are the ones of the first version of the app.
/// </summary>
public static class NavigationLinks
{
	#region Constants
	/// <summary>
	/// The address of the 4411 parking application.
	/// </summary>
	private const string App4411Address = "https://www.4411.io";

	/// <summary>
	/// The address of the EasyPark parking application.
	/// </summary>
	private const string EasyParkAddress = "https://www.easypark.com";

	/// <summary>
	/// The address of the Seety parking application.
	/// </summary>
	private const string SeetyAddress = "https://seety.co";

	/// <summary>
	/// The e-mail address of the City of Brussels service that renews residents' cards.
	/// </summary>
	public const string RenewalAddress = "circulation.dem@brucity.be";
	#endregion

	#region Static Instances
	/// <summary>
	/// The payment applications offered on a paid spot, in the order they are shown.
	/// </summary>
	public static readonly IReadOnlyList<PayApp> PayApps =
	[
		new("4411", new Uri(App4411Address)),
		new("EasyPark", new Uri(EasyParkAddress)),
		new("Seety", new Uri(SeetyAddress)),
	];
	#endregion

	#region Public Methods
	/// <summary>
	/// Builds the Waze address that starts navigation to a place.
	/// </summary>
	/// <param name="destination">The place.</param>
	/// <returns>The Waze address.</returns>
	public static Uri Waze(GeoPoint destination) => new($"https://waze.com/ul?ll={Pair(destination)}&navigate=yes");

	/// <summary>
	/// Builds the Google Maps address that gives driving directions to a place.
	/// </summary>
	/// <param name="destination">The place.</param>
	/// <returns>The Google Maps address.</returns>
	public static Uri GoogleMapsDriving(GeoPoint destination) => new($"https://www.google.com/maps/dir/?api=1&destination={Pair(destination)}&travelmode=driving");

	/// <summary>
	/// Builds the Google Maps address that gives walking directions to a place.
	/// </summary>
	/// <param name="destination">The place.</param>
	/// <returns>The Google Maps address.</returns>
	public static Uri GoogleMapsWalking(GeoPoint destination) => new($"https://www.google.com/maps/dir/?api=1&destination={Pair(destination)}&travelmode=walking");

	/// <summary>
	/// Builds the e-mail address that asks the City to renew a card.
	/// </summary>
	/// <param name="cardNumber">The card number, or <see langword="null"/> when none was entered.</param>
	/// <returns>The <c>mailto</c> address with the subject filled in.</returns>
	public static Uri Renewal(string? cardNumber)
	{
		return new Uri($"mailto:{RenewalAddress}?subject={Uri.EscapeDataString("Renouvellement carte riverain " + (cardNumber ?? string.Empty))}");
	}

	/// <summary>
	/// Writes a position as <c>latitude,longitude</c> with six decimals, the precision of the first version of the app.
	/// </summary>
	/// <param name="point">The position.</param>
	/// <returns>The pair, for example <c>50.825000,4.360600</c>.</returns>
	public static string Pair(GeoPoint point)
	{
		return string.Create(CultureInfo.InvariantCulture, $"{point.Latitude:F6},{point.Longitude:F6}");
	}
	#endregion
}
