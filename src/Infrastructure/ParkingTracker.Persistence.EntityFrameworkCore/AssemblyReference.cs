using System.Reflection;

namespace ParkingTracker.Persistence.EntityFrameworkCore;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Provides a static reference to the server persistence assembly, used by reflection-based tests.
/// </summary>
public static class AssemblyReference
{
	#region Static Instances
	/// <summary>
	/// The <see cref="System.Reflection.Assembly"/> of the server persistence project.
	/// </summary>
	public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
	#endregion
}
