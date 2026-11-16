using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pivot.Framework.Application.Abstractions.Messaging;
using Pivot.Framework.Application.Abstractions.Messaging.Commands;
using Pivot.Framework.Application.Behaviors;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Tests;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="ApplicationServiceInstaller"/> and <see cref="AssemblyReference"/>:
///              the installer registers the Pivot mediator and rejects missing arguments.
/// </summary>
public sealed class ApplicationServiceInstallerTests
{
	#region Test Infrastructure
	private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();

	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : A test-only command used to exercise the validation pipeline.
	/// </summary>
	/// <param name="Name">The name that the sample validator requires.</param>
	private sealed record SampleCommand(string Name) : ICommand;

	/// <summary>
	/// Author      : Gihed Annabi
	/// Date        : 11-2026
	/// Purpose     : A test-only validator for <see cref="SampleCommand"/> that requires a name.
	/// </summary>
	private sealed class SampleCommandValidator : AbstractValidator<SampleCommand>
	{
		#region Constructors
		/// <summary>
		/// Initializes a new instance of the <see cref="SampleCommandValidator"/> class.
		/// </summary>
		public SampleCommandValidator() => RuleFor(command => command.Name).NotEmpty();
		#endregion
	}
	#endregion

	#region Install Tests
	/// <summary>
	/// Verifies that the installer makes the mediator and its sender resolvable.
	/// </summary>
	[Fact]
	public void Install_ShouldRegisterMediatorAndSender()
	{
		var services = new ServiceCollection();

		new ApplicationServiceInstaller().Install(services, EmptyConfiguration());

		using var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		scope.ServiceProvider.GetService<ISender>().Should().NotBeNull();
		scope.ServiceProvider.GetService<IMediator>().Should().NotBeNull();
	}

	/// <summary>
	/// Verifies that installing without convention-based registration still registers the mediator.
	/// </summary>
	[Fact]
	public void Install_WithoutConventions_ShouldStillRegisterMediator()
	{
		var services = new ServiceCollection();

		new ApplicationServiceInstaller().Install(services, EmptyConfiguration(), includeConventionBasedRegistration: false);

		services.Should().Contain(descriptor => descriptor.ServiceType == typeof(ISender));
	}

	/// <summary>
	/// Verifies that the validation pipeline behavior is registered as an open generic pipeline behavior.
	/// </summary>
	[Fact]
	public void Install_ShouldRegisterValidationPipelineBehavior()
	{
		var services = new ServiceCollection();

		new ApplicationServiceInstaller().Install(services, EmptyConfiguration());

		services.Should().Contain(descriptor =>
			descriptor.ServiceType == typeof(IPipelineBehavior<,>)
			&& descriptor.ImplementationType == typeof(ValidationPipelineBehavior<,>));
	}

	/// <summary>
	/// Verifies that the installed validation behavior resolves the registered validators and rejects an invalid request.
	/// </summary>
	/// <returns>A task that completes when the assertion has run.</returns>
	[Fact]
	public async Task Install_ValidatorRegistered_ShouldLetTheValidationBehaviorRejectAnInvalidRequest()
	{
		var services = new ServiceCollection();
		new ApplicationServiceInstaller().Install(services, EmptyConfiguration());
		services.AddScoped<IValidator<SampleCommand>, SampleCommandValidator>();
		using var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var behavior = scope.ServiceProvider.GetRequiredService<IPipelineBehavior<SampleCommand, Result>>();

		var result = await behavior.Handle(new SampleCommand(string.Empty), _ => Task.FromResult(Result.Success()), CancellationToken.None);

		result.IsFailure.Should().BeTrue();
	}

	/// <summary>
	/// Verifies that a missing service collection is rejected.
	/// </summary>
	[Fact]
	public void Install_NullServices_ShouldThrowArgumentNullException()
	{
		var act = () => new ApplicationServiceInstaller().Install(null!, EmptyConfiguration());

		act.Should().Throw<ArgumentNullException>();
	}
	#endregion

	#region Assembly Tests
	/// <summary>
	/// Verifies that the reference points at the Application assembly.
	/// </summary>
	[Fact]
	public void Assembly_ShouldBeTheApplicationAssembly()
	{
		AssemblyReference.Assembly.GetName().Name.Should().Be("ParkingTracker.Application");
	}
	#endregion
}
