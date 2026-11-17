using NSubstitute;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;
using ParkingTracker.Application.Features.SpotDecisions.Services;
using ParkingTracker.Domain.Features.PaidHours.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using ParkingTracker.Domain.Features.Zones.ValueObjects;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Tests.Features.SpotDecisions.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The source ports of an area as substitutes that answer with empty data, so that a test changes only the answer it
///              is about, plus the provider built on them.
/// </summary>
internal sealed class SourceSubstitutes
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="SourceSubstitutes"/> class with every source answering successfully and empty.
	/// </summary>
	public SourceSubstitutes()
	{
		ParkingBrussels.GetZonesAsync(Arg.Any<GeoBox>(), Arg.Any<CancellationToken>()).Returns(Result.Success<IReadOnlyList<ZoneSurface>>([]));
		ParkingBrussels.GetMaillesAsync(Arg.Any<GeoBox>(), Arg.Any<CancellationToken>()).Returns(Result.Success<IReadOnlyList<SectorMaille>>([]));
		Rules.GetRulesAsync(Arg.Any<string?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
			.Returns(Result.Success<IReadOnlyDictionary<string, ParkingRule>>(new Dictionary<string, ParkingRule>(StringComparer.Ordinal)));
		Addresses.GetAddressPointsAsync(Arg.Any<GeoBox>(), Arg.Any<CancellationToken>()).Returns(Result.Success<IReadOnlyList<AddressPoint>>([]));
	}
	#endregion

	#region Properties
	/// <summary>
	/// Gets the parking.brussels port.
	/// </summary>
	public IParkingBrusselsClient ParkingBrussels { get; } = Substitute.For<IParkingBrusselsClient>();

	/// <summary>
	/// Gets the parking rules port.
	/// </summary>
	public IParkingRulesClient Rules { get; } = Substitute.For<IParkingRulesClient>();

	/// <summary>
	/// Gets the address points port.
	/// </summary>
	public IAddressPointsClient Addresses { get; } = Substitute.For<IAddressPointsClient>();

	/// <summary>
	/// Gets a provider that reads the substitutes.
	/// </summary>
	public AreaStatusProvider Provider => new(ParkingBrussels, Rules, Addresses);
	#endregion
}
