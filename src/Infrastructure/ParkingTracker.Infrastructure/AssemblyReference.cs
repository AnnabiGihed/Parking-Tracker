using System.Reflection;

namespace ParkingTracker.Infrastructure;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Provides a static reference to the Infrastructure assembly, used for convention-based service
///              scanning and by reflection-based tests.
/// </summary>
public static class AssemblyReference
{
	#region Static Instances
	/// <summary>
	/// The <see cref="System.Reflection.Assembly"/> of the Infrastructure project.
	/// </summary>
	public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
	#endregion
}
