using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using Serilog.Core;

namespace Nightvision;

public class Utils
{
    public static void OnPlayerConnect(CCSPlayerController? player)
    {
        if (player == null || player.IsBot)
            return;

        Globals.playerVars[player.Slot] = new PlayerVars();
        Globals.connectedPlayers[player.Slot] = new CCSPlayerController(player.Handle);
    }

    public static void OnPlayerDisconnect(CCSPlayerController? player)
    {
        if (player == null || player.IsBot)
            return;

        RemovePlayerPP(player);
        Globals.playerVars[player.Slot] = new PlayerVars();
        Globals.playerVars.Remove(player.Slot);
        Globals.connectedPlayers.Remove(player.Slot);
    }

    public static void CreatePlayerPP(CCSPlayerController? player)
    {
        var pp = Utilities.CreateEntityByName<CPostProcessingVolume>("post_processing_volume")!;
        pp.Master = true;

        pp.FadeDuration = 0f;
        pp.ExposureControl = true;
        pp.MaxExposure = Globals.playerVars[player.Slot].NightvisionIntensity;
        pp.MinExposure = Globals.playerVars[player.Slot].NightvisionIntensity;
        
        pp.DispatchSpawn();
        
        Globals.postProcessVolumes.Add(player, pp);
    }

    public static void RemovePlayerPP(CCSPlayerController? player)
    {
        if (Globals.postProcessVolumes.TryGetValue(player, out var pp))
        {
            pp.AcceptInput("Kill");
            pp.Remove();
            Globals.postProcessVolumes.Remove(player);
        }
    }
}