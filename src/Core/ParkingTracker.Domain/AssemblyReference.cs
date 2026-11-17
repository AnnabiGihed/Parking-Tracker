using System.Reflection;

namespace ParkingTracker.Domain;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Provides a static reference to the Domain assembly for reflection-based scenarios such as
///              architecture tests and handler scanning.
/// </summary>
public static class AssemblyReference
{
	#region Static Instances
	/// <summary>
	/// The <see cref="System.Reflection.Assembly"/> of the Domain project.
	/// </summary>
	public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
	#endregion
}
