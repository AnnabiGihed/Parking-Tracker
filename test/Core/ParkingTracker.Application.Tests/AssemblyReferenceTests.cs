namespace ParkingTracker.Application.Tests;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="AssemblyReference"/> of the Application assembly.
/// </summary>
public sealed class AssemblyReferenceTests
{
	#region Assembly Tests
	/// <summary>
	/// Verifies that the reference points at the Application assembly.
	/// </summary>
	[Fact]
	public void Assembly_ShouldBeTheApplicationAssembly()
	{
		var name = AssemblyReference.Assembly.GetName().Name;

		name.Should().Be("ParkingTracker.Application");
	}
	#endregion
}
