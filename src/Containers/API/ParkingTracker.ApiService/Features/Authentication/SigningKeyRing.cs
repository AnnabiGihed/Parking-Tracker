using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace ParkingTracker.ApiService.Features.Authentication;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The ES256 keys of the access tokens, ready to use: the current key that signs, and the current and previous keys
///              that validate. The key id of each is the base64url SHA-256 JWK thumbprint of its public key, so it follows the key
///              and needs no list. Without a current key (Development only) it makes one for the life of the process.
/// </summary>
public sealed class SigningKeyRing : IDisposable
{
	#region Constants
	/// <summary>
	/// The object identifier of the curve P-256.
	/// </summary>
	private const string P256Oid = "1.2.840.10045.3.1.7";
	#endregion

	#region Fields
	/// <summary>
	/// The key objects the ring owns and disposes.
	/// </summary>
	private readonly List<ECDsa> owned = [];
	#endregion

	#region Properties
	/// <summary>
	/// Gets the key that signs new tokens.
	/// </summary>
	public ECDsaSecurityKey Current { get; }

	/// <summary>
	/// Gets the keys that validate tokens: the current key first, then the previous one when there is one.
	/// </summary>
	public IReadOnlyList<SecurityKey> ValidationKeys { get; }
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SigningKeyRing"/> class.
	/// </summary>
	/// <param name="options">The signing key settings; they are expected to be valid (see <see cref="TokenOptionsValidator"/>).</param>
	/// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">A key is not a P-256 private key in PEM text.</exception>
	public SigningKeyRing(SigningKeyOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		var validation = new List<SecurityKey>();
		Current = Add(string.IsNullOrWhiteSpace(options.Current) ? ECDsa.Create(ECCurve.NamedCurves.nistP256) : Read(options.Current));
		validation.Add(Current);
		if (!string.IsNullOrWhiteSpace(options.Previous))
		{
			validation.Add(Add(Read(options.Previous)));
		}

		ValidationKeys = validation;
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Checks that a text is a P-256 private key in PEM, without keeping it.
	/// </summary>
	/// <param name="pem">The PEM text.</param>
	/// <returns><see langword="null"/> when the key is usable; otherwise what is wrong, never the key itself.</returns>
	public static string? Check(string pem)
	{
		try
		{
			using var key = Read(pem);
			return null;
		}
		catch (InvalidOperationException problem)
		{
			return problem.Message;
		}
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		foreach (var key in owned)
		{
			key.Dispose();
		}
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Reads a P-256 private key from PEM text.
	/// </summary>
	/// <param name="pem">The PEM text.</param>
	/// <returns>The key.</returns>
	/// <exception cref="InvalidOperationException">The text is not a P-256 private key in PEM.</exception>
	private static ECDsa Read(string pem)
	{
		var key = ECDsa.Create();
		try
		{
			key.ImportFromPem(pem);
			var curve = key.ExportParameters(false).Curve;
			if (!string.Equals(curve.Oid.Value, P256Oid, StringComparison.Ordinal))
			{
				throw new InvalidOperationException("The key is not on the curve P-256.");
			}

			_ = key.ExportParameters(true);
			return key;
		}
		catch (Exception problem) when (problem is CryptographicException or ArgumentException)
		{
			key.Dispose();
			throw new InvalidOperationException("The text is not a P-256 private key in PEM.", problem);
		}
		catch (InvalidOperationException)
		{
			key.Dispose();
			throw;
		}
	}

	/// <summary>
	/// Wraps a key as a security key with its thumbprint as key id, and keeps it to dispose.
	/// </summary>
	/// <param name="key">The key.</param>
	/// <returns>The security key.</returns>
	private ECDsaSecurityKey Add(ECDsa key)
	{
		owned.Add(key);
		var securityKey = new ECDsaSecurityKey(key);
		var thumbprint = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(securityKey).ComputeJwkThumbprint();
		securityKey.KeyId = Base64UrlEncoder.Encode(thumbprint);
		return securityKey;
	}
	#endregion
}
