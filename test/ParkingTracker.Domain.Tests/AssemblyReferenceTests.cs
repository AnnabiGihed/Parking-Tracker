namespace ParkingTracker.Domain.Tests;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="AssemblyReference"/> of the Domain assembly.
/// </summary>
public sealed class AssemblyReferenceTests
{
	#region Assembly Tests
	/// <summary>
	/// Verifies that the reference points at the Domain assembly.
	/// </summary>
	[Fact]
	public void Assembly_ShouldBeTheDomainAssembly()
	{
		var name = AssemblyReference.Assembly.GetName().Name;

		name.Should().Be("ParkingTracker.Domain");
	}
	#endregion
}
