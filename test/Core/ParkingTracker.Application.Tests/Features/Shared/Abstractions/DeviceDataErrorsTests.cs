using ParkingTracker.Application.Features.Shared.Abstractions;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Tests.Features.Shared.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="DeviceDataErrors"/>: the plain "nothing is stored" failure and the "key cannot be read now"
///              failure are told apart from each other and from any other fault.
/// </summary>
public sealed class DeviceDataErrorsTests
{
	#region IsNotStored Tests
	/// <summary>
	/// Verifies that the not-stored failure is recognised and every other result is not.
	/// </summary>
	[Fact]
	public void IsNotStored_Results_ShouldRecogniseOnlyTheNotStoredFailure()
	{
		DeviceDataErrors.IsNotStored(Result.Failure(DeviceDataErrors.NotStored, ResultExceptionType.NotFound)).Should().BeTrue();
		DeviceDataErrors.IsNotStored(Result.Failure(new Error("Other.Error", "other"))).Should().BeFalse();
		DeviceDataErrors.IsNotStored(Result.Success()).Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a missing result is rejected.
	/// </summary>
	[Fact]
	public void IsNotStored_NullResult_ShouldThrow()
	{
		var check = () => DeviceDataErrors.IsNotStored(null!); // null on purpose: proves the guard clause rejects it

		check.Should().Throw<ArgumentNullException>();
	}
	#endregion

	#region IsKeyUnavailable Tests
	/// <summary>
	/// Verifies that the key-unavailable failure is recognised, and that it is neither "nothing stored" nor any other result.
	/// </summary>
	[Fact]
	public void IsKeyUnavailable_Results_ShouldRecogniseOnlyTheKeyUnavailableFailure()
	{
		var unavailable = Result.Failure(DeviceDataErrors.KeyUnavailable);

		DeviceDataErrors.IsKeyUnavailable(unavailable).Should().BeTrue();
		DeviceDataErrors.IsNotStored(unavailable).Should().BeFalse();
		DeviceDataErrors.IsKeyUnavailable(Result.Failure(DeviceDataErrors.NotStored, ResultExceptionType.NotFound)).Should().BeFalse();
		DeviceDataErrors.IsKeyUnavailable(Result.Failure(new Error("Other.Error", "other"))).Should().BeFalse();
		DeviceDataErrors.IsKeyUnavailable(Result.Success()).Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a missing result is rejected.
	/// </summary>
	[Fact]
	public void IsKeyUnavailable_NullResult_ShouldThrow()
	{
		var check = () => DeviceDataErrors.IsKeyUnavailable(null!); // null on purpose: proves the guard clause rejects it

		check.Should().Throw<ArgumentNullException>();
	}
	#endregion

	#region IsKeyLost Tests
	/// <summary>
	/// Verifies that the key-lost failure is recognised, and that it is neither "key unavailable" nor "nothing stored".
	/// </summary>
	[Fact]
	public void IsKeyLost_Results_ShouldRecogniseOnlyTheKeyLostFailure()
	{
		var lost = Result.Failure(DeviceDataErrors.KeyLost);

		DeviceDataErrors.IsKeyLost(lost).Should().BeTrue();
		DeviceDataErrors.IsKeyUnavailable(lost).Should().BeFalse();
		DeviceDataErrors.IsNotStored(lost).Should().BeFalse();
		DeviceDataErrors.IsKeyLost(Result.Failure(DeviceDataErrors.KeyUnavailable)).Should().BeFalse();
		DeviceDataErrors.IsKeyLost(Result.Success()).Should().BeFalse();
	}

	/// <summary>
	/// Verifies that a missing result is rejected.
	/// </summary>
	[Fact]
	public void IsKeyLost_NullResult_ShouldThrow()
	{
		var check = () => DeviceDataErrors.IsKeyLost(null!); // null on purpose: proves the guard clause rejects it

		check.Should().Throw<ArgumentNullException>();
	}
	#endregion
}
