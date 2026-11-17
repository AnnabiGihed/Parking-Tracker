using ParkingTracker.Domain.Features.PaidHours.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.SpotDecisions.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the Brussels Mobility parking rules API: paid days and hours, prices, maximum stay and fine per zone code.
/// </summary>
public interface IParkingRulesClient
{
	#region Methods
	/// <summary>
	/// Gets the rules in force at a moment, one per zone code (the one with the latest start when several are in force).
	/// </summary>
	/// <param name="code">The zone code to ask for, or <see langword="null"/> for the rules of every zone.</param>
	/// <param name="now">The moment; rules that are disabled or not yet active at it are left out.</param>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The rules by zone code, or a failure with a <c>ParkingRules.*</c> code and a message such as <c>parking rules HTTP 500</c>.</returns>
	Task<Result<IReadOnlyDictionary<string, ParkingRule>>> GetRulesAsync(string? code, DateTimeOffset now, CancellationToken cancellationToken);
	#endregion
}
