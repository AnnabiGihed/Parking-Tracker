using ParkingTracker.Domain.Decisions;
using ParkingTracker.Domain.Geometry;
using ParkingTracker.Domain.Pricing;
using ParkingTracker.Domain.ReferenceData;
using ParkingTracker.Domain.Tests.TestDoubles;
using ParkingTracker.Domain.Zones;

namespace ParkingTracker.Domain.Tests.Decisions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Unit tests for <see cref="SpotDecider"/> and <see cref="SpotAnnotator"/>: the answers for a missing or expired
///              card, a poor fix, a failed source, each zone type and each municipality kind, reserved spaces, roadworks and
///              paid hours. The recorded real-world places are in the parity tests of the Infrastructure test project.
/// </summary>
public sealed class SpotDeciderTests
{
	#region Test Infrastructure
	private static readonly DateTimeOffset Sunday = new(2026, 11, 15, 11, 0, 0, TimeSpan.FromHours(1));

	private static SpotDecision Decide(DecisionScript script, SpotQuestion question) => script.Run(SpotDecider.Begin(question));

	private static DecisionScript ScriptFor(ZoneSurface surface, ParkingRule? rule = null)
	{
		var script = new DecisionScript
		{
			Zones = DecisionScript.Ok(surface),
			Spaces = Listed(Array.Empty<ReservedSpace>()),
			Works = Listed(Array.Empty<WorkItem>()),
		};
		if (rule is not null && surface.ZoneCode is not null)
		{
			script.Rules = DecisionScript.RuleOf(surface.ZoneCode, rule);
		}

		return script;
	}

	private static Pivot.Framework.Domain.Shared.Result<IReadOnlyList<T>> Listed<T>(T[] values)
	{
		return Pivot.Framework.Domain.Shared.Result.Success<IReadOnlyList<T>>(values);
	}

	private static DecisionContext WholeMunicipality() => ZoneBuilder.Context(MunicipalityKind.Whole);
	#endregion

	#region Precondition Tests
	/// <summary>
	/// Verifies that without a card the answer is "can't verify" and nothing is fetched.
	/// </summary>
	[Fact]
	public void Begin_NoCard_ShouldCantVerifyWithoutAskingAnySource()
	{
		var script = new DecisionScript();
		var context = new DecisionContext(null, ZoneBuilder.Municipality(MunicipalityKind.Whole), null, null);

		var decision = Decide(script, ZoneBuilder.Question(context));

		decision.Verdict.Should().Be(Verdict.CantVerify);
		decision.Detail.Should().Be("Add your residents' parking card first (card button, top right).");
		script.Asked.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that an expired card is answered with "pay" and the date it expired.
	/// </summary>
	[Fact]
	public void Begin_ExpiredCard_ShouldAnswerPayWithTheExpiryDate()
	{
		var script = new DecisionScript();

		var decision = Decide(script, ZoneBuilder.Question(WholeMunicipality(), now: new DateTimeOffset(2028, 1, 5, 11, 0, 0, TimeSpan.FromHours(1))));

		decision.Verdict.Should().Be(Verdict.Pay);
		decision.Detail.Should().Be("Your card expired on 31/12/2027.");
		script.Asked.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that being offline gives "can't verify".
	/// </summary>
	[Fact]
	public void Begin_Offline_ShouldCantVerify()
	{
		var decision = Decide(new DecisionScript(), ZoneBuilder.Question(WholeMunicipality(), isOnline: false));

		decision.Verdict.Should().Be(Verdict.CantVerify);
		decision.Detail.Should().Be("No internet connection. The app only answers with live data.");
	}

	/// <summary>
	/// Verifies that a GPS fix worse than 25 m gives "can't verify" with the rounded accuracy.
	/// </summary>
	[Fact]
	public void Begin_PoorGps_ShouldCantVerifyWithTheAccuracy()
	{
		var script = new DecisionScript();

		var decision = Decide(script, ZoneBuilder.Question(WholeMunicipality(), accuracy: 25.5));

		decision.Detail.Should().Be("GPS is not precise enough yet (±26 m, need ±25 m). Waiting for a better fix…");
		script.Asked.Should().BeEmpty();
	}
	#endregion

	#region Source Failure Tests
	/// <summary>
	/// Verifies that a failed surfaces request gives "can't verify" with the reason and no zone.
	/// </summary>
	[Fact]
	public void Decide_SurfacesFail_ShouldCantVerifyWithTheReason()
	{
		var script = new DecisionScript { Zones = DecisionScript.Failed<IReadOnlyList<ZoneSurface>>("parking.brussels HTTP 500") };

		var decision = Decide(script, ZoneBuilder.Question(WholeMunicipality()));

		decision.Verdict.Should().Be(Verdict.CantVerify);
		decision.Detail.Should().Be("Could not reach parking.brussels (parking.brussels HTTP 500).");
		decision.Zone.Should().BeNull();
		script.Asked.Should().Equal("zones");
	}

	/// <summary>
	/// Verifies that a place far from every surface is not taken to be on a street.
	/// </summary>
	[Fact]
	public void Decide_NoSurfaceNearby_ShouldAskForThePinOnTheStreet()
	{
		var far = ZoneBuilder.Surface("Rue Lointaine", 4, center: new GeoPoint(50.8350, 4.3606));
		var script = ScriptFor(far);

		var decision = Decide(script, ZoneBuilder.Question(WholeMunicipality()));

		decision.Detail.Should().Be("This point is not on a street with parking rules. Put the pin on the street, where you park.");
	}

	/// <summary>
	/// Verifies that a failed reserved-spaces request gives "can't verify" that keeps only the street.
	/// </summary>
	[Fact]
	public void Decide_SpacesFail_ShouldCantVerifyAndDropTheTariff()
	{
		var surface = ZoneBuilder.Surface("Rue Test", 4);
		var script = ScriptFor(surface, RuleBuilder.Create(ZoneType.Derogation));
		script.Spaces = DecisionScript.Failed<IReadOnlyList<ReservedSpace>>("spaces down");

		var decision = Decide(script, ZoneBuilder.Question(WholeMunicipality()));

		decision.Verdict.Should().Be(Verdict.CantVerify);
		decision.Detail.Should().Be("Could not check reserved parking spaces (spaces down).");
		decision.Tariff.Should().BeNull();
		decision.Zone!.Surface.Should().Be(surface);
		decision.Zone.Rule.Should().BeNull();
	}
	#endregion

	#region Zone Type Tests
	/// <summary>
	/// Verifies that an unregulated surface is free for everyone and needs no rule.
	/// </summary>
	[Fact]
	public void Decide_UnregulatedSurface_ShouldBeFreeWithoutFetchingARule()
	{
		var script = ScriptFor(ZoneBuilder.Surface("Rue Test", 0));

		var decision = Decide(script, ZoneBuilder.Question(WholeMunicipality()));

		decision.Verdict.Should().Be(Verdict.Free);
		decision.Detail.Should().Be("Rue Test has no parking rules: free for everyone.");
		script.Asked.Should().Equal("zones", "spaces", "works");
	}

	/// <summary>
	/// Verifies that a red zone is paid whatever the card, and that the tariff of the rule is added.
	/// </summary>
	[Fact]
	public void Decide_RedZoneDuringPaidHours_ShouldPayAndShowTheTariff()
	{
		var rule = RuleBuilder.Create(ZoneType.Payable);
		var script = ScriptFor(ZoneBuilder.Surface("Rue Test", 1), rule);

		var decision = Decide(script, ZoneBuilder.Question(WholeMunicipality()));

		decision.Verdict.Should().Be(Verdict.Pay);
		decision.Status.Should().Be(SpotStatus.Pay);
		decision.Detail.Should().Be("Rue Test is a red zone: residents' cards are not valid here. Pay at the meter or with an app.");
		decision.Tariff.Should().Be(TariffText.Describe(rule));
	}

	/// <summary>
	/// Verifies that outside the paid hours "pay" becomes "free now", with the schedule and when it starts again.
	/// </summary>
	[Fact]
	public void Decide_RedZoneOnSunday_ShouldBeFreeNowAndKeepTheEarlierAnswerAsANote()
	{
		var script = ScriptFor(ZoneBuilder.Surface("Rue Test", 1), RuleBuilder.Create(ZoneType.Payable));

		var decision = Decide(script, ZoneBuilder.Question(WholeMunicipality(), now: Sunday));

		decision.Verdict.Should().Be(Verdict.FreeNow);
		decision.Title.Should().Be("FREE NOW");
		decision.Detail.Should().Be("No need to pay right now: here, paid parking applies Mon–Sat 09:00–21:00. It starts again tomorrow 09:00.");
		decision.Notes.Should().Equal("During paid hours: Rue Test is a red zone: residents' cards are not valid here. Pay at the meter or with an app.");
	}

	/// <summary>
	/// Verifies that an orange zone without a rule cannot be told and is not guessed.
	/// </summary>
	[Fact]
	public void Decide_OrangeZoneWithoutRule_ShouldCantVerify()
	{
		var script = ScriptFor(ZoneBuilder.Surface("Rue Test", 2, zoneCode: null));

		var decision = Decide(script, ZoneBuilder.Question(WholeMunicipality()));

		decision.Verdict.Should().Be(Verdict.CantVerify);
		decision.Detail.Should().Contain("the rules of this orange zone could not be checked");
	}

	/// <summary>
	/// Verifies that an event zone is not decided by the normal rules.
	/// </summary>
	[Fact]
	public void Decide_EventZone_ShouldCantVerifyAndFollowTheSigns()
	{
		var script = ScriptFor(ZoneBuilder.Surface("Rue Test", 6, zoneCode: null));

		var decision = Decide(script, ZoneBuilder.Question(WholeMunicipality()));

		decision.Detail.Should().Be("Rue Test is an event zone: the normal rules don't apply. Follow the signs.");
	}

	/// <summary>
	/// Verifies that a reserved zone names its use and that residents' cards are not valid.
	/// </summary>
	[Fact]
	public void Decide_ReservedZone_ShouldPayAndNameTheUse()
	{
		var rule = RuleBuilder.Create(ZoneType.Reserved) with { Name = "Car-sharing" };
		var script = ScriptFor(ZoneBuilder.Surface("Rue Test", 2), rule);

		var decision = Decide(script, ZoneBuilder.Question(WholeMunicipality()));

		decision.Detail.Should().Be("Rue Test: reserved zone (Car-sharing). Residents' cards are not valid here.");
	}

	/// <summary>
	/// Verifies that a rule that could not be read is mentioned and that the app then assumes paid hours apply.
	/// </summary>
	[Fact]
	public void Decide_RuleFails_ShouldAddANoteAndAssumeThePaidHoursApply()
	{
		var script = ScriptFor(ZoneBuilder.Surface("Rue Test", 1));
		script.Rules = DecisionScript.Failed<IReadOnlyDictionary<string, ParkingRule>>("rules down");

		var decision = Decide(script, ZoneBuilder.Question(WholeMunicipality(), now: Sunday));

		decision.Verdict.Should().Be(Verdict.Pay);
		decision.Notes.Should().Equal("Could not check the paid hours (rules down): the app assumes they apply.");
	}
	#endregion

	#region Card Scope Tests
	/// <summary>
	/// Verifies that a whole-municipality card is valid in a card zone of its municipality.
	/// </summary>
	[Fact]
	public void Decide_GreenZoneOfAWholeMunicipalityCard_ShouldBeFree()
	{
		var script = ScriptFor(ZoneBuilder.Surface("Rue Test", 4), RuleBuilder.Create(ZoneType.Derogation));

		var decision = Decide(script, ZoneBuilder.Question(WholeMunicipality()));

		decision.Verdict.Should().Be(Verdict.Free);
		decision.Detail.Should().Be("Rue Test: your card is valid in this zone everywhere in Testville.");
	}

	/// <summary>
	/// Verifies that a blue zone outside the card's scope asks for a parking disc.
	/// </summary>
	[Fact]
	public void Decide_BlueZoneOfAnotherMunicipality_ShouldAskForTheDisc()
	{
		var surface = ZoneBuilder.Surface("Rue Test", 5, municipality: "UCCLE");
		var script = ScriptFor(surface, RuleBuilder.Create(ZoneType.Disk));

		var decision = Decide(script, ZoneBuilder.Question(WholeMunicipality()));

		decision.Verdict.Should().Be(Verdict.ParkingDisc);
		decision.Detail.Should().Be("Rue Test is in Uccle, not in Testville: your card doesn't cover it. Blue zone: put your parking disc, time is limited.");
	}

	/// <summary>
	/// Verifies that a municipality that does not publish its sectors gives "check your card" with its note.
	/// </summary>
	[Fact]
	public void Decide_UnknownMunicipalityKind_ShouldAskTheUserToCheckTheCard()
	{
		var script = ScriptFor(ZoneBuilder.Surface("Rue Test", 4), RuleBuilder.Create(ZoneType.Derogation));

		var decision = Decide(script, ZoneBuilder.Question(ZoneBuilder.Context(MunicipalityKind.Unknown)));

		decision.Verdict.Should().Be(Verdict.CheckCard);
		decision.Detail.Should().Be("Rue Test: Check your card..");
	}

	/// <summary>
	/// Verifies that an area-based card without chosen areas asks the user to choose them, without fetching the areas of the map.
	/// </summary>
	[Fact]
	public void Decide_MaillesCardWithoutAreas_ShouldAskTheUserToChooseThem()
	{
		var script = ScriptFor(ZoneBuilder.Surface("Rue Test", 4), RuleBuilder.Create(ZoneType.Derogation));
		script.Mailles = Listed(Array.Empty<SectorMaille>());

		var decision = Decide(script, ZoneBuilder.Question(ZoneBuilder.Context(MunicipalityKind.Mailles)));

		decision.Verdict.Should().Be(Verdict.CheckCard);
		decision.Detail.Should().Contain("choose the areas (mailles) printed on your card");
		script.Asked.Should().Contain("mailles");
	}

	/// <summary>
	/// Verifies that a failed sector-areas request gives "can't verify" with the reason.
	/// </summary>
	[Fact]
	public void Decide_MaillesFail_ShouldCantVerifyWithTheReason()
	{
		var script = ScriptFor(ZoneBuilder.Surface("Rue Test", 4), RuleBuilder.Create(ZoneType.Derogation));
		script.Mailles = DecisionScript.Failed<IReadOnlyList<SectorMaille>>("maille down");

		var decision = Decide(script, ZoneBuilder.Question(ZoneBuilder.Context(MunicipalityKind.Mailles)));

		decision.Detail.Should().Be("Could not get the sector map from parking.brussels (maille down).");
	}
	#endregion

	#region Reserved Space Tests
	/// <summary>
	/// Verifies that standing on a reserved space gives "reserved" whatever the card, keeping the earlier explanation.
	/// </summary>
	[Fact]
	public void Decide_OnADeliverySpace_ShouldBeReserved()
	{
		var script = ScriptFor(ZoneBuilder.Surface("Rue Test", 4), RuleBuilder.Create(ZoneType.Derogation));
		script.Spaces = Listed([new ReservedSpace(ZoneBuilder.Here, 1)]);

		var decision = Decide(script, ZoneBuilder.Question(WholeMunicipality()));

		decision.Verdict.Should().Be(Verdict.Reserved);
		decision.Status.Should().Be(SpotStatus.Pay);
		decision.Detail.Should().Be("🚚 Delivery space: you can't park here, whatever your card. Rue Test: your card is valid in this zone everywhere in Testville.");
		decision.ReservedSpace!.ReservationType.Should().Be(1);
	}

	/// <summary>
	/// Verifies that a reserved space nearby but not under the user is only a warning.
	/// </summary>
	[Fact]
	public void Decide_NearADeliverySpace_ShouldWarnButStayFree()
	{
		var script = ScriptFor(ZoneBuilder.Surface("Rue Test", 4), RuleBuilder.Create(ZoneType.Derogation));
		var nearby = new GeoPoint(ZoneBuilder.Here.Latitude + 0.00003, ZoneBuilder.Here.Longitude);
		script.Spaces = Listed([new ReservedSpace(nearby, 1)]);

		var decision = Decide(script, ZoneBuilder.Question(WholeMunicipality()));

		decision.Verdict.Should().Be(Verdict.Free);
		decision.Notes.Should().ContainSingle().Which.Should().Be("⚠️ 🚚 Delivery space 3 m away — don't park on it.");
	}

	/// <summary>
	/// Verifies that a time-limited space turns "free" into "time limited" and says why.
	/// </summary>
	[Fact]
	public void Decide_OnATimeLimitedSpace_ShouldBeTimeLimited()
	{
		var script = ScriptFor(ZoneBuilder.Surface("Rue Test", 4), RuleBuilder.Create(ZoneType.Derogation));
		script.Spaces = Listed([new ReservedSpace(ZoneBuilder.Here, 16)]);

		var decision = Decide(script, ZoneBuilder.Question(WholeMunicipality()));

		decision.Verdict.Should().Be(Verdict.TimeLimited);
		decision.Status.Should().Be(SpotStatus.Unknown);
		decision.Notes.Should().Equal("⏱️ 30-minute zone: parking time is limited here, your card doesn't extend it.");
	}
	#endregion

	#region Roadworks Tests
	/// <summary>
	/// Verifies that roadworks at the place are reported with where and until when.
	/// </summary>
	[Fact]
	public void Decide_RoadworksAtThePlace_ShouldAddANote()
	{
		var script = ScriptFor(ZoneBuilder.Surface("Rue Test", 4), RuleBuilder.Create(ZoneType.Derogation));
		script.Works = Listed([new WorkItem(WorkKind.Works, [ZoneBuilder.Here], null, "Travaux", "Rue Test", "2026-12-18")]);

		var decision = Decide(script, ZoneBuilder.Question(WholeMunicipality()));

		decision.Notes.Should().Equal("🚧 Travaux — Rue Test (until 18/12/2026). Parking may be forbidden: check the signs.");
	}

	/// <summary>
	/// Verifies that roadworks far away are not reported.
	/// </summary>
	[Fact]
	public void Decide_RoadworksFarAway_ShouldAddNoNote()
	{
		var script = ScriptFor(ZoneBuilder.Surface("Rue Test", 4), RuleBuilder.Create(ZoneType.Derogation));
		script.Works = Listed([new WorkItem(WorkKind.Works, [new GeoPoint(50.83, 4.37)], null, "Travaux", string.Empty, null)]);

		var decision = Decide(script, ZoneBuilder.Question(WholeMunicipality()));

		decision.Notes.Should().BeEmpty();
	}

	/// <summary>
	/// Verifies that a failed roadworks request is reported in a note and does not change the answer.
	/// </summary>
	[Fact]
	public void Decide_RoadworksFail_ShouldAddANoteAndKeepTheAnswer()
	{
		var script = ScriptFor(ZoneBuilder.Surface("Rue Test", 4), RuleBuilder.Create(ZoneType.Derogation));
		script.Works = DecisionScript.Failed<IReadOnlyList<WorkItem>>("roadworks HTTP 503");

		var decision = Decide(script, ZoneBuilder.Question(WholeMunicipality()));

		decision.Verdict.Should().Be(Verdict.Free);
		decision.Notes.Should().Equal("Could not check roadworks (roadworks HTTP 503).");
	}
	#endregion
}
