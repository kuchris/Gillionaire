using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Objects;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Dalamud.Game;
using Dalamud.Game.Chat;
using Dalamud.Interface.Windowing;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.UI;
using ECommons;
using ECommons.UIHelpers.AddonMasterImplementations;
using ECommons.Automation;
using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using System;
using System.Threading;


namespace GIllionaire;


public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] public static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] public static IGameGui GameGui { get; private set; } = null!;
    [PluginService] public static ITargetManager TargetManager { get; private set; } = null!;

    private const int MaxTradeAmount = 1_000_000; // 1 million gil cap per trade

    private int remainingGil = 0;
    private bool isTradeInProgress = false;
    private readonly WindowSystem windowSystem = new("Gillionaire");
    private readonly MainWindow mainWindow;
    private CancellationTokenSource tradeCancellation = new();

    internal int RemainingGil => remainingGil;
    internal bool IsTradeInProgress => isTradeInProgress;
    internal bool HasTarget => TargetManager.Target != null;
    internal string StatusMessage { get; private set; } = "Ready.";

    public Plugin()
    {
        ECommonsMain.Init(PluginInterface, this);
        mainWindow = new MainWindow(this);
        windowSystem.AddWindow(mainWindow);
        ChatGui.ChatMessage += OnChatMessage;
        Framework.Update += SelectYes;
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;
        CommandManager.AddHandler("/giltrade", new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Gillionaire window or start trading immediately: /giltrade [amount]"
        });

        Log.Information($"===Gillionaire Loaded===");
    }

    private unsafe void SelectYes(IFramework framework)
    {
        if (isTradeInProgress)
        {
            var addon = GameGui.GetAddonByName("SelectYesno");
            if (addon.IsNull) return;
            new AddonMaster.SelectYesno(addon.Address).Yes();
        }
    }

    private void OnChatMessage(IHandleableChatMessage chatMessage)
    {
        // Hacky way to check if the trade is complete
        // TODO: Replace with a more reliable method
        if (chatMessage.Message.TextValue.Contains("Trade complete.") && isTradeInProgress)
        {
            CompleteCurrentTrade();
        }
        else if (chatMessage.Message.TextValue.Contains("Trade canceled."))
        {
            CancelTrade("Gil trading canceled.");
        }
    }

    internal unsafe bool TryStartTrade(int gilAmount)
    {
        if (gilAmount <= 0)
        {
            StatusMessage = "Enter a positive gil amount.";
            return false;
        }
        if (TargetManager.Target == null)
        {
            StatusMessage = "No target selected. Target a player first.";
            return false;
        }

        var currentGil = InventoryManager.Instance()->GetGil();
        if (gilAmount > currentGil)
        {
            StatusMessage = "You do not have enough gil for this trade.";
            return false;
        }
        if (isTradeInProgress)
        {
            StatusMessage = "A trade sequence is already running.";
            return false;
        }

        tradeCancellation.Cancel();
        tradeCancellation.Dispose();
        tradeCancellation = new CancellationTokenSource();
        remainingGil = gilAmount;
        StatusMessage = $"Starting trade sequence for {gilAmount:N0} gil.";
        Log.Information($"Starting trade sequence for {gilAmount} gil");
        StartNextTrade();
        return true;
    }

    internal void CancelTrade(string message = "Trade sequence canceled.")
    {
        tradeCancellation.Cancel();
        isTradeInProgress = false;
        remainingGil = 0;
        StatusMessage = message;
        Log.Information(message);
    }

    private void StartNextTrade()
    {
        if (TargetManager.Target == null)
        {
            CancelTrade("No target selected. Target a player to continue.");
            return;
        }
        if (isTradeInProgress || remainingGil <= 0) return;

        isTradeInProgress = true;
        StatusMessage = $"Opening trade. {remainingGil:N0} gil remaining.";
        // Trade the player you're targeting
        Chat.ExecuteCommand("/trade");

        Schedule(() =>
        {
            int amountToSend = CalculateTradeAmount(remainingGil, MaxTradeAmount);
            remainingGil -= amountToSend;
            StatusMessage = $"Trading {amountToSend:N0} gil. {remainingGil:N0} remaining.";
            Log.Information($"Trading {amountToSend} gil. Remaining: {remainingGil}");
            TradeAmount(amountToSend);
        }, 800);
    }

    // Calculate amount to send in current trade
    public int CalculateTradeAmount(int totalRemaining, int maxPerTrade)
    {
        // If we can send it all, do so
        if (totalRemaining <= maxPerTrade)
            return totalRemaining;

        // Calculate the optimal amount to send
        int amountToSend = totalRemaining % maxPerTrade;

        // If the remainder is 0, we need to send the max amount
        if (amountToSend == 0)
            return maxPerTrade;

        return amountToSend;
    }

    internal static unsafe class NodeUtils
    {
        public static AtkComponentNode* GetAsAtkComponentNode(AtkResNode* me) =>
            (ushort)me->Type < 1000
                ? throw new ArgumentException("Node is not a component node", nameof(me))
                : (AtkComponentNode*)me;

        public static T* GetComponent<T>(AtkComponentNode* node) where T : unmanaged =>
            GetNodeComponentType(node) != GetComponentType(typeof(T))
                ? throw new ArgumentException($"{GetNodeComponentType(node)} node is not a {typeof(T).Name}", nameof(node))
                : (T*)node->Component;

        public static T* GetAsAtkComponent<T>(AtkResNode* me) where T : unmanaged =>
            GetComponent<T>(GetAsAtkComponentNode(me));

        private static ComponentType GetComponentType(Type t)
        {
            if (t == typeof(AtkComponentBase)) return ComponentType.Base;
            if (t == typeof(AtkComponentButton)) return ComponentType.Button;
            if (t == typeof(AtkComponentWindow)) return ComponentType.Window;
            if (t == typeof(AtkComponentCheckBox)) return ComponentType.CheckBox;
            if (t == typeof(AtkComponentRadioButton)) return ComponentType.RadioButton;
            if (t == typeof(AtkComponentGaugeBar)) return ComponentType.GaugeBar;
            if (t == typeof(AtkComponentSlider)) return ComponentType.Slider;
            if (t == typeof(AtkComponentTextInput)) return ComponentType.TextInput;
            if (t == typeof(AtkComponentNumericInput)) return ComponentType.NumericInput;
            if (t == typeof(AtkComponentList)) return ComponentType.List;
            if (t == typeof(AtkComponentDropDownList)) return ComponentType.DropDownList;
            // if (t == typeof(AtkComponentTab)) return ComponentType.Tab;
            if (t == typeof(AtkComponentTreeList)) return ComponentType.TreeList;
            if (t == typeof(AtkComponentScrollBar)) return ComponentType.ScrollBar;
            if (t == typeof(AtkComponentListItemRenderer)) return ComponentType.ListItemRenderer;
            if (t == typeof(AtkComponentIcon)) return ComponentType.Icon;
            if (t == typeof(AtkComponentIconText)) return ComponentType.IconText;
            if (t == typeof(AtkComponentDragDrop)) return ComponentType.DragDrop;
            if (t == typeof(AtkComponentGuildLeveCard)) return ComponentType.GuildLeveCard;
            if (t == typeof(AtkComponentTextNineGrid)) return ComponentType.TextNineGrid;
            if (t == typeof(AtkComponentJournalCanvas)) return ComponentType.JournalCanvas;
            // if (t == typeof(AtkComponentMultipurpose)) return ComponentType.Multipurpose;
            // if (t == typeof(AtkComponentMap)) return ComponentType.Map;
            // if (t == typeof(AtkComponentPreview)) return ComponentType.Preview;
            if (t == typeof(AtkComponentHoldButton)) return ComponentType.HoldButton;
            if (t == typeof(AtkComponentPortrait)) return ComponentType.Portrait;
            throw new ArgumentOutOfRangeException(nameof(t), t, "Unknown component type");
        }

        private static ComponentType? GetNodeComponentType(AtkComponentNode* node)
        {
            var info = ((AtkUldComponentInfo*)node->Component->UldManager.Objects);
            return info == null ? null : info->ComponentType;
        }

        public static AtkResNode* GetNodeById(AtkComponentBase* node, uint id) =>
            node->UldManager.SearchNodeById(id);

        public static AtkResNode* GetNodeByIdStrict(AtkComponentBase* node, uint id)
        {
            for (var i = 0; i < node->UldManager.NodeListCount; i++)
            {
                var n = node->UldManager.NodeList[i];
                if (n->NodeId == id)
                    return n;
            }
            return null;
        }

        public static void SetVisibility(AtkResNode* node, bool visible)
        {
            if (visible)
                node->NodeFlags |= NodeFlags.Visible;
            else
                node->NodeFlags &= ~NodeFlags.Visible;
        }
    }

    public unsafe void TradeAmount(int amount)
    {
        // Get the Trade window
        var tradeAgentPtr = GameGui.FindAgentInterface("Trade");
        if (tradeAgentPtr.IsNull)
        {
            Log.Error("Trade window not found.");
            isTradeInProgress = false;
            return;
        }
        var tradeAgent = (AgentInterface*)tradeAgentPtr.Address;

        // This is a hacky way of specifying the Gil to send and pressing the Trade button.
        var clickatkReturn = new AtkValue { Bool = false };
        var clickvalues = new AtkValue { Type = AtkValueType.Int, Int = 2 };
        tradeAgent->ReceiveEvent(&clickatkReturn, &clickvalues, 2, 0);

        var atkReturn = new AtkValue { Bool = true };
        var values = new AtkValue { Type = AtkValueType.Int, Int = amount };
        tradeAgent->ReceiveEvent(&atkReturn, &values, 1, 1);

        Schedule(() =>
        {
            var currentTradeAgentPtr = GameGui.FindAgentInterface("Trade");
            if (currentTradeAgentPtr.IsNull)
            {
                CancelTrade("Trade window closed before the gil amount could be entered.");
                return;
            }
            var currentTradeAgent = (AgentInterface*)currentTradeAgentPtr.Address;
            var clickCloseatkReturn = new AtkValue { Bool = true };
            var clickClosevalues = new AtkValue { Type = AtkValueType.Int, Int = -1 };
            currentTradeAgent->ReceiveEvent(&clickCloseatkReturn, &clickClosevalues, 2, 1);

            var finalCloseReturn = new AtkValue { Bool = false };
            var finalCloseValues = new AtkValue { Type = AtkValueType.Int, Int = 4 };
            currentTradeAgent->ReceiveEvent(&finalCloseReturn, &finalCloseValues, 2, 0);

            var inputNumeric = GameGui.GetAddonByName("InputNumeric");
            if (!inputNumeric.IsNull)
                new AddonMaster.InputNumeric(inputNumeric.Address).Cancel();

            WaitForRecipient();
        }, 300);
    }

    private unsafe void WaitForRecipient()
    {
        var tradeWindowPtr = GameGui.GetAddonByName("Trade");
        if (tradeWindowPtr.IsNull)
        {
            CancelTrade("Trade closed before the recipient confirmed.");
            return;
        }

        var tradeWindow = (AtkUnitBase*)tradeWindowPtr.Address;
        var receiverComponent = NodeUtils.GetAsAtkComponent<AtkComponentBase>(tradeWindow->GetNodeById(5));
        var receiverOk = receiverComponent->GetTextNodeById(2);
        if (receiverOk->Alpha_2 <= 200)
        {
            Schedule(WaitForRecipient, 300);
            return;
        }

        var tradeAgentPtr = GameGui.FindAgentInterface("Trade");
        if (tradeAgentPtr.IsNull)
        {
            CancelTrade("Trade agent disappeared before confirmation.");
            return;
        }

        var tradeAgent = (AgentInterface*)tradeAgentPtr.Address;
        var finalCloseReturn = new AtkValue { Bool = false };
        var finalCloseValues = new AtkValue { Type = AtkValueType.Int, Int = 0 };
        tradeAgent->ReceiveEvent(&finalCloseReturn, &finalCloseValues, 2, 0);
        Schedule(WaitForTradeWindowToClose, 300);
    }

    private void WaitForTradeWindowToClose()
    {
        if (!GameGui.GetAddonByName("Trade").IsNull)
        {
            Schedule(WaitForTradeWindowToClose, 300);
            return;
        }

        CompleteCurrentTrade();
    }

    private void CompleteCurrentTrade()
    {
        if (!isTradeInProgress)
            return;

        isTradeInProgress = false;
        if (remainingGil > 0)
        {
            StatusMessage = $"Trade complete. {remainingGil:N0} gil remaining.";
            Schedule(StartNextTrade, 500);
            return;
        }

        StatusMessage = "Gil trading completed successfully.";
        Log.Information(StatusMessage);
    }

    private void Schedule(System.Action action, int delayMilliseconds)
    {
        var token = tradeCancellation.Token;
        _ = Framework.RunOnTick(
            () =>
            {
                if (!token.IsCancellationRequested)
                    action();
            },
            TimeSpan.FromMilliseconds(delayMilliseconds),
            cancellationToken: token);
    }

    public void Dispose()
    {
        tradeCancellation.Cancel();
        tradeCancellation.Dispose();
        ECommonsMain.Dispose();
        ChatGui.ChatMessage -= OnChatMessage;
        Framework.Update -= SelectYes;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUi;
        windowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler("/giltrade");
    }

    private unsafe void OnCommand(string command, string args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            ToggleMainUi();
            return;
        }

        if (!int.TryParse(args.Trim(), out var gilAmount))
        {
            StatusMessage = "Enter a whole-number gil amount.";
            ChatGui.PrintError(StatusMessage);
            mainWindow.IsOpen = true;
            return;
        }

        if (!TryStartTrade(gilAmount))
            ChatGui.PrintError(StatusMessage);
    }

    private void ToggleMainUi() => mainWindow.Toggle();
}
