namespace ParkingTracker.Infrastructure.Features.Shared.Http;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : How long a live source may take before the check gives up (no retry follows): 12 seconds for a request and
///              30 seconds for the download of a document, the limits of the first version of the app. Registered as a value so
///              that tests can shorten them.
/// </summary>
/// <param name="Request">The limit of a request for data or for the headers of a document.</param>
/// <param name="Download">The limit of the download of a document.</param>
internal sealed record SourceTimeouts(TimeSpan Request, TimeSpan Download)
{
	#region Static Instances
	/// <summary>
	/// The limits of the first version of the app.
	/// </summary>
	internal static readonly SourceTimeouts Default = new(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(30));
	#endregion
}
