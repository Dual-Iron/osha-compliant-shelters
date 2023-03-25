using BepInEx;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using RegionKit.Modules.ShelterBehaviors;
using System;
using System.IO;

namespace OshaShelters;

sealed partial class Plugin : BaseUnityPlugin
{
    void EnableRegionKit()
    {
        On.AssetManager.ResolveFilePath += FixCctorCrash;
        try {
            HookRegionKit();
        }
        catch (FileNotFoundException) {
            Logger.LogDebug("RegionKit assembly not loaded, no hook needed.");
        }
        catch (Exception e) {
            Logger.LogError("Couldn't hook shelter behaviors: " + e);
        }
        On.AssetManager.ResolveFilePath -= FixCctorCrash;
    }

    string FixCctorCrash(On.AssetManager.orig_ResolveFilePath orig, string path)
    {
        return path;
    }

    void HookRegionKit()
    {
        new ILHook(typeof(ShelterBehaviorManager).GetMethod("Update"), UpdateHook);
    }

    void UpdateHook(ILContext context)
    {
        ILCursor cursor = new(context);

        cursor.GotoNext(MoveType.After, i => i.MatchCall<UpdatableAndDeletable>("Update"));
        cursor.Emit(OpCodes.Ret);
    }
}
