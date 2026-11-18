namespace ParkingTracker.Persistence.Device.Features.Shared.Database.Schema;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : One numbered step of the schema of the device database, written as explicit SQL (ADR-0026). A released step is
///              never edited: a change of the schema is a new step with the next number.
/// </summary>
internal interface IDeviceSchemaStep
{
	#region Properties
	/// <summary>
	/// Gets the number of the step; steps are applied in ascending order and the numbers start at 1.
	/// </summary>
	int Version { get; }

	/// <summary>
	/// Gets the SQL statements of the step, run in this order inside one transaction.
	/// </summary>
	IReadOnlyList<string> Statements { get; }
	#endregion
}
