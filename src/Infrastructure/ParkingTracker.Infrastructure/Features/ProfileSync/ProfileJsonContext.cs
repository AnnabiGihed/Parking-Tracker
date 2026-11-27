using System.Text.Json;
using System.Text.Json.Serialization;
using ParkingTracker.Contracts.Features.Profiles;

namespace ParkingTracker.Infrastructure.Features.ProfileSync;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The source-generated JSON of the profile endpoints (ADR-0032): the document the phone reads and writes and the revision
///              the server answers a write with. The web defaults match the API, so no reflection is needed on the phone.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ProfileDocument))]
[JsonSerializable(typeof(ProfileRevisionResponse))]
internal sealed partial class ProfileJsonContext : JsonSerializerContext
{
}
