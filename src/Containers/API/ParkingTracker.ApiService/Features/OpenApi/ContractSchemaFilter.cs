using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using ParkingTracker.Contracts.Features.Accounts;
using ParkingTracker.Contracts.Features.Sessions;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ParkingTracker.ApiService.Features.OpenApi;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Completes the schemas of the contract records in the OpenAPI document (docs-api-contracts R9, R10): every property
///              that is not nullable is required, and each record has an example. The problem document gets a neutral example; the
///              examples of its answers (the refused sign-in, the limit) are set per response by
///              <see cref="ProblemResponseExamplesOperationFilter"/>, so one does not show under the other.
/// </summary>
public sealed class ContractSchemaFilter : ISchemaFilter
{
	#region Static Instances
	/// <summary>
	/// The example of each type, by type.
	/// </summary>
	private static readonly Dictionary<Type, JsonObject> Examples = new()
	{
		[typeof(SignInChallengeResponse)] = new JsonObject { ["nonce"] = "q7Zx0mR2oVwq3k8c4yT1bN5hJd9sLpEa6uFgXzCvWiM", ["expiresAt"] = "2026-11-20T10:05:00+00:00" },
		[typeof(CreateSessionRequest)] = new JsonObject { ["idToken"] = "eyJhbGciOiJSUzI1NiIsImtpZCI6Ij....<Google ID token>" },
		[typeof(SessionResponse)] = new JsonObject
		{
			["accessToken"] = "eyJhbGciOiJFUzI1NiIsImtpZCI6Ij....<API access token>",
			["accessTokenExpiresAt"] = "2026-11-20T10:15:00+00:00",
			["refreshToken"] = "mB3xT9vQ1zKd7sWc5nYhLr2PuJe8GfAo4iXqVtZ0kNE",
		},
		[typeof(MeResponse)] = new JsonObject { ["accountId"] = "3f2c1e5a-7a52-4b8e-9d0e-0a5b1f6d9c11", ["createdOn"] = "2026-11-20", ["lastSeenOn"] = "2026-11-20", ["activeSessionCount"] = 1 },
		[typeof(ProblemDetails)] = new JsonObject { ["type"] = "Error.Code", ["title"] = "Short title of the problem", ["status"] = 400, ["detail"] = "What went wrong, without private data." },
	};
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
	{
		ArgumentNullException.ThrowIfNull(schema);
		ArgumentNullException.ThrowIfNull(context);
		if (schema is not OpenApiSchema concrete || !Examples.TryGetValue(context.Type, out var example))
		{
			return;
		}

		concrete.Example = example.DeepClone();
		if (concrete.Properties is not null && context.Type.Namespace?.StartsWith("ParkingTracker.Contracts", StringComparison.Ordinal) == true)
		{
			concrete.Required ??= new HashSet<string>(StringComparer.Ordinal);
			foreach (var name in concrete.Properties.Keys)
			{
				_ = concrete.Required.Add(name);
			}
		}
	}
	#endregion
}
