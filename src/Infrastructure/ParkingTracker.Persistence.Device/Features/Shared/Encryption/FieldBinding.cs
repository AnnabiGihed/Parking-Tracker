namespace ParkingTracker.Persistence.Device.Features.Shared.Encryption;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Where an encrypted value lives: its table, its column and the identifier of its row. The cipher authenticates these
///              together with the value (as associated data of AES-GCM), so that a ciphertext copied into another column or another
///              row does not decrypt there.
/// </summary>
/// <param name="Table">The name of the table.</param>
/// <param name="Column">The name of the column.</param>
/// <param name="RowId">The identifier of the row, the identifier of its aggregate or place.</param>
public readonly record struct FieldBinding(string Table, string Column, string RowId);
