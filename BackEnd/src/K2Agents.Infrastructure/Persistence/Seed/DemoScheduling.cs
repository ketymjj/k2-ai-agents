using K2Agents.Application.Scheduling;

namespace K2Agents.Infrastructure.Persistence.Seed;

/// <summary>Agendas de demonstração (serviços e expediente fictícios).</summary>
internal static class DemoScheduling
{
    private static IReadOnlyList<TimeRange> R(params (string Start, string End)[] ranges) => ranges.Select(r => new TimeRange(r.Start, r.End)).ToList();

    private static Dictionary<string, IReadOnlyList<TimeRange>> Week(IReadOnlyList<TimeRange> weekday, IReadOnlyList<TimeRange>? saturday = null) => new()
    {
        ["monday"] = weekday, ["tuesday"] = weekday, ["wednesday"] = weekday, ["thursday"] = weekday, ["friday"] = weekday,
        ["saturday"] = saturday ?? [], ["sunday"] = []
    };

    public static readonly Dictionary<string, SchedulingSettings> ByCompany = new()
    {
        ["studiobella"] = new SchedulingSettings
        {
            SlotMinutes = 30, MinAdvanceMinutes = 120, MaxDaysAhead = 30,
            Services = [new("Corte feminino", 60), new("Escova", 45), new("Manicure", 45), new("Design de sobrancelha", 30)],
            WeeklyHours = new Dictionary<string, IReadOnlyList<TimeRange>>
            {
                ["tuesday"] = R(("09:00", "12:00"), ("13:00", "19:00")), ["wednesday"] = R(("09:00", "12:00"), ("13:00", "19:00")),
                ["thursday"] = R(("09:00", "12:00"), ("13:00", "19:00")), ["friday"] = R(("09:00", "12:00"), ("13:00", "19:00")),
                ["saturday"] = R(("09:00", "15:00"))
            }
        },
        ["petvida"] = new SchedulingSettings
        {
            SlotMinutes = 30, MinAdvanceMinutes = 60, MaxDaysAhead = 21,
            Services = [new("Consulta clínica", 30), new("Vacinação", 20), new("Banho e tosa", 90)],
            WeeklyHours = Week(R(("08:00", "12:00"), ("14:00", "20:00")), R(("08:00", "14:00")))
        },
        ["energia"] = new SchedulingSettings
        {
            SlotMinutes = 60, MinAdvanceMinutes = 180, MaxDaysAhead = 14,
            Services = [new("Aula experimental", 60), new("Avaliação física", 45)],
            WeeklyHours = Week(R(("06:00", "12:00"), ("17:00", "22:00")), R(("08:00", "12:00")))
        }
    };
}
