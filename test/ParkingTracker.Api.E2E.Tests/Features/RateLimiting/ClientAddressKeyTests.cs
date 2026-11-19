using System.Net;
using ParkingTracker.ApiService.Features.RateLimiting;

namespace ParkingTracker.Api.E2E.Tests.Features.RateLimiting;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests of the rate-limit bucket key of a client address: IPv4 by address, IPv6 by its /64 network.
/// </summary>
public sealed class ClientAddressKeyTests
{
	#region Key Tests
	/// <summary>
	/// Verifies that an unknown address gets the shared "unknown" key.
	/// </summary>
	[Fact]
	public void From_NullAddress_ShouldBeUnknown()
	{
		ClientAddressKey.From(null).Should().Be(ClientAddressKey.Unknown);
	}

	/// <summary>
	/// Verifies that an IPv4 address keys by itself.
	/// </summary>
	[Fact]
	public void From_IPv4Address_ShouldBeTheAddress()
	{
		ClientAddressKey.From(IPAddress.Parse("203.0.113.5")).Should().Be("203.0.113.5");
	}

	/// <summary>
	/// Verifies that an IPv4 address in its IPv6-mapped form gets the same key as the plain IPv4 address.
	/// </summary>
	[Fact]
	public void From_IPv4MappedAddress_ShouldEqualTheIPv4Key()
	{
		var mapped = IPAddress.Parse("203.0.113.5").MapToIPv6();

		ClientAddressKey.From(mapped).Should().Be(ClientAddressKey.From(IPAddress.Parse("203.0.113.5")));
	}

	/// <summary>
	/// Verifies that two IPv6 addresses of one /64 share a key.
	/// </summary>
	[Fact]
	public void From_IPv6AddressesOfOneNetwork_ShouldShareAKey()
	{
		var first = ClientAddressKey.From(IPAddress.Parse("2001:db8:1:2::1"));
		var second = ClientAddressKey.From(IPAddress.Parse("2001:db8:1:2:ffff:ffff:ffff:ffff"));

		first.Should().Be(second);
	}

	/// <summary>
	/// Verifies that IPv6 addresses of different /64 networks get different keys.
	/// </summary>
	[Fact]
	public void From_IPv6AddressesOfDifferentNetworks_ShouldDiffer()
	{
		var first = ClientAddressKey.From(IPAddress.Parse("2001:db8:1:2::1"));
		var second = ClientAddressKey.From(IPAddress.Parse("2001:db8:1:3::1"));

		first.Should().NotBe(second);
	}
	#endregion
}
