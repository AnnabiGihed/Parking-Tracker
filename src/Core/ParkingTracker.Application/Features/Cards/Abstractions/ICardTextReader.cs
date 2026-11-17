using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.Cards.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port that takes or picks a photo of the residents' card and returns the text printed on it (ADR-0003). The host
///              recognises the text on the phone, so the photo never leaves the device and is deleted once it was read. Turning
///              the text into card fields is the job of the Application, not of the port.
/// </summary>
public interface ICardTextReader
{
	#region Methods
	/// <summary>
	/// Lets the user take a photo of the card, or pick one when the camera cannot be used, and reads the text on it.
	/// </summary>
	/// <param name="cancellationToken">Cancels the recognition; it does not close a camera or picker that is already on screen.</param>
	/// <returns>
	/// The recognised text, which is empty when the photo holds no text; or a failure with a <c>CardScan.*</c> code, see
	/// <see cref="CardScanErrors"/>. A user who leaves the camera or the picker without a photo is the failure
	/// <see cref="CardScanErrors.CancelledCode"/>, which is not an error to show.
	/// </returns>
	Task<Result<string>> ReadTextAsync(CancellationToken cancellationToken);
	#endregion
}
