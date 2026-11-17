namespace ParkingTracker.Application.Features.Shared.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port that opens an address outside the app, in the application the phone chooses for it: Waze, Google Maps, a
///              payment app in the browser, or the e-mail application. The screens never open links themselves.
/// </summary>
public interface ILinkLauncher
{
	#region Methods
	/// <summary>
	/// Opens an address in the application the device associates with it.
	/// </summary>
	/// <param name="uri">The address to open, for example an <c>https</c> or <c>mailto</c> address.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns><see langword="true"/> when an application accepted the address; <see langword="false"/> when none could open it.</returns>
	Task<bool> OpenAsync(Uri uri, CancellationToken cancellationToken = default);
	#endregion
}
