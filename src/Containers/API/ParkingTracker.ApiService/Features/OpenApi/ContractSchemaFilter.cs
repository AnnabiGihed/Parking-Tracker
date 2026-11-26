using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using ParkingTracker.Contracts.Features.Accounts;
using ParkingTracker.Contracts.Features.Profiles;
using ParkingTracker.Contracts.Features.Sessions;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ParkingTracker.ApiService.Features.OpenApi;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Completes the schemas of the contract records in the OpenAPI document (docs-api-contracts R9, R10): every property
///              that is not nullable is required, and each record the API sends or reads has an example. The problem document gets a neutral example; the
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
		[typeof(RefreshSessionRequest)] = new JsonObject { ["refreshToken"] = "mB3xT9vQ1zKd7sWc5nYhLr2PuJe8GfAo4iXqVtZ0kNE" },
		[typeof(SessionResponse)] = new JsonObject
		{
			["accessToken"] = "eyJhbGciOiJFUzI1NiIsImtpZCI6Ij....<API access token>",
			["accessTokenExpiresAt"] = "2026-11-20T10:15:00+00:00",
			["refreshToken"] = "mB3xT9vQ1zKd7sWc5nYhLr2PuJe8GfAo4iXqVtZ0kNE",
		},
		[typeof(MeResponse)] = new JsonObject { ["accountId"] = "3f2c1e5a-7a52-4b8e-9d0e-0a5b1f6d9c11", ["createdOn"] = "2026-11-20", ["lastSeenOn"] = "2026-11-20", ["activeSessionCount"] = 1 },
		[typeof(ProfileRevisionResponse)] = new JsonObject { ["revision"] = 3 },
		[typeof(ProfileDocument)] = new JsonObject
		{
			["schemaVersion"] = 1,
			["home"] = new JsonObject { ["label"] = "Home", ["latitude"] = 50.8467, ["longitude"] = 4.3525, ["addressText"] = "Rue de la Loi 16, 1000 Bruxelles", ["updatedAt"] = "2026-11-20T10:00:00+00:00" },
			["work"] = null,
			["favourites"] = new JsonObject
			{
				["updatedAt"] = "2026-11-20T10:00:00+00:00",
				["places"] = new JsonArray(new JsonObject { ["label"] = "Gym", ["latitude"] = 50.8503, ["longitude"] = 4.3517, ["addressText"] = null }),
			},
			["vehicle"] = new JsonObject
			{
				["make"] = "renault",
				["model"] = "Zoe",
				["year"] = 2021,
				["fuel"] = "Electric",
				["euroNorm"] = "Unknown",
				["connectors"] = new JsonArray("Type2", "Ccs"),
				["batteryKwh"] = 52.0,
				["rangeKm"] = 300,
				["plate"] = null,
				["updatedAt"] = "2026-11-20T10:00:00+00:00",
			},
			["card"] = null,
		},
		[typeof(ProblemDetails)] = new JsonObject { ["type"] = "Error.Code", ["title"] = "Short title of the problem", ["status"] = 400, ["detail"] = "What went wrong, without private data." },
	};
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
	{
		ArgumentNullException.ThrowIfNull(schema);
		ArgumentNullException.ThrowIfNull(context);
		if (schema is not OpenApiSchema concrete)
		{
			return;
		}

		if (Examples.TryGetValue(context.Type, out var example))
		{
			concrete.Example = example.DeepClone();
		}

		if (concrete.Properties is not null && context.Type.Namespace?.StartsWith("ParkingTracker.Contracts", StringComparison.Ordinal) == true)
		{
			concrete.Required ??= new HashSet<string>(StringComparer.Ordinal);
			var nullability = new NullabilityInfoContext();
			foreach (var name in concrete.Properties.Keys.Where(name => !IsNullable(context.Type, name, nullability)))
			{
				_ = concrete.Required.Add(name);
			}
		}
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Tells whether a property of a contract record may be <see langword="null"/>, so it is not listed as required.
	/// </summary>
	/// <param name="type">The contract record.</param>
	/// <param name="jsonName">The name of the property in the document.</param>
	/// <param name="nullability">Reads the nullable annotations.</param>
	/// <returns><see langword="true"/> for a nullable reference or a nullable value type.</returns>
	private static bool IsNullable(Type type, string jsonName, NullabilityInfoContext nullability)
	{
		var property = type.GetProperty(jsonName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
		if (property is null)
		{
			return false;
		}

		return Nullable.GetUnderlyingType(property.PropertyType) is not null
			|| nullability.Create(property).ReadState == NullabilityState.Nullable;
	}
	#endregion
}
