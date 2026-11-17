namespace ParkingTracker.Infrastructure.Features.Shared.Legacy.LevelDb;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A file of a LevelDB directory held in memory (the write-ahead log <c>*.log</c> or a table <c>*.ldb</c> / <c>*.sst</c>), so
///              that the parser runs on bytes and tests need no files. The whole directory of the old web view's local storage is a few
///              kilobytes.
/// </summary>
/// <param name="Name">The file name, which tells the kind by its extension.</param>
/// <param name="Content">The bytes of the file.</param>
public sealed record LevelDbFile(string Name, byte[] Content);
