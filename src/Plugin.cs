using BepInEx;
using HUD;
using RWCustom;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Permissions;
using System.Text;
using UnityEngine;

// Allows access to private members
#pragma warning disable CS0618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618

namespace OshaShelters;

[BepInPlugin("com.dual.osha-shelters", "OSHA Compliant Shelters", "1.0.7")]
sealed class Plugin : BaseUnityPlugin
{
    const int startSleep = 20;

    sealed class PlayerData { public int sleepTime; public bool forceSleep; }
    sealed class RegionData { public readonly Dictionary<int, SavedPos> entities = new(); };
    sealed class SavedPos
    {
        public Vector2[] chunks;
        public string roomName;
    }

    static readonly ConditionalWeakTable<Player, PlayerData> players = new();
    static readonly ConditionalWeakTable<RegionState, RegionData> regions = new();

    static PlayerData Data(Player p) => players.GetValue(p, _ => new());

    static bool SlowSleep(Player self)
    {
        if (self.room?.game.session is not StoryGameSession sess) return false;

        bool willStarve = !sess.saveState.malnourished && self.FoodInRoom(self.room, false) > 0 && self.FoodInRoom(self.room, false) < sess.characterStats.foodToHibernate;
        bool pupDanger = ModManager.MSC && self.room.game.cameras[0].hud.foodMeter.pupBars.Any(PupWarning);

        return willStarve || pupDanger;

        static bool PupWarning(FoodMeter m)
        {
            if (m.PupHasDied) {
                return false;
            }
            return m.abstractPup.Room != m.abstractPup.Room || m.PupInDanger || m.CurrentPupFood < m.survivalLimit;
        }
    }
    static int MaxSleepTime(Player p)
    {
        return startSleep + (SlowSleep(p)
            ? Mathf.Max(260, Mathf.CeilToInt(Options.SleepTime.Value * 80))
            : Mathf.CeilToInt(Options.SleepTime.Value * 40));
    }

    static float MinSleepPercent(RainWorldGame game)
    {
        return game.PlayersToProgressOrWin.Min(c => c.realizedCreature is Player p ? SleepPercent(p) : 0);
    }
    static float SleepPercent(Player self)
    {
        return Mathf.Clamp01(1f * (Data(self).sleepTime - startSleep) / (MaxSleepTime(self) - startSleep));
    }
    static bool SafePos(ShelterDoor door, IntVector2 tile)
    {
        return ShelterDoor.CoordInsideShelterRange(tile, door.isAncient) && (door.isAncient || !door.closeTiles.Contains(tile));
    }

    public void OnEnable()
    {
        On.RainWorld.OnModsInit += RainWorld_OnModsInit;

        On.Player.ctor += Player_ctor;
        On.Player.JollyUpdate += UpdateSleep;
        On.Player.Update += FixForceSleep;
        On.ShelterDoor.Close += FixClose;

        On.HUD.FoodMeter.GameUpdate += FoodMeter_GameUpdate; // Fix line sprite jittering when it shouldn't
        On.HUD.FoodMeter.MeterCircle.Update += FoodMeter_Update; // Fix HUD flashing red when not trying to sleep

        On.ShelterDoor.DestroyExcessiveObjects += SaveExtraObjects;
        On.ShelterDoor.Update += EjectStuck;
        On.ShortcutHandler.SpitOutCreature += ForbidEntry;
        On.ShortcutHandler.VesselAllowedInRoom += ForbidEntryAgain;

        // Have to store positions manually (instead of relying on vanilla) because vanilla doesn't save per-chunk pos
        On.RegionState.ctor += LoadPositions;
        On.RegionState.SaveToString += SavePositions;
        On.RegionState.AdaptRegionStateToWorld += UpdateRegionState;
        On.AbstractPhysicalObject.RealizeInRoom += AbstractPhysicalObject_RealizeInRoom;
    }

    private void RainWorld_OnModsInit(On.RainWorld.orig_OnModsInit orig, RainWorld self)
    {
        orig(self);

        MachineConnector.SetRegisteredOI("osha-shelters", new Options());
    }

    private void Player_ctor(On.Player.orig_ctor orig, Player self, AbstractCreature abstractCreature, World world)
    {
        orig(self, abstractCreature, world);

        // Fix sleep counter with dead/small creatures
        AbstractRoom room = world.GetAbstractRoom(abstractCreature.pos.room);
        if (self.sleepCounter == 0 && room.shelter && room.creatures.All(c => c.state.dead || c.creatureTemplate.smallCreature 
        || c.creatureTemplate.type == CreatureTemplate.Type.Slugcat || c.creatureTemplate.type == MoreSlugcats.MoreSlugcatsEnums.CreatureTemplateType.SlugNPC)) {
            self.sleepCounter = 100;
        }
    }

    private void UpdateSleep(On.Player.orig_JollyUpdate orig, Player self, bool eu)
    {
        orig(self, eu);

        ref bool forceSleep = ref Data(self).forceSleep;
        if (self.AI != null && self.grabbedBy.Count > 0 && self.grabbedBy.Any(c => c.grabber is Player p && Data(p).forceSleep)) {
            forceSleep = true;
        }
        if (forceSleep) {
            if (self.Stunned) {
                forceSleep = false;
                self.sleepCounter = 0;
                self.sleepCurlUp = 0;
            }
            else {
                self.sleepCounter = -24;
                self.sleepCurlUp = 1;
            }
        }

        // Not needed anymore
        self.stillInStartShelter = false;

        ref int sleepTime = ref Data(self).sleepTime;

        // Don't allow sleeping
        if (self.Stunned || self.room?.game.session is not StoryGameSession sess || !self.room.abstractRoom.shelter || self.room.shelterDoor == null
            || self.room.shelterDoor.Broken
            || self.room.shelterDoor.closedFac != 0 && self.room.shelterDoor.closeSpeed < 0 // shelter still opening
            || !SafePos(self.room.shelterDoor, self.room.GetTilePosition(self.bodyChunks[0].pos))
            || !SafePos(self.room.shelterDoor, self.room.GetTilePosition(self.bodyChunks[1].pos))
            || self.FoodInRoom(self.room, eatAndDestroy: false) < 1
            || self.FoodInRoom(self.room, eatAndDestroy: false) < sess.characterStats.maxFood && sess.characterStats.malnourished
            ) {
            sleepTime = 0;
            return;
        }

        Player.InputPackage i = self.input[0];

        // Allow snuggling against walls
        bool x = i.x == 0 || self.IsTileSolid(0, i.x, 0) && (!self.IsTileSolid(0, -1, 0) || !self.IsTileSolid(0, 1, 0));
        bool anim = self.bodyMode == Player.BodyModeIndex.Default
            || self.bodyMode == Player.BodyModeIndex.CorridorClimb
            || self.bodyMode == Player.BodyModeIndex.WallClimb
            || self.bodyMode == Player.BodyModeIndex.Crawl
            || self.bodyMode == Player.BodyModeIndex.ZeroG
            || self.bodyMode == Player.BodyModeIndex.ClimbingOnBeam && self.room.gravity < 0.1f;
        bool floor = self.bodyMode == Player.BodyModeIndex.Default || self.IsTileSolid(0, 0, -1) || self.IsTileSolid(1, 0, -1);
            
        if (i.y < 0 && x && anim && !i.jmp && !i.thrw && !i.pckp && floor) {
            sleepTime++;

            self.emoteSleepCounter = 0;
            self.sleepCurlUp = SleepPercent(self);
        }
        else {
            sleepTime = 0;
        }

        // Close doors when ready (if-check is just an optimization)
        if (self.AI == null && sleepTime >= MaxSleepTime(self)) {
            self.room.shelterDoor.Close();
        }
    }

    private void FixForceSleep(On.Player.orig_Update orig, Player self, bool eu)
    {
        orig(self, eu);
        if (self.room?.game.session is StoryGameSession) {
            int time = Data(self).sleepTime;
            if (time > 0 && SlowSleep(self))
                self.forceSleepCounter = Mathf.CeilToInt(259 * MinSleepPercent(self.room.game)); // 260 softlocks the player, so leave it at 259
            else
                self.forceSleepCounter = 0;
        }
    }
    private void FixClose(On.ShelterDoor.orig_Close orig, ShelterDoor self)
    {
        if (!self.room.game.IsStorySession) {
            orig(self);
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
            if (self.closeSpeed > 0)
                Data(plr).forceSleep = true;
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

    private void SaveExtraObjects(On.ShelterDoor.orig_DestroyExcessiveObjects orig, ShelterDoor self)
    {
        if (!Options.SaveExcess.Value) {
            orig(self);
        }
    }

    private void EjectStuck(On.ShelterDoor.orig_Update orig, ShelterDoor self, bool eu)
    {
        orig(self, eu);

        if (self.isAncient || self.Closed <= 0 || self.room.shortcuts == null) {
            return;
        }

        // When the door closes...
        Vector2 depositPos = self.room.MiddleOfTile(self.room.LocalCoordinateOfNode(0)) + self.dir * 110;

        foreach (PhysicalObject obj in self.room.physicalObjects.SelectMany(p => p).ToList()) {
            // Shove creatures still in the entrance back through
            if (obj is Creature crit) {
                bool cont = false;
                foreach (BodyChunk chunk in crit.bodyChunks) {
                    Room.Tile tile = self.room.GetTile(chunk.pos);
                    IntVector2 tilePos = new(tile.X, tile.Y);
                    if (tile.Terrain == Room.Tile.TerrainType.ShortcutEntrance && self.room.WhichRoomDoesThisExitLeadTo(self.room.shortcutData(tilePos).DestTile) != null) {
                        Logger.LogDebug($"Shoving {crit.Template.type} ({crit.abstractPhysicalObject.ID.number}) out of shelter");
                        crit.SuckedIntoShortCut(tilePos, false);
                        cont = true;
                        break;
                    }
                }
                if (cont) continue;
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

    private void LoadPositions(On.RegionState.orig_ctor orig, RegionState self, SaveState saveState, World world)
    {
        orig(self, saveState, world);

        RegionData data = regions.GetValue(self, _ => new());

        for (int s = self.unrecognizedSaveStrings.Count - 1; s >= 0; s--) {
            string[] split = self.unrecognizedSaveStrings[s].Split(new string[] { "<rgB>" }, 0);

            if (split[0] != "ENTITYPOSITIONS") {
                continue;
            }

            self.unrecognizedSaveStrings.RemoveAt(s);

            string[] entries = split[1].Split(new char[] { ')' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string entry in entries) {
                string[] entrySplit = entry.Split('(');
                string[] roomNameSplit = entrySplit[0].Split(':');

                if (roomNameSplit.Length < 2) {
                    Logger.LogWarning($"Updating from 1.0.4, entity {entrySplit[0]} position lost");
                    continue;
                }

                if (!int.TryParse(roomNameSplit[0], out int id)) {
                    Logger.LogWarning($"ID:roomname format was outdated or incorrect: {entrySplit[0]}");
                    continue;
                }

                string[] positions = entrySplit[1].Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

                data.entities[id] = new() {
                    chunks = new Vector2[positions.Length],
                    roomName = roomNameSplit[1]
                };

                for (int i = 0; i < positions.Length; i++) {
                    string[] xy = positions[i].Split(',');
                    
                    if (int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y)) {
                        data.entities[id].chunks[i] = new(x * 0.0001f, y * 0.0001f);
                    }
                    else {
                        Logger.LogWarning($"X and Y coordinate saved incorrectly: {positions[i]}");
                    }
                }
            }
        }
    }

    private string SavePositions(On.RegionState.orig_SaveToString orig, RegionState self)
    {
        StringBuilder save = new("ENTITYPOSITIONS<rgB>");
        foreach (var ent in regions.GetValue(self, _ => new()).entities) {
            save.Append(ent.Key);
            save.Append(":");
            save.Append(ent.Value.roomName);
            save.Append("(");
            foreach (var pos in ent.Value.chunks) {
                // Save positions as a fixed-point integer (10000× scale) to hopefully prevent floating-point/string-culture jank.
                save.Append((int)(pos.x * 10000f));
                save.Append(",");
                save.Append((int)(pos.y * 10000f));
                save.Append(";");
            }
            save.Append(")");
        }
        return $"{orig(self)}{save}<rgA>";
    }

    private void UpdateRegionState(On.RegionState.orig_AdaptRegionStateToWorld orig, RegionState self, int playerShelter, int activeGate)
    {
        orig(self, playerShelter, activeGate);

        RegionData data = regions.GetValue(self, _ => new());

        data.entities.Clear();

        for (int k = 0; k < self.world.NumberOfRooms; k++) {
            AbstractRoom room = self.world.GetAbstractRoom(self.world.firstRoomIndex + k);
            if (!room.shelter) {
                continue;
            }
            foreach (var apo in room.entities.OfType<AbstractPhysicalObject>()) {
                if (apo.realizedObject != null && apo.realizedObject.bodyChunks.Length > 0) {
                    data.entities[apo.ID.number] = new SavedPos() {
                        roomName = room.name,
                        chunks = apo.realizedObject.bodyChunks.Select(b => b.pos).ToArray()
                    };
                }
                else if (!data.entities.ContainsKey(apo.ID.number)) {
                    data.entities[apo.ID.number] = new SavedPos() {
                        roomName = room.name,
                        chunks = new Vector2[] { new(apo.pos.x * 20 + 10, apo.pos.y * 20 + 10) }
                    };
                }
            }
        }
    }

    private void AbstractPhysicalObject_RealizeInRoom(On.AbstractPhysicalObject.orig_RealizeInRoom orig, AbstractPhysicalObject self)
    {
        // Use a UAD to set positions because, for whatever reason, items are hard-set to the player's head chunk after a few ticks.
        if (self.Room.shelter && regions.GetOrCreateValue(self.world.regionState).entities.TryGetValue(self.ID.number, out var pos)) {
            if (pos.roomName == self.Room.name)
                self.Room.realizedRoom?.AddObject(new PositionSetter(self, pos.chunks));
            else
                Logger.LogWarning($"Saved object {(self is AbstractCreature c ? c.creatureTemplate.type.value : self.type.value)} ({self.ID.number}) moved from {pos.roomName} to {self.Room.name}");
        }
        orig(self);
    }
}
