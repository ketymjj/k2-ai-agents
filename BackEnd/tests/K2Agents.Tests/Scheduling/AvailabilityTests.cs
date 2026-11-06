using K2Agents.Application.Automations;
using K2Agents.Application.Scheduling;

namespace K2Agents.Tests.Scheduling;

public class AvailabilityTests
{
    private static readonly SchedulingSettings Settings = new()
    {
        TimeZone = "America/Sao_Paulo",
        SlotMinutes = 30,
        MinAdvanceMinutes = 60,
        MaxDaysAhead = 30,
        Services = [new("Corte", 60)],
        WeeklyHours = new Dictionary<string, IReadOnlyList<TimeRange>>
        {
            ["monday"] = [new("09:00", "12:00"), new("13:00", "15:00")]
        }
    };

    // Segunda-feira, 02/11/2026. São Paulo = UTC-3 (sem horário de verão).
    private static readonly DateOnly Monday = new(2026, 11, 2);
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Slots_FollowBusinessHoursInLocalTime()
    {
        var slots = Availability.Slots(Settings, Monday, 60, [], Now);

        // 09:00–12:00 com serviço de 60 min e passo de 30: 09:00, 09:30, 10:00, 10:30, 11:00 → 5; 13:00–15:00: 13:00, 13:30, 14:00 → 3.
        Assert.Equal(8, slots.Count);
        Assert.Equal(new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc), slots[0].Start); // 09:00 local = 12:00 UTC
        Assert.Equal("02/11 09:00", slots[0].LocalLabel);
        Assert.DoesNotContain(slots, s => s.LocalLabel.EndsWith("11:30")); // não cabe antes das 12:00
    }

    [Fact]
    public void ClosedDay_HasNoSlots() => Assert.Empty(Availability.Slots(Settings, Monday.AddDays(1), 60, [], Now));

    [Fact]
    public void BusyIntervals_RemoveOverlappingSlots_ButAllowAdjacentOnes()
    {
        var busy = new List<(DateTime, DateTime)> { (new DateTime(2026, 11, 2, 13, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 2, 14, 0, 0, DateTimeKind.Utc)) }; // 10:00–11:00 local
        var labels = Availability.Slots(Settings, Monday, 60, busy, Now).Select(s => s.LocalLabel[6..]).ToList();

        Assert.DoesNotContain("09:30", labels); // 09:30–10:30 sobrepõe
        Assert.DoesNotContain("10:00", labels);
        Assert.DoesNotContain("10:30", labels);
        Assert.Contains("09:00", labels);       // 09:00–10:00 encosta, não sobrepõe
        Assert.Contains("11:00", labels);       // 11:00–12:00 encosta
    }

    [Fact]
    public void MinAdvance_HidesSlotsTooSoon()
    {
        var now = new DateTime(2026, 11, 2, 12, 30, 0, DateTimeKind.Utc); // 09:30 local; antecedência 60 → a partir de 10:30
        var labels = Availability.Slots(Settings, Monday, 60, [], now).Select(s => s.LocalLabel[6..]).ToList();
        Assert.Equal("10:30", labels[0]);
    }

    [Fact]
    public void MaxDaysAhead_IsRespected()
    {
        var far = Monday.AddDays(35);
        Assert.Empty(Availability.Slots(Settings, far, 60, [], Now));
    }

    [Fact]
    public void WithinBusinessHours_ChecksWholeInterval()
    {
        Assert.True(Availability.WithinBusinessHours(Settings, new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 2, 13, 0, 0, DateTimeKind.Utc)));
        Assert.False(Availability.WithinBusinessHours(Settings, new DateTime(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc), new DateTime(2026, 11, 2, 15, 30, 0, DateTimeKind.Utc))); // 11:30–12:30
        Assert.False(Availability.WithinBusinessHours(Settings, new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 3, 13, 0, 0, DateTimeKind.Utc))); // terça fechada
    }

    [Fact]
    public void Validator_RejectsOverlappingRangesAndBadValues()
    {
        var validator = new SchedulingSettingsValidator();
        var bad = Settings with
        {
            TimeZone = "Marte/Olympus",
            SlotMinutes = 7,
            Services = [new("Corte", 60), new("corte", 30)],
            WeeklyHours = new Dictionary<string, IReadOnlyList<TimeRange>> { ["monday"] = [new("09:00", "12:00"), new("11:00", "13:00")], ["funday"] = [] }
        };
        var result = validator.Validate(bad);
        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 4);
        Assert.True(validator.Validate(Settings).IsValid);
    }
}

public class AutomationTemplateTests
{
    [Fact]
    public void Render_ReplacesOnlyKnownPlaceholders()
    {
        var text = AutomationEngine.Render("Novo lead: {contato} via {canal} {sistema} {}", new Dictionary<string, string>
        {
            ["contato"] = "Ana", ["canal"] = "WebChat", ["sistema"] = "NÃO DEVE APARECER"
        });
        Assert.Equal("Novo lead: Ana via WebChat {sistema} {}", text);
    }

    [Fact]
    public void ParseConfig_IgnoresGarbage() => Assert.Null(AutomationEngine.ParseConfig("isto não é json").Title);
}
