using System.Text.Json;
using ParkingTracker.Contracts.Features.Profiles;

namespace ParkingTracker.Backend.Application.Features.Accounts.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Turns the profile document into the UTF-8 JSON that is encrypted and back, with one fixed set of options (camelCase,
///              nulls written, no indentation), so what is stored does not change with the settings of the host.
/// </summary>
public static class ProfileDocumentJson
{
	#region Static Instances
	/// <summary>
	/// The options of the stored document.
	/// </summary>
	private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = false };
	#endregion

	#region Public Methods
	/// <summary>
	/// Writes a document as UTF-8 JSON.
	/// </summary>
	/// <param name="document">The document.</param>
	/// <returns>The bytes of the JSON text.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
	public static byte[] Serialize(ProfileDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);
		return JsonSerializer.SerializeToUtf8Bytes(document, Options);
	}

	/// <summary>
	/// Reads a document from UTF-8 JSON.
	/// </summary>
	/// <param name="utf8Json">The bytes of the JSON text.</param>
	/// <returns>The document, or <see langword="null"/> when the bytes are not a profile document.</returns>
	public static ProfileDocument? TryDeserialize(ReadOnlySpan<byte> utf8Json)
	{
		try
		{
			return JsonSerializer.Deserialize<ProfileDocument>(utf8Json, Options);
		}
		catch (JsonException)
		{
			return null;
		}
	}
	#endregion
}
