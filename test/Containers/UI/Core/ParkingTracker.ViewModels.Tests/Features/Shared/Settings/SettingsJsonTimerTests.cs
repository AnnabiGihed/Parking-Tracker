using ParkingTracker.Domain.Features.Cars.ValueObjects;
using ParkingTracker.ViewModels.Features.Shared.Settings;
using ParkingTracker.ViewModels.Tests.Features.Shared.TestDoubles;

namespace ParkingTracker.ViewModels.Tests.Features.Shared.Settings;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for the <c>pt.timer</c> and <c>pt.bgAlerts</c> values of <see cref="SettingsJson"/> and
///              <see cref="SettingsStore"/>: the shapes <c>app.js</c> wrote are read unchanged, unreadable text means no timer, the
///              alerts are on unless the value is <c>false</c>, and both go to the plain storage.
/// </summary>
public sealed class SettingsJsonTimerTests
{
	#region Timer Tests
	/// <summary>
	/// Verifies that the value the first version wrote is read, with the end as milliseconds since 1970.
	/// </summary>
	[Fact]
	public void ParseTimer_FirstVersionValue_ShouldReadEndAndMinutes()
	{
		var timer = SettingsJson.ParseTimer("""{"end":1794996000000,"minutes":120}""");

		timer.Should().Be(new ParkingTimer(DateTimeOffset.FromUnixTimeMilliseconds(1794996000000), 120));
	}

	/// <summary>
	/// Verifies that text that holds no timer reads as no timer.
	/// </summary>
	/// <param name="json">The saved text.</param>
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("not json")]
	[InlineData("[1]")]
	[InlineData("""{"minutes":30}""")]
	public void ParseTimer_NoTimer_ShouldReturnNull(string? json)
	{
		SettingsJson.ParseTimer(json).Should().BeNull();
	}

	/// <summary>
	/// Verifies that writing then reading a timer gives it back, in the property order of the first version.
	/// </summary>
	[Fact]
	public void SerializeTimer_ThenParse_ShouldRoundTrip()
	{
		var timer = new ParkingTimer(DateTimeOffset.FromUnixTimeMilliseconds(1794996000000), 60);

		var json = SettingsJson.SerializeTimer(timer);

		json.Should().Be("""{"end":1794996000000,"minutes":60}""");
		SettingsJson.ParseTimer(json).Should().Be(timer);
	}
	#endregion

	#region Alerts Tests
	/// <summary>
	/// Verifies that the alerts are on unless the saved value is <c>false</c>.
	/// </summary>
	/// <param name="json">The saved text.</param>
	/// <param name="expected">Whether the alerts are on.</param>
	[Theory]
	[InlineData(null, true)]
	[InlineData("true", true)]
	[InlineData("false", false)]
	[InlineData(" false ", false)]
	[InlineData("garbage", true)]
	public void ParseAlertsEnabled_Value_ShouldBeOnUnlessFalse(string? json, bool expected)
	{
		SettingsJson.ParseAlertsEnabled(json).Should().Be(expected);
	}
	#endregion

	#region Store Tests
	/// <summary>
	/// Verifies that the timer and the switch go to the plain storage under their keys and can be read and cleared.
	/// </summary>
	/// <returns>A task that completes when the assertions have run.</returns>
	[Fact]
	public async Task Store_TimerAndAlerts_ShouldUsePlainStorageUnderTheirKeys()
	{
		var secure = new InMemoryKeyValueStorage();
		var plain = new InMemoryKeyValueStorage();
		var store = new SettingsStore(secure, plain);
		var timer = new ParkingTimer(DateTimeOffset.FromUnixTimeMilliseconds(1794996000000), 60);

		await store.SaveTimerAsync(timer);
		await store.SaveAlertsEnabledAsync(false);

		secure.Values.Should().BeEmpty();
		plain.Values.Should().ContainKey("pt.timer").And.ContainKey("pt.bgAlerts");
		(await store.GetTimerAsync()).Should().Be(timer);
		(await store.GetAlertsEnabledAsync()).Should().BeFalse();

		await store.ClearTimerAsync();

		(await store.GetTimerAsync()).Should().BeNull();
		(await new SettingsStore(secure, new InMemoryKeyValueStorage()).GetAlertsEnabledAsync()).Should().BeTrue();
	}
	#endregion
}
