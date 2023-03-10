using BepInEx;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Permissions;
using UnityEngine;

// Allows access to private members
#pragma warning disable CS0618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618

namespace OshaShelters;

[BepInPlugin("com.dual.osha-shelters", "OSHA Compliant Shelters", "1.0.0")]
sealed class Plugin : BaseUnityPlugin
{
    const int startSleep = 20;

    sealed class PlayerData { public int sleepTime; }

    readonly ConditionalWeakTable<Player, PlayerData> players = new();

    PlayerData Data(Player p) => players.GetValue(p, _ => new());

    int MaxSleepTime(Player p)
    {
        return startSleep + (p.forceSleepCounter > 0
            ? Mathf.Max(260, Mathf.CeilToInt(Options.SleepTime.Value * 80))
            : Mathf.CeilToInt(Options.SleepTime.Value * 40));
    }

    float SleepPercent(Player self)
    {
        return Mathf.Clamp01(1f * (Data(self).sleepTime - startSleep) / (MaxSleepTime(self) - startSleep));
    }

    public void OnEnable()
    {
        On.RainWorld.OnModsInit += RainWorld_OnModsInit;

        On.Player.JollyUpdate += UpdateSleep;
        On.Player.Update += FixForceSleep;
        On.ShelterDoor.Close += UpdateClose;

        On.HUD.FoodMeter.GameUpdate += FoodMeter_GameUpdate; // Fix line sprite jittering when it shouldn't
        On.HUD.FoodMeter.MeterCircle.Update += FoodMeter_Update; // Fix HUD flashing red when not trying to sleep
    }

    private void RainWorld_OnModsInit(On.RainWorld.orig_OnModsInit orig, RainWorld self)
    {
        orig(self);

        MachineConnector.SetRegisteredOI("osha-shelters", new Options());
    }

    private void UpdateSleep(On.Player.orig_JollyUpdate orig, Player self, bool eu)
    {
        orig(self, eu);

        // Not needed anymore
        self.stillInStartShelter = false;

        ref int sleepTime = ref Data(self).sleepTime;

        // Don't allow sleeping
        if (self.Stunned || self.room?.game.session is not StoryGameSession sess || !self.room.abstractRoom.shelter || self.room.shelterDoor == null
            || self.room.shelterDoor.Broken
            || self.room.shelterDoor.closedFac != 0 && self.room.shelterDoor.closeSpeed < 0 // shelter still opening
            || self.FoodInRoom(self.room, eatAndDestroy: false) < 1
            || self.FoodInRoom(self.room, eatAndDestroy: false) < sess.characterStats.maxFood && sess.characterStats.malnourished
            ) {
            sleepTime = 0;
            return;
        }

        Player.InputPackage i = self.input[0];

        bool x = i.x == 0 || self.IsTileSolid(1, i.x, 0) && (!self.IsTileSolid(1, -1, -1) || !self.IsTileSolid(1, 1, -1));
        bool anim = self.bodyMode == Player.BodyModeIndex.Default || self.bodyMode == Player.BodyModeIndex.Crawl || self.bodyMode == Player.BodyModeIndex.ZeroG;

        if (i.y < 0 && x && anim && !i.jmp && !i.thrw && !i.pckp && self.IsTileSolid(1, 0, -1)) {
            sleepTime++;

            self.emoteSleepCounter = 0;
            self.sleepCurlUp = SleepPercent(self);
        }
        else {
            sleepTime = 0;
        }

        // Close doors when ready
        if (sleepTime >= MaxSleepTime(self)) {
            self.room.shelterDoor.Close();
        }
    }

    private void FixForceSleep(On.Player.orig_Update orig, Player self, bool eu)
    {
        orig(self, eu);
        if (self.room?.game.session is not StoryGameSession) {
            return;
        }
        int time = Data(self).sleepTime;
        if (self.forceSleepCounter > 0 && time > 0)
            self.forceSleepCounter = Mathf.CeilToInt(259 * SleepPercent(self)); // 260 softlocks the player, so leave it at 259
        else
            self.forceSleepCounter = 0;
    }

    private void UpdateClose(On.ShelterDoor.orig_Close orig, ShelterDoor self)
    {
        if (!self.room.game.IsStorySession) {
            orig(self);
            return;
        }

        var relevantPlayers = self.room.game.PlayersToProgressOrWin.Select(p => p.realizedObject).Where(p => p != null && p.room == self.room).OfType<Player>();
        if (!Options.SleepTogether.Value) {
            relevantPlayers = relevantPlayers.Take(1);
        }
        if (!relevantPlayers.Any()) {
            orig(self);
            return;
        }
        foreach (Player plr in relevantPlayers) {
            int sleepTime = Data(plr).sleepTime;
            if (sleepTime < MaxSleepTime(plr)) {
                return;
            }
        }

        orig(self);

        // Make sleeping players stay sleeping
        if (self.closeSpeed > 0) {
            foreach (Player plr in relevantPlayers) {
                plr.sleepCounter = -24;
            }
        }
    }

    private void FoodMeter_GameUpdate(On.HUD.FoodMeter.orig_GameUpdate orig, HUD.FoodMeter self)
    {
        Player owner = (Player)self.hud.owner;
        int y = owner.input[0].y;
        owner.input[0].y = 0;
        orig(self);
        owner.input[0].y = y;
    }

    private void FoodMeter_Update(On.HUD.FoodMeter.MeterCircle.orig_Update orig, HUD.FoodMeter.MeterCircle self)
    {
        if (self.meter.hud.owner is Player p) {
            p.stillInStartShelter = Data(p).sleepTime <= 0;
        }
        orig(self);
        if (self.meter.hud.owner is Player p2) {
            p2.stillInStartShelter = false;
        }
    }
}
