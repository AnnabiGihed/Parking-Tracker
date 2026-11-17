using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParkingTracker.Application.Features.SpotDecisions.Abstractions;

namespace ParkingTracker.Infrastructure.Tests;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="InfrastructureServiceInstaller"/> and <see cref="AssemblyReference"/>:
///              the installer completes against an empty assembly and rejects missing arguments.
/// </summary>
public sealed class InfrastructureServiceInstallerTests
{
	#region Test Infrastructure
	/// <summary>
	/// Creates a configuration without any setting.
	/// </summary>
	/// <returns>Empty configuration.</returns>
	private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();
	#endregion

	#region Install Tests
	/// <summary>
	/// Verifies that installing into a service collection succeeds and the provider builds.
	/// </summary>
	[Fact]
	public void Install_ShouldBuildAValidProvider()
	{
		var services = new ServiceCollection();

		new InfrastructureServiceInstaller().Install(services, EmptyConfiguration());

		var act = () => services.BuildServiceProvider(validateScopes: true).Dispose();
		act.Should().NotThrow();
	}

	/// <summary>
	/// Verifies that the reference data port resolves to one shared instance that loads.
	/// </summary>
	[Fact]
	public void Install_ShouldRegisterTheReferenceDataAsASingleton()
	{
		var services = new ServiceCollection();
		new InfrastructureServiceInstaller().Install(services, EmptyConfiguration());
		using var provider = services.BuildServiceProvider(validateScopes: true);

		var first = provider.GetRequiredService<IReferenceData>();

		first.Should().BeSameAs(provider.GetRequiredService<IReferenceData>());
		first.GetReferenceData().IsSuccess.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a missing configuration is rejected.
	/// </summary>
	[Fact]
	public void Install_NullConfiguration_ShouldThrowArgumentNullException()
	{
		var act = () => new InfrastructureServiceInstaller().Install(new ServiceCollection(), null!); // null on purpose: proves the guard clause rejects it

		act.Should().Throw<ArgumentNullException>();
	}
	#endregion

	#region Assembly Tests
	/// <summary>
	/// Verifies that the reference points at the Infrastructure assembly.
	/// </summary>
	[Fact]
	public void Assembly_ShouldBeTheInfrastructureAssembly()
	{
		AssemblyReference.Assembly.GetName().Name.Should().Be("ParkingTracker.Infrastructure");
	}
	#endregion
}
