using System.Net;
using System.Net.Sockets;

namespace ParkingTracker.ApiService.Features.RateLimiting;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Turns a client address into the key of its rate-limit bucket: an IPv4 address by itself, an IPv6 address by its /64
///              network, because one subscriber holds a whole /64 and could otherwise change address at will.
/// </summary>
public static class ClientAddressKey
{
	#region Constants
	/// <summary>
	/// The key of a request without a known address.
	/// </summary>
	public const string Unknown = "unknown";

	/// <summary>
	/// The number of leading bytes of an IPv6 address that form the /64 network.
	/// </summary>
	private const int Ipv6NetworkBytes = 8;
	#endregion

	#region Public Methods
	/// <summary>
	/// Builds the bucket key of an address.
	/// </summary>
	/// <param name="address">The client address, or <see langword="null"/> when unknown.</param>
	/// <returns>The address text for IPv4, the /64 network text for IPv6, or <see cref="Unknown"/>.</returns>
	public static string From(IPAddress? address)
	{
		if (address is null)
		{
			return Unknown;
		}

		if (address.IsIPv4MappedToIPv6)
		{
			address = address.MapToIPv4();
		}

		if (address.AddressFamily != AddressFamily.InterNetworkV6)
		{
			return address.ToString();
		}

		var bytes = address.GetAddressBytes();
		Array.Clear(bytes, Ipv6NetworkBytes, bytes.Length - Ipv6NetworkBytes);
		return $"{new IPAddress(bytes)}/64";
	}
	#endregion
}
