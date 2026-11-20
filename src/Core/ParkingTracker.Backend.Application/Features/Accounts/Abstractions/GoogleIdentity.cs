namespace ParkingTracker.Backend.Application.Features.Accounts.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : What the API keeps of a validated Google ID token: the subject that identifies the user at Google and the nonce
///              the token is bound to. Neither is ever logged or stored as is.
/// </summary>
/// <param name="Subject">The Google <c>sub</c> claim.</param>
/// <param name="Nonce">The <c>nonce</c> claim.</param>
public sealed record GoogleIdentity(string Subject, string Nonce);
