namespace ParkingTracker.Backend.Application.Features.Accounts.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port that turns a Google subject into the keyed hash the server keeps instead of the subject (ADR-0015).
/// </summary>
public interface ISubjectHasher
{
	#region Methods
	/// <summary>
	/// Hashes a Google subject with the secret subject hash key.
	/// </summary>
	/// <param name="subject">The Google <c>sub</c> claim.</param>
	/// <returns>HMAC-SHA256 of the subject as 64 lowercase hexadecimal characters.</returns>
	string Hash(string subject);
	#endregion
}
