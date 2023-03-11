using BepInEx;
using RWCustom;
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

    float MinSleepPercent(RainWorldGame game)
    {
        return game.PlayersToProgressOrWin.Min(c => c.realizedCreature is Player p ? SleepPercent(p) : 0);
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

        On.ShelterDoor.Update += EjectStuck;
        On.ShortcutHandler.SpitOutCreature += ForbidEntry;
        On.ShortcutHandler.VesselAllowedInRoom += ForbidEntryAgain;
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
            || !ShelterDoor.CoordInsideShelterRange(self.abstractCreature.pos.Tile, self.room.shelterDoor.isAncient)
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

        // Close doors when ready (if-check is just an optimization)
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
            self.forceSleepCounter = Mathf.CeilToInt(259 * MinSleepPercent(self.room.game)); // 260 softlocks the player, so leave it at 259
        else
            self.forceSleepCounter = 0;
    }

    private void UpdateClose(On.ShelterDoor.orig_Close orig, ShelterDoor self)
    {
        if (!self.room.game.IsStorySession) {
            orig(self);
            return;
        }

        if (self.room.game.PlayersToProgressOrWin.Any(p => !ShelterDoor.CoordInsideShelterRange(p.pos.Tile, self.isAncient))) {
            return;
        }

        var relevantPlayers = self.room.game.PlayersToProgressOrWin.Select(p => p.realizedObject).OfType<Player>().Where(p => p != null && p.room == self.room && !p.dead);

        bool any = false;
        foreach (Player plr in relevantPlayers) {
            plr.readyForWin = true;
            plr.ReadyForWinJolly = true;

            int sleepTime = Data(plr).sleepTime;
            if (sleepTime < MaxSleepTime(plr)) {
                if (Options.SleepTogether.Value) return;
                else continue;
            }

            any = true;
        }
        if (!any) {
            return;
        }

        orig(self);

        foreach (Player plr in relevantPlayers) {
            plr.readyForWin = false;
            plr.ReadyForWinJolly = false;

            if (self.closeSpeed > 0) {
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

    private void EjectStuck(On.ShelterDoor.orig_Update orig, ShelterDoor self, bool eu)
    {
        orig(self, eu);

        if (self.isAncient || self.Closed <= 0 || self.room.shortcuts == null) {
            return;
        }

        // When the door closes...
        IntVector2 shortcutPos = self.room.LocalCoordinateOfNode(0).Tile;
        Vector2 depositPos = self.room.MiddleOfTile(self.room.LocalCoordinateOfNode(0)) + self.dir * 110;

        foreach (PhysicalObject obj in self.room.physicalObjects.SelectMany(p => p).ToList()) {
            // Shove creatures still in the entrance back through
            if (obj is Creature crit && crit.bodyChunks.Any(c => self.room.GetTilePosition(c.pos) == shortcutPos)) {
                crit.SuckedIntoShortCut(shortcutPos, false);
                continue;
            }
            // Shove everything else INTO the shelter
            foreach (var c in obj.bodyChunks) {
                if (self.room.GetTile(c.pos).Solid) {
                    c.pos = depositPos;
                    c.vel = self.dir * 5;
                }
            }
        }
    }

    private void ForbidEntry(On.ShortcutHandler.orig_SpitOutCreature orig, ShortcutHandler self, ShortcutHandler.ShortCutVessel vessel)
    {
        orig(self, vessel);

        if (vessel.room.realizedRoom.shelterDoor != null && vessel.room.realizedRoom.shelterDoor.Closed > 0) {
            self.SuckInCreature(vessel.creature, vessel.creature.room, vessel.creature.room.shortcutData(vessel.pos));
        }
    }

    private bool ForbidEntryAgain(On.ShortcutHandler.orig_VesselAllowedInRoom orig, ShortcutHandler self, ShortcutHandler.Vessel vessel)
    {
        return orig(self, vessel) && !(vessel.room.realizedRoom?.shelterDoor != null && vessel.room.realizedRoom.shelterDoor.Closed > 0);
    }
}
