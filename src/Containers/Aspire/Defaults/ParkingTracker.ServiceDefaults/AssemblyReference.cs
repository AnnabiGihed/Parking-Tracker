using System.Reflection;

namespace ParkingTracker.ServiceDefaults;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Provides a static reference to the ServiceDefaults assembly, used by reflection-based tests.
/// </summary>
public static class AssemblyReference
{
	#region Static Instances
	/// <summary>
	/// The <see cref="System.Reflection.Assembly"/> of the ServiceDefaults project.
	/// </summary>
	public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
	#endregion
}
