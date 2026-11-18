namespace ParkingTracker.Persistence.Device.Features.Shared.Encryption;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A 256-bit key of the field cipher with the identifier that is written in front of every value it encrypts, so that
///              a later release can add a second key and still read the old values.
/// </summary>
/// <param name="Id">The identifier of the key: short, without a dot.</param>
/// <param name="Material">The 32 bytes of the key.</param>
public sealed record DeviceKey(string Id, ReadOnlyMemory<byte> Material);
