using Clientprefs.API;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace Nightvision;

public class Nightvision : BasePlugin
{
    public override string ModuleName => "Nightvision";
    public override string ModuleVersion => $"1.0.1";
    public override string ModuleAuthor => "rc https://github.com/rcnoob/";
    public override string ModuleDescription => "A CS2 nightvision plugin";
    
    private readonly PluginCapability<IClientprefsApi> g_PluginCapability = new("Clientprefs");
    private IClientprefsApi ClientprefsApi;
    private int g_iCookieID = -1, g_iCookieID2 = -1;
    private Dictionary<int, Dictionary<string, string>> playerCookies = new();
    
    private static readonly MemoryFunctionVoid<CCSPlayerPawn, CSPlayerState> StateTransition = new(GameData.GetSignature("StateTransition"));
    private readonly INetworkServerService networkServerService = new();
    private readonly CSPlayerState[] _oldPlayerState = new CSPlayerState[65];

    public override void Load(bool hotReload)
    {
        Logger.LogInformation("[Nightvision] Loading plugin...");
        
        StateTransition.Hook(Hook_StateTransition, HookMode.Post);
        
        RegisterEventHandler<EventPlayerConnectFull>((@event, info) =>
        {
            if (@event.Userid!.IsValid)
            {
                var player = @event.Userid;

                if (player.IsValid && !player.IsBot)
                    Utils.OnPlayerConnect(player);
            }
            return HookResult.Continue;
        });
        
        RegisterEventHandler<EventPlayerDisconnect>((@event, info) =>
        {
            if (@event.Userid!.IsValid)
            {
                var player = @event.Userid;

                if (player.IsValid && !player.IsBot)
                    Utils.OnPlayerDisconnect(player);
            }
            return HookResult.Continue;
        });
        
        RegisterListener<Listeners.CheckTransmit>((CCheckTransmitInfoList infoList) =>
        {
            IEnumerable<CCSPlayerController> players = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller");

            if (!players.Any())
                return;

            foreach ((CCheckTransmitInfo info, CCSPlayerController? player) in infoList)
            {
                if (player == null || player.IsBot || !player.IsValid || player.IsHLTV)
                    continue;

                if (!Globals.connectedPlayers.TryGetValue(player.Slot, out var connected))
                    continue;
                
                foreach (var (owner, pp) in Globals.postProcessVolumes.Where(x => x.Key != player))
                    info.TransmitEntities.Remove(pp);
            }
        });
        
        AddCommand("nv", "Enable/disable nightvision", (player, info) =>
        {
            if (player == null || player.IsBot || player.Team == CsTeam.Spectator) return;
            Globals.playerVars[player.Slot].NightvisionEnabled = !Globals.playerVars[player.Slot].NightvisionEnabled;

            if (Globals.playerVars[player.Slot].NightvisionEnabled)
            {
                Utils.CreatePlayerPP(player);
                ClientprefsApi.SetPlayerCookie(player, g_iCookieID, "true");
            }
            else
            {
                Utils.RemovePlayerPP(player);
                ClientprefsApi.SetPlayerCookie(player, g_iCookieID, "false");
            }
            
            player.PrintToChat($"[Nightvision] {(Globals.playerVars[player.Slot].NightvisionEnabled ? "Enabled" : "Disabled")}");
        });
        
        AddCommand("nvi", "Change nightvision intensity", (player, info) =>
        {
            if (player == null || player.IsBot) return;

            if (!Globals.playerVars[player.Slot].NightvisionEnabled) return;

            string arg = info.ArgByIndex(1);
            if (arg is null || arg == "")
            {
                player.PrintToChat("[Nightvision] Please provide a float value (!nvi 1.3)");
                return;
            }
            
            float nvIntensity = float.Parse(arg);
            if (nvIntensity < 0)
            {
                player.PrintToChat("[Nightvision] Please provide a positive float value (!nvi 1.3)");
                return;
            }
            
            ClientprefsApi.SetPlayerCookie(player, g_iCookieID2, nvIntensity.ToString());
            playerCookies[player.Slot]["nightvision_intensity"] = nvIntensity.ToString();
            Globals.playerVars[player.Slot].NightvisionIntensity = nvIntensity;
            
            Utils.RemovePlayerPP(player);
            Utils.CreatePlayerPP(player);
            
            player.PrintToChat($"[Nightvision] Intensity set to {Globals.playerVars[player.Slot].NightvisionIntensity}");
        });
        
        Logger.LogInformation("[Nightvision] Loaded!");
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        ClientprefsApi = g_PluginCapability.Get();

        if (ClientprefsApi == null) return;
        
        ClientprefsApi.OnDatabaseLoaded += OnClientprefDatabaseReady;
        ClientprefsApi.OnPlayerCookiesCached += OnPlayerCookiesCached;

        if (hotReload && ClientprefsApi != null)
        {
            OnClientprefDatabaseReady();
            foreach (CCSPlayerController player in Utilities.GetPlayers().Where(p => !p.IsBot))
            {
                OnPlayerCookiesCached(player);
            }
        }
    }
    
    public void OnClientprefDatabaseReady()
    {
        if (ClientprefsApi is null) return;

        g_iCookieID = ClientprefsApi.RegPlayerCookie("nightvision_enabled", "Nightvision status");
        g_iCookieID2 = ClientprefsApi.RegPlayerCookie("nightvision_intensity", "Nightvision intensity");
        
        if (g_iCookieID == -1)
        {
            Logger.LogError("[Nightvision] Failed to register player cookie nightvision_enabled!");
            return;
        }
        
        if (g_iCookieID2 == -1)
        {
            Logger.LogError("[Nightvision] Failed to register player cookie nightvision_intensity!");
            return;
        }
    }
    
    public void OnPlayerCookiesCached(CCSPlayerController player)
    {
        if (ClientprefsApi is null) return;

        playerCookies[player.Slot] = new Dictionary<string, string>();

        playerCookies[player.Slot]["nightvision_enabled"] = ClientprefsApi.GetPlayerCookie(player, g_iCookieID);
        playerCookies[player.Slot]["nightvision_intensity"] = ClientprefsApi.GetPlayerCookie(player, g_iCookieID2);
        
        if (playerCookies[player.Slot]["nightvision_enabled"].Equals("true"))
        {
            Globals.playerVars[player.Slot].NightvisionEnabled = true;

            if (playerCookies[player.Slot]["nightvision_intensity"] != null &&
                playerCookies[player.Slot]["nightvision_intensity"] != "")
            {
                float nvIntensity = float.Parse(playerCookies[player.Slot]["nightvision_intensity"]);
                Globals.playerVars[player.Slot].NightvisionIntensity = nvIntensity;
                Utils.CreatePlayerPP(player);
            }
            else
            {
                Utils.CreatePlayerPP(player);
            }
        }
    }
    private HookResult Hook_StateTransition(DynamicHook h)
    {
        var player = h.GetParam<CCSPlayerPawn>(0).OriginalController.Value;
        var state = h.GetParam<CSPlayerState>(1);

        if (player is null) return HookResult.Continue;

        if (state != _oldPlayerState[player.Index])
        {
            if (state == CSPlayerState.STATE_OBSERVER_MODE || _oldPlayerState[player.Index] == CSPlayerState.STATE_OBSERVER_MODE)
            {
                ForceFullUpdate(player);
            }
        }

        _oldPlayerState[player.Index] = state;

        return HookResult.Continue;
    }
    private void ForceFullUpdate(CCSPlayerController? player)
    {
        if (player is null || !player.IsValid) return;

        var networkGameServer = networkServerService.GetIGameServer();
        networkGameServer.GetClientBySlot(player.Slot)?.ForceFullUpdate();

        player.PlayerPawn.Value?.Teleport(null, player.PlayerPawn.Value.EyeAngles, null);
    }
}