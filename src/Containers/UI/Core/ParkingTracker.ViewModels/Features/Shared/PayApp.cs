namespace ParkingTracker.ViewModels.Features.Shared;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A parking payment application the user can open from a paid spot. Payment happens in the operator's own app.
/// </summary>
/// <param name="Name">The name on the button.</param>
/// <param name="Address">The address of the operator, opened through the link launcher.</param>
public sealed record PayApp(string Name, Uri Address);
