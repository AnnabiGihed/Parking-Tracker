using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Domain.Features.PaidHours.ValueObjects;
using ParkingTracker.Infrastructure.Features.Shared.Http;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Infrastructure.Features.PaidHours;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Reads the Brussels Mobility parking rules API over HTTP: one attempt, a limit of time, no cache.
/// </summary>
internal sealed class ParkingRulesClient : IParkingRulesClient
{
	#region Fields
	/// <summary>
	/// HTTP client the source requests go through.
	/// </summary>
	private readonly HttpClient client;

	/// <summary>
	/// Time limits applied to each request.
	/// </summary>
	private readonly SourceTimeouts timeouts;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ParkingRulesClient"/> class.
	/// </summary>
	/// <param name="client">The HTTP client of the service.</param>
	/// <param name="timeouts">The limits of time.</param>
	public ParkingRulesClient(HttpClient client, SourceTimeouts timeouts)
	{
		this.client = client;
		this.timeouts = timeouts;
	}
	#endregion

	#region IParkingRulesClient Implementation
	/// <inheritdoc />
	public Task<Result<IReadOnlyDictionary<string, ParkingRule>>> GetRulesAsync(string? code, DateTimeOffset now, CancellationToken cancellationToken)
	{
		return SourceHttp.GetAsync(client, new SourceCall(SourceKind.ParkingRules, ParkingRuleParser.What, timeouts.Request), SourceRequests.Rules(code), json => ParkingRuleParser.Parse(json, now), cancellationToken);
	}
	#endregion
}
