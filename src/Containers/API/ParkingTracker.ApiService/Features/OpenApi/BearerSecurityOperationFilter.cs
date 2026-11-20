using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ParkingTracker.ApiService.Features.OpenApi;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Declares the bearer scheme on every operation that needs a signed-in user (docs-api-contracts R12): all of them, except
///              those marked to allow anonymous access, which matches the fallback policy of the host.
/// </summary>
public sealed class BearerSecurityOperationFilter : IOperationFilter
{
	#region Constants
	/// <summary>
	/// The name of the security scheme in the document.
	/// </summary>
	public const string SchemeName = "Bearer";
	#endregion

	#region Public Methods
	/// <inheritdoc />
	public void Apply(OpenApiOperation operation, OperationFilterContext context)
	{
		ArgumentNullException.ThrowIfNull(operation);
		ArgumentNullException.ThrowIfNull(context);
		var anonymous = context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any();
		if (anonymous)
		{
			return;
		}

		operation.Security ??= [];
		operation.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(SchemeName, context.Document)] = [] });
	}
	#endregion
}
