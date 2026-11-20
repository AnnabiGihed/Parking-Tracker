using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Pivot.Framework.Application.Abstractions;
using Pivot.Framework.Authentication.Models;

namespace ParkingTracker.ApiService.Features.Authentication;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Pivot current-user contracts implemented from the claims the API issues itself (ADR-0014): the user is the account
///              identifier in the <c>sub</c> claim, and nothing else is known of the person. Audit stamps therefore carry the account
///              identifier and never an e-mail address. Outside an authenticated request the actor is <see cref="SystemActor"/>.
/// </summary>
public sealed class AccountCurrentUser : ICurrentUser, ICurrentUserProvider
{
	#region Constants
	/// <summary>
	/// The actor that audit stamps carry when no user is signed in.
	/// </summary>
	public const string SystemActor = "System";
	#endregion

	#region Fields
	/// <summary>
	/// Gives the request being served.
	/// </summary>
	private readonly IHttpContextAccessor accessor;

	/// <summary>
	/// The roles of the user, read once per request because the user does not change within it.
	/// </summary>
	private IReadOnlyList<string>? roles;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="AccountCurrentUser"/> class.
	/// </summary>
	/// <param name="accessor">Gives the request being served.</param>
	public AccountCurrentUser(IHttpContextAccessor accessor)
	{
		this.accessor = accessor;
	}
	#endregion

	#region Properties
	/// <inheritdoc />
	public Guid? UserId => Guid.TryParse(Subject, out var id) && id != Guid.Empty ? id : null;

	/// <inheritdoc />
	public string? Email => null;

	/// <inheritdoc />
	public string? Username => Subject;

	/// <inheritdoc />
	public string? DisplayName => null;

	/// <inheritdoc />
	public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

	/// <inheritdoc />
	public ClaimsPrincipal? Principal => accessor.HttpContext?.User;

	/// <inheritdoc />
	public IReadOnlyList<string> Roles => roles ??= Principal?.FindAll("role").Select(claim => claim.Value).ToList() ?? [];

	/// <summary>
	/// Gets the <c>sub</c> claim of the signed-in user, or <see langword="null"/> when nobody is signed in.
	/// </summary>
	private string? Subject => IsAuthenticated ? Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value : null;
	#endregion

	#region ICurrentUser Implementation
	/// <inheritdoc />
	public bool IsInRole(string role)
	{
		return Roles.Contains(role, StringComparer.Ordinal);
	}
	#endregion

	#region ICurrentUserProvider Implementation
	/// <inheritdoc />
	public string GetCurrentUser()
	{
		return Subject ?? SystemActor;
	}
	#endregion
}
