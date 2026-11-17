namespace ParkingTracker.ViewModels.Features.Shared.Settings.Legacy.LevelDb;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : One write found in a LevelDB file: a key with the sequence number of the write (a higher number is newer), and either
///              a value or the mark that the key was deleted. <see cref="ChromiumLocalStorageParser"/> keeps the newest entry of each key.
/// </summary>
/// <param name="Key">The key as the application wrote it (without the sequence tag LevelDB adds in table files).</param>
/// <param name="Sequence">The sequence number of the write.</param>
/// <param name="IsDeletion"><see langword="true"/> when the write removed the key.</param>
/// <param name="Value">The value; empty for a deletion.</param>
public readonly record struct LevelDbEntry(byte[] Key, long Sequence, bool IsDeletion, byte[] Value);
