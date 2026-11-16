using ParkingTracker.Domain.ReferenceData;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.ReferenceData;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port that gives the Application layer the reference data (municipalities, sectors, tolerance
///              agreements) shipped with the app.
/// </summary>
public interface IReferenceData
{
	#region Methods
	/// <summary>
	/// Returns the reference data, loaded once and cached.
	/// </summary>
	/// <returns>The data set, or a failure with a <c>ReferenceData.*</c> code when a resource is missing or invalid.</returns>
	Result<ReferenceDataSet> GetReferenceData();
	#endregion
}
