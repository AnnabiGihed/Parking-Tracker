using System.Reflection;

namespace ParkingTracker.Application;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Provides a static reference to the Application assembly, used to register the mediator
///              handlers it contains and by reflection-based tests.
/// </summary>
public static class AssemblyReference
{
	#region Static Instances
	/// <summary>
	/// The <see cref="System.Reflection.Assembly"/> of the Application project.
	/// </summary>
	public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
	#endregion
}
