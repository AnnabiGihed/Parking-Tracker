using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ParkingTracker.Backend.Application.Features.Accounts.Abstractions;
using ParkingTracker.Backend.Application.Features.Accounts.Commands.CreateSignInChallenge;
using Pivot.Framework.Application.Abstractions.Messaging;

namespace ParkingTracker.Backend.Application.Tests;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="BackendApplicationServiceCollectionExtensions"/>: the mediator reaches the handlers of the
///              assembly, and a missing collection is rejected.
/// </summary>
public sealed class BackendApplicationServiceCollectionExtensionsTests
{
	#region Registration Tests
	/// <summary>
	/// Verifies that a command sent through the registered mediator reaches its handler.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task AddBackendApplication_ShouldRouteACommandToItsHandler()
	{
		var services = new ServiceCollection();
		_ = services.AddSingleton(Substitute.For<ISignInChallengeStore>());
		_ = services.AddSingleton(TimeProvider.System);
		_ = services.AddBackendApplication();
		await using var provider = services.BuildServiceProvider();

		var result = await provider.GetRequiredService<ISender>().Send(new CreateSignInChallengeCommand());

		result.IsSuccess.Should().BeTrue();
		result.Value.Nonce.Should().NotBeNullOrWhiteSpace();
	}

	/// <summary>
	/// Verifies that a missing service collection is rejected.
	/// </summary>
	[Fact]
	public void AddBackendApplication_NullServices_ShouldThrow()
	{
		var act = () => BackendApplicationServiceCollectionExtensions.AddBackendApplication(null!); // null on purpose: proves the guard clause rejects it

		act.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
