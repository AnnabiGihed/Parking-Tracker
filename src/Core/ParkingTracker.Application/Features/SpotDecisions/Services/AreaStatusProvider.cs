using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Domain.Features.PaidHours.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.SpotDecisions.Services;
using ParkingTracker.Domain.Features.SpotDecisions.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.SpotDecisions.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Fetches surfaces, parking rules, sector areas and house numbers for an area, in that order, and lets
///              <see cref="AreaDecider"/> evaluate the surfaces.
/// </summary>
public sealed class AreaStatusProvider : IAreaStatusProvider
{
	#region Fields
	/// <summary>
	/// Port to the parking.brussels WFS service: zones, reserved spaces and mailles.
	/// </summary>
	private readonly IParkingBrusselsClient parkingBrussels;

	/// <summary>
	/// Port to the parking rules service.
	/// </summary>
	private readonly IParkingRulesClient rules;

	/// <summary>
	/// Port to the address points service.
	/// </summary>
	private readonly IAddressPointsClient addresses;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="AreaStatusProvider"/> class.
	/// </summary>
	/// <param name="parkingBrussels">The parking.brussels port.</param>
	/// <param name="rules">The parking rules port.</param>
	/// <param name="addresses">The address points port.</param>
	public AreaStatusProvider(IParkingBrusselsClient parkingBrussels, IParkingRulesClient rules, IAddressPointsClient addresses)
	{
		this.parkingBrussels = parkingBrussels;
		this.rules = rules;
		this.addresses = addresses;
	}
	#endregion

	#region IAreaStatusProvider Implementation
	/// <inheritdoc />
	/// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
	public async Task<Result<IReadOnlyList<SurfaceStatus>>> LoadAsync(DecisionContext context, GeoBox box, DateTimeOffset now, AddressFetching addressFetching, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		var padded = box.Pad();
		var zones = await parkingBrussels.GetZonesAsync(padded, cancellationToken).ConfigureAwait(false);
		if (zones.IsFailure)
		{
			return Result.Failure<IReadOnlyList<SurfaceStatus>>(zones.Error);
		}

		var allRules = await rules.GetRulesAsync(null, now, cancellationToken).ConfigureAwait(false);
		IReadOnlyList<SectorMaille> mailles = [];
		if (AreaDecider.NeedsMailles(context))
		{
			var fetched = await parkingBrussels.GetMaillesAsync(padded, cancellationToken).ConfigureAwait(false);
			if (fetched.IsFailure)
			{
				return Result.Failure<IReadOnlyList<SurfaceStatus>>(fetched.Error);
			}

			mailles = fetched.Value;
		}

		IReadOnlyList<AddressPoint> points = [];
		if (addressFetching == AddressFetching.Fetch && AreaDecider.NeedsAddresses(context))
		{
			var fetched = await addresses.GetAddressPointsAsync(box, cancellationToken).ConfigureAwait(false);
			if (fetched.IsFailure)
			{
				return Result.Failure<IReadOnlyList<SurfaceStatus>>(fetched.Error);
			}

			points = fetched.Value;
		}

		IReadOnlyDictionary<string, ParkingRule>? ruleMap = allRules.IsSuccess ? allRules.Value : null;
		return Result.Success(AreaDecider.Evaluate(context, box, zones.Value, ruleMap, mailles, points));
	}
	#endregion
}
