using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace GIllionaire;

internal sealed class MainWindow : Window
{
    private readonly Plugin plugin;
    private int gilAmount = 1_000_000;

    internal MainWindow(Plugin plugin)
        : base("Gillionaire##MainWindow")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 210),
            MaximumSize = new Vector2(560, 360),
        };
    }

    public override void Draw()
    {
        ImGui.TextUnformatted("Automatically send gil in 1,000,000-gil trades.");
        ImGui.Spacing();

        ImGui.TextUnformatted(plugin.HasTarget ? "Target: ready" : "Target: select a player first");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputInt("##GilAmount", ref gilAmount, 100_000, 1_000_000);
        gilAmount = Math.Clamp(gilAmount, 1, 999_999_999);

        ImGui.BeginDisabled(plugin.IsTradeInProgress || !plugin.HasTarget);
        if (ImGui.Button("Start trading", new Vector2(-1, 0)))
            plugin.TryStartTrade(gilAmount);
        ImGui.EndDisabled();

        if (plugin.IsTradeInProgress)
        {
            if (ImGui.Button("Cancel sequence", new Vector2(-1, 0)))
                plugin.CancelTrade();
            ImGui.TextUnformatted($"Remaining: {plugin.RemainingGil:N0} gil");
        }

        ImGui.Spacing();
        ImGui.TextWrapped(plugin.StatusMessage);
    }
}
