using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ParkingTracker.Application.Features.ParkingDetection.Commands.HandleParked;
using ParkingTracker.Application.Features.ParkingDetection.Services;
using ParkingTracker.Domain.Features.ParkingDetection.ValueObjects;
using ParkingTracker.Domain.Features.Shared.ValueObjects;
using Pivot.Framework.Application.Abstractions.Messaging;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Tests.Features.ParkingDetection.Services;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="ParkingWatcher"/>: a drive followed by two minutes of standing still sends one command
///              from a scope of its own, the same stop is not sent twice, nothing is sent without a drive, a failed command is
///              reported by the result, and the loop looks by itself and ends when cancelled.
/// </summary>
public sealed class ParkingWatcherTests : IDisposable
{
	#region Static Instances
	/// <summary>
	/// Where the car stops.
	/// </summary>
	private static readonly GeoPoint Stop = new(50.8265, 4.3650);
	#endregion

	#region Fields
	/// <summary>
	/// The mediator substitute that receives the command.
	/// </summary>
	private readonly ISender sender = Substitute.For<ISender>();

	/// <summary>
	/// The fake clock the watcher reads.
	/// </summary>
	private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero));

	/// <summary>
	/// The provider that supplies the scopes, with the substitute registered in it.
	/// </summary>
	private readonly ServiceProvider provider;

	/// <summary>
	/// The watcher under test.
	/// </summary>
	private readonly ParkingWatcher watcher;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ParkingWatcherTests"/> class.
	/// </summary>
	public ParkingWatcherTests()
	{
		sender.Send(Arg.Any<HandleParkedCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
		var services = new ServiceCollection();
		services.AddScoped(_ => sender);
		provider = services.BuildServiceProvider(validateScopes: true);
		watcher = new ParkingWatcher(provider.GetRequiredService<IServiceScopeFactory>(), clock, NullLogger<ParkingWatcher>.Instance);
	}
	#endregion

	#region IDisposable Implementation
	/// <inheritdoc />
	public void Dispose()
	{
		provider.Dispose();
	}
	#endregion

	#region Check Tests
	/// <summary>
	/// Verifies that after a drive and two minutes standing still the stop is sent once, with the position and accuracy of the last fix.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Check_DriveThenTwoMinutesStill_ShouldSendTheCommandOnce()
	{
		watcher.Observe(Fix(speed: 10));
		clock.Advance(TimeSpan.FromMinutes(2));

		var first = await watcher.CheckAsync();
		var second = await watcher.CheckAsync();

		first.Should().BeTrue();
		second.Should().BeFalse();
		await sender.Received(1).Send(new HandleParkedCommand(Stop.Latitude, Stop.Longitude, 6), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that standing still for less than two minutes sends nothing.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Check_StillForOneMinute_ShouldSendNothing()
	{
		watcher.Observe(Fix(speed: 10));
		clock.Advance(TimeSpan.FromMinutes(1));

		var found = await watcher.CheckAsync();

		found.Should().BeFalse();
		await sender.DidNotReceiveWithAnyArgs().Send(default(HandleParkedCommand)!, default);
	}

	/// <summary>
	/// Verifies that a phone that never drove is not parked, however long it stands.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Check_NeverDrove_ShouldSendNothing()
	{
		watcher.Observe(Fix(speed: 0));
		clock.Advance(TimeSpan.FromMinutes(10));

		var found = await watcher.CheckAsync();

		found.Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a command that fails is reported as not handled, and that the stop is not offered again.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Check_CommandFails_ShouldReturnFalseAndNotRepeat()
	{
		sender.Send(Arg.Any<HandleParkedCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Failure(new Error("Test.Down", "down")));
		watcher.Observe(Fix(speed: 10));
		clock.Advance(TimeSpan.FromMinutes(2));

		var first = await watcher.CheckAsync();
		var second = await watcher.CheckAsync();

		first.Should().BeFalse();
		second.Should().BeFalse();
		await sender.Received(1).Send(Arg.Any<HandleParkedCommand>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// Verifies that a reset forgets the drive.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Reset_AfterDrive_ShouldForgetTheDrive()
	{
		watcher.Observe(Fix(speed: 10));
		watcher.Reset();
		clock.Advance(TimeSpan.FromMinutes(2));

		var found = await watcher.CheckAsync();

		found.Should().BeFalse();
	}
	#endregion

	#region Run Tests
	/// <summary>
	/// Verifies that the loop finds the stop by itself when time passes, and ends without an exception when cancelled.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Run_TimePasses_ShouldHandleTheStopAndEndWhenCancelled()
	{
		var handled = new TaskCompletionSource();
		sender.Send(Arg.Any<HandleParkedCommand>(), Arg.Any<CancellationToken>()).Returns(_ =>
		{
			handled.TrySetResult();
			return Task.FromResult(Result.Success());
		});
		using var cancellation = new CancellationTokenSource();
		watcher.Observe(Fix(speed: 10));
		var run = watcher.RunAsync(cancellation.Token);

		clock.Advance(TimeSpan.FromMinutes(2));
		await handled.Task.WaitAsync(TimeSpan.FromSeconds(10));
		await cancellation.CancelAsync();

		await run.WaitAsync(TimeSpan.FromSeconds(10));
		run.IsCompletedSuccessfully.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that an exception of one look is swallowed and the loop goes on with the next look.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Run_LookThrows_ShouldKeepLooking()
	{
		var attempts = 0;
		var recovered = new TaskCompletionSource();
		sender.Send(Arg.Any<HandleParkedCommand>(), Arg.Any<CancellationToken>()).Returns(_ =>
		{
			attempts++;
			recovered.TrySetResult();
			return Task.FromException<Result>(new InvalidOperationException("boom"));
		});
		using var cancellation = new CancellationTokenSource();
		watcher.Observe(Fix(speed: 10));
		var run = watcher.RunAsync(cancellation.Token);

		clock.Advance(TimeSpan.FromMinutes(2));
		await recovered.Task.WaitAsync(TimeSpan.FromSeconds(10));
		await cancellation.CancelAsync();

		await run.WaitAsync(TimeSpan.FromSeconds(10));
		attempts.Should().Be(1);
	}
	#endregion

	#region Private Helpers
	/// <summary>
	/// Builds a fix at the stop, taken now by the fake clock.
	/// </summary>
	/// <param name="speed">The speed of the device in metres per second.</param>
	/// <returns>The fix.</returns>
	private GpsFix Fix(double speed) => new(Stop, 6, speed, clock.GetUtcNow());
	#endregion
}
