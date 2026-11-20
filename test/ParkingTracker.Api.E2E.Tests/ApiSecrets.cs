using System.Security.Cryptography;

namespace ParkingTracker.Api.E2E.Tests;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The three secrets the API needs to start outside Development, generated for one test: the current and the previous
///              token signing key as P-256 PEM text and the subject hash key as base64 of 32 bytes. Empty values stand for a
///              missing secret.
/// </summary>
/// <param name="CurrentKeyPem">The current signing key; empty for none.</param>
/// <param name="PreviousKeyPem">The previous signing key; empty for none.</param>
/// <param name="SubjectHashKey">The subject hash key; empty for none.</param>
public sealed record ApiSecrets(string CurrentKeyPem, string PreviousKeyPem, string SubjectHashKey)
{
	#region Static Instances
	/// <summary>
	/// No secret at all, as on a server where the files are missing.
	/// </summary>
	public static readonly ApiSecrets None = new(string.Empty, string.Empty, string.Empty);
	#endregion

	#region Factory Methods
	/// <summary>
	/// Generates a current signing key and a subject hash key, without a previous key.
	/// </summary>
	/// <returns>The secrets.</returns>
	public static ApiSecrets Generate()
	{
		return new ApiSecrets(NewSigningKeyPem(), string.Empty, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
	}

	/// <summary>
	/// Generates a P-256 private key as PEM text.
	/// </summary>
	/// <returns>The PEM text.</returns>
	public static string NewSigningKeyPem()
	{
		using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		return key.ExportPkcs8PrivateKeyPem();
	}
	#endregion
}
