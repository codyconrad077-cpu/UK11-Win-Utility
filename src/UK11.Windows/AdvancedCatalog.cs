using UK11.Core;

namespace UK11.Windows;

public static class AdvancedCatalog
{
    public static IReadOnlyList<Tweak> Build(IReadOnlyList<Tweak> discovered)
    {
        var items = new List<Tweak>();
        foreach (var (id, title) in AdvancedBlueprints.All)
        {
            foreach (var ac in new[] { true, false })
            {
                var backend = ac ? "PowerAC" : "PowerDC";
                var source = discovered.FirstOrDefault(t => t.Name == id && t.Backend == backend);
                if (source is null) continue; // Hardware/OS absent targets are never fabricated.
                var target = PowerSettings.PerformanceDefault(source);
                items.Add(source with
                {
                    Desired = target ?? source.Desired,
                    Writable = source.Writable && target is not null,
                    Category = "Advanced",
                    Title = title.Replace("Processor performance", "CPU performance").Replace("Processor", "CPU") + (ac ? " · Plugged in" : " · Battery"),
                    Effect = "Use the Windows High Performance default for this individual parameter. " + source.Effect + (target is null ? " Windows did not report a valid performance default; editing is unavailable." : " Higher power consumption, heat and fan activity are possible. This is not a measured FPS improvement.")
                });
            }
        }
        return items;
    }
}
