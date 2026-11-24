using System.Text.Json;
using System.Text.Json.Serialization;
using ParkingTracker.Contracts.Features.Accounts;
using ParkingTracker.Contracts.Features.Sessions;

namespace ParkingTracker.Infrastructure.Features.Accounts;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The generated JSON metadata of the transport records of the account API, so that reading and writing them needs no
///              reflection and survives trimming. Names are camel case, as the API writes them.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CreateSessionRequest))]
[JsonSerializable(typeof(RefreshSessionRequest))]
[JsonSerializable(typeof(SessionResponse))]
[JsonSerializable(typeof(SignInChallengeResponse))]
[JsonSerializable(typeof(MeResponse))]
internal sealed partial class AccountJsonContext : JsonSerializerContext
{
}
