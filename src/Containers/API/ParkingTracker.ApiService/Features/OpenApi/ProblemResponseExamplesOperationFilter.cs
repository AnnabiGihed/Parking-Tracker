using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ParkingTracker.ApiService.Features.OpenApi;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Gives each problem response of an operation the example that belongs to its status (docs-api-contracts R10): the
///              refused sign-in on the 401 of <c>createSession</c>, a plain 401 on the others, the refusal of the limit on 429, the lost save on 409 and
///              the body that is too large on 413.
/// </summary>
public sealed class ProblemResponseExamplesOperationFilter : IOperationFilter
{
	#region Constants
	/// <summary>
	/// The media type of a problem document.
	/// </summary>
	private const string ProblemMediaType = "application/problem+json";

	/// <summary>
	/// The operation whose 401 is the refused sign-in.
	/// </summary>
	private const string SignInOperationId = "createSession";

	/// <summary>
	/// The operation whose 401 is the refused refresh.
	/// </summary>
	private const string RefreshOperationId = "refreshSession";
	#endregion

	#region Static Instances
	/// <summary>
	/// The example of the problem of a status.
	/// </summary>
	private static readonly Dictionary<string, JsonObject> ByStatus = new(StringComparer.Ordinal)
	{
		["400"] = new JsonObject { ["type"] = "https://tools.ietf.org/html/rfc9110#section-15.5.1", ["title"] = "One or more validation errors occurred.", ["status"] = 400, ["errors"] = new JsonObject { ["IdToken"] = new JsonArray("The IdToken field is required.") } },
		["401"] = new JsonObject { ["title"] = "Unauthorized", ["status"] = 401 },
		["409"] = new JsonObject { ["type"] = "Persistence.Concurrency", ["title"] = "Conflict", ["status"] = 409, ["detail"] = "The data changed while it was being saved." },
		["413"] = new JsonObject { ["title"] = "Request body too large", ["status"] = 413 },
		["429"] = new JsonObject { ["title"] = "Too many requests", ["status"] = 429 },
	};

	/// <summary>
	/// The example of the refused sign-in.
	/// </summary>
	private static readonly JsonObject SignInRefused = new() { ["type"] = "Session.SignIn.Failed", ["title"] = "Unauthorized", ["status"] = 401, ["detail"] = "The sign-in could not be completed." };

	/// <summary>
	/// The example of the refused refresh.
	/// </summary>
	private static readonly JsonObject RefreshRefused = new() { ["type"] = "Session.Refresh.Failed", ["title"] = "Unauthorized", ["status"] = 401, ["detail"] = "The session could not be refreshed." };
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public void Apply(OpenApiOperation operation, OperationFilterContext context)
	{
		ArgumentNullException.ThrowIfNull(operation);
		ArgumentNullException.ThrowIfNull(context);
		if (operation.Responses is null)
		{
			return;
		}

		foreach (var (status, response) in operation.Responses)
		{
			if (response is OpenApiResponse concrete
				&& concrete.Content is not null
				&& concrete.Content.TryGetValue(ProblemMediaType, out var media)
				&& ExampleOf(status, operation.OperationId) is { } example)
			{
				media.Example = example.DeepClone();
			}
		}
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Picks the example of a problem response.
	/// </summary>
	/// <param name="status">The status code of the response.</param>
	/// <param name="operationId">The operation the response belongs to.</param>
	/// <returns>The example, or <see langword="null"/> when the status has none.</returns>
	private static JsonObject? ExampleOf(string status, string? operationId)
	{
		if (string.Equals(status, "401", StringComparison.Ordinal) && string.Equals(operationId, SignInOperationId, StringComparison.Ordinal))
		{
			return SignInRefused;
		}

		if (string.Equals(status, "401", StringComparison.Ordinal) && string.Equals(operationId, RefreshOperationId, StringComparison.Ordinal))
		{
			return RefreshRefused;
		}

		return ByStatus.GetValueOrDefault(status);
	}
	#endregion
}
