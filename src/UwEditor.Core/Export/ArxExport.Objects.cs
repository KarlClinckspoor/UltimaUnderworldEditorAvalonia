using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;
using UltimateEditor.Uw1;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;

namespace UwEditor.Core.Export
{
    public static partial class ArxExport
    {
        /// <summary>include/critter.h GOAL_* as Uw1Schemas.MobileObject names them.</summary>
        static readonly string[] GoalNames = { "stand", "go home", "wander", "follow", "guard", "attack", "flee", "stand", "mill about", "cornered", "talk to player", "flutter", "hover" };
        /// <summary>include/object.h OBJ_ATTITUDE.</summary>
        static readonly string[] AttitudeNames = { "hostile", "upset", "mellow", "friendly" };

        /// <summary>Stage 2: the objects, then the readings that refer back to them by index.</summary>
        static void AddObjects(Ctx c, LevelModel m, JObj doc)
        {
            doc.Add("objects", Objects(c, m));
            doc.Add("doors", Doors(c, m));
            var chains = Triggers.Chains(m);
            doc.Add("links", Links(c, m, chains));
            doc.Add("triggers", TriggerList(c, m, chains));
            doc.Add("trapsWithoutTrigger", Triggers.Unreached(m, chains).Select(t => (object)new JLine(Step(c, m, t, t.TileX, t.TileY))).ToList());
            doc.Add("tileChanges", TileChanges(c, m));
            doc.Add("playerStart", PlayerStart(c, m));
            doc.Add("lights", Lights(c, m));
            doc.Add("standable", Standable(c, m));
        }

        static int Flags(ObjectInfo o) { return ((o.Record[0] | o.Record[1] << 8) >> 9) & 15; }
        static bool Bit(ObjectInfo o, int bit) { return (((o.Record[0] | o.Record[1] << 8) >> bit) & 1) != 0; }

        /// <summary>Every object not on a free list (LevelModel.ReadObjects), by index, with the containment LevelModel resolves.</summary>
        static List<object> Objects(Ctx c, LevelModel m)
        {
            var s = c.S;
            var list = new List<object>();
            foreach (var o in m.Objects)
            {
                if (o.Free) continue;
                bool onTile = o.OnTile;
                var e = new JObj()
                    .Add("index", o.Index).Add("mobile", o.IsMobile).Add("item", o.Item).Add("name", o.Name)
                    .Add("category", ItemCategories.Of(o.Item, GameKind.UW1).ToString())
                    .Add("tileX", o.TileX).Add("tileY", o.TileY).Add("fineX", o.FineX).Add("fineY", o.FineY).Add("z", o.Z).Add("heading", o.Heading)
                    .Add("worldX", onTile ? (object)o.WorldX : null).Add("worldY", onTile ? (object)o.WorldY : null).Add("worldZ", onTile ? (object)o.WorldZ : null)
                    .Add("quality", o.Quality).Add("owner", o.Owner).Add("link", o.Link).Add("next", o.Next).Add("isQuantity", o.IsQuantity)
                    .Add("invisible", o.Invisible).Add("enchanted", o.Enchanted).Add("flags", Flags(o)).Add("tenacious", Bit(o, 13))
                    .Add("parent", o.Parent).Add("children", Ints(o.Children)).Add("onTile", onTile)
                    .Add("heightIsData", Triggers.HeightIsData(o.Item)).Add("finePositionIsData", Triggers.FinePositionIsData(o.Item, GameKind.UW1));
                string text = ObjectTexts.TextOf(s, o);
                if (text != null) e.Add("text", text);
                if (o.IsMobile)
                {
                    var f = new JObj();
                    foreach (var fd in Uw1Schemas.MobileObject.Fields)
                        if (fd.Offset >= 8) f.Add(ArxJson.Key(fd.Name), fd.Get(o.Record));
                    e.Add("mobileFields", f);
                    if (o.IsCritter)
                    {
                        int goal = Uw1Schemas.MobileObject["Goal"].Get(o.Record), att = Uw1Schemas.MobileObject["Attitude"].Get(o.Record);
                        e.Add("goalName", goal < GoalNames.Length ? GoalNames[goal] : null);
                        e.Add("attitudeName", AttitudeNames[att & 3]);
                        e.Add("npcName", ObjectTexts.NpcName(s, o));
                    }
                }
                e.Add("record", Hex(o.Record));
                list.Add(new JLine(e));
            }
            return list;
        }

        /// <summary>
        /// The doors (0x140-0x14F) as the 3D view draws them (ObjectModels.Build, 3d/DRAWOBJ.C do_door):
        /// at the object's own place in its tile, turned by its heading, the frame in the tile's wall
        /// texture, the leaf in the level's door texture item &amp; 7 (a portcullis is a plain-coloured
        /// model, the secret door the tile's wall texture). 0x148-0x14F are the open doors. The lock and
        /// how it opens: LevelEditor.LockOf, Randomizer/ZoneDoors.Requirement.
        /// </summary>
        static List<object> Doors(Ctx c, LevelModel m)
        {
            var list = new List<object>();
            foreach (var o in m.Objects)
            {
                if (o.Free || o.Item < 0x140 || o.Item > 0x14F) continue;
                int door = o.Item & 7;
                bool portcullis = door == 6, secret = door == 7;
                var tile = o.TileX >= 0 ? m.Tiles[o.TileX, o.TileY] : null;
                int wallTex = tile != null ? m.WallTextureNumber(tile.WallIndex) : -1;
                object tex = portcullis ? null : secret
                    ? (wallTex >= 0 ? new JObj().Add("set", "walls").Add("number", wallTex) : null)
                    : (object)new JObj().Add("set", "doors").Add("number", m.DoorMap[door]);
                var lk = o.Children.Select(i => m.Objects[i]).FirstOrDefault(x => !x.Free && x.Item == 0x10F);
                string notes;
                string req = ZoneDoors.Requirement(c.S, m, o, out notes);
                string plane = o.Heading % 2 == 1 ? "diagonal" : o.Heading % 4 == 0 ? "east-west" : "north-south";
                list.Add(new JLine(new JObj()
                    .Add("index", o.Index).Add("item", o.Item).Add("name", o.Name)
                    .Add("kind", portcullis ? "portcullis" : secret ? "secret door" : "door")
                    .Add("tileX", o.TileX).Add("tileY", o.TileY).Add("fineX", o.FineX).Add("fineY", o.FineY)
                    .Add("worldX", o.OnTile ? (object)o.WorldX : null).Add("worldY", o.OnTile ? (object)o.WorldY : null).Add("worldZ", o.OnTile ? (object)o.WorldZ : null)
                    .Add("heading", o.Heading).Add("headingDegrees", o.Heading * 45).Add("plane", plane)
                    .Add("open", o.Item >= 0x148).Add("swing", Flags(o) & 7)
                    .Add("texture", tex)
                    .Add("frameTexture", wallTex >= 0 ? new JObj().Add("set", "walls").Add("number", wallTex) : null)
                    .Add("lock", lk == null ? null : (object)lk.Index)
                    .Add("locked", lk != null && Bit(lk, 9))
                    .Add("keyNumber", lk != null && lk.IsQuantity ? (object)(lk.Link & 0x1FF) : null)
                    .Add("pickDifficulty", lk == null ? null : (object)lk.Z)
                    .Add("relocks", lk != null && Bit(lk, 10))
                    .Add("spiked", DoorSpikes.Holds(o))
                    .Add("opensWith", req).Add("notes", notes)));
            }
            return list;
        }

        /// <summary>
        /// The ways out of the level and across it: UW1 has no stair object, a stairway is a move
        /// trigger whose chain reaches a teleport trap (0x181) whose Z is another level (Randomizer/
        /// NavRegions.cs builds its "stairs" edges the same way). Each teleport reached from a trigger
        /// (Triggers.Chain), each teleport no trigger reaches, and the end-of-game hack trap (0x183
        /// quality 0x3F). The check variable traps before a teleport in its chain are listed: the
        /// teleport may need them to pass.
        /// </summary>
        static List<object> Links(Ctx c, LevelModel m, List<TrapChain> chains)
        {
            var list = new List<object>();
            var reached = new HashSet<int>();
            foreach (var ch in chains)
            {
                var t = ch.Trigger;
                var checks = new List<int>();
                foreach (var st in ch.Steps)
                {
                    if (st.Item == 0x18E) checks.Add(st.Index);
                    bool tele = Triggers.IsTeleport(st.Item), end = st.Item == 0x183 && st.Quality == 0x3F;
                    if (!tele && !end) continue;
                    reached.Add(st.Index);
                    int to = st.Z == 0 ? m.Level : st.Z;
                    list.Add(new JLine(new JObj()
                        .Add("kind", end ? "end of game" : to != m.Level ? "level" : "teleport")
                        .Add("trigger", t.Index).Add("triggerName", t.Name).Add("tileX", t.TileX).Add("tileY", t.TileY)
                        .Add("holder", t.Parent).Add("playerSetsOff", Bit(t, 11))
                        .Add("trap", st.Index)
                        .Add("toLevel", end ? null : (object)to).Add("toX", end ? null : (object)st.Quality).Add("toY", end ? null : (object)st.Owner)
                        .Add("checks", Ints(checks))));
                }
            }
            foreach (var o in m.Objects)
            {
                if (o.Free || o.TileX < 0 || reached.Contains(o.Index)) continue;
                bool tele = Triggers.IsTeleport(o.Item), end = o.Item == 0x183 && o.Quality == 0x3F;
                if (!tele && !end) continue;
                int to = o.Z == 0 ? m.Level : o.Z;
                list.Add(new JLine(new JObj()
                    .Add("kind", end ? "end of game" : to != m.Level ? "level" : "teleport")
                    .Add("trigger", null).Add("triggerName", null).Add("tileX", o.TileX).Add("tileY", o.TileY)
                    .Add("holder", o.Parent).Add("playerSetsOff", null)
                    .Add("trap", o.Index)
                    .Add("toLevel", end ? null : (object)to).Add("toX", end ? null : (object)o.Quality).Add("toY", end ? null : (object)o.Owner)
                    .Add("checks", new List<object>())));
            }
            return list;
        }

        /// <summary>Each trigger (Triggers.TriggersOf) with its chain (Triggers.Chain), the whole chain running at the trigger's target square.</summary>
        static List<object> TriggerList(Ctx c, LevelModel m, List<TrapChain> chains)
        {
            var modes = ObjectTables.Table("Triggers");
            var list = new List<object>();
            foreach (var ch in chains)
            {
                var t = ch.Trigger;
                int kind = t.Item - Uw1Traps.FirstTrigger;
                int mode = kind >= 0 && kind < modes.Count ? c.Tables.Get(modes, kind, modes.Schema["Mode"]) : -1;
                list.Add(new JObj()
                    .Add("index", t.Index).Add("item", t.Item).Add("name", t.Name)
                    .Add("type", Uw1Traps.IsTrigger(t.Item) ? Uw1Traps.TriggerNames[kind] : null)
                    .Add("tileX", t.TileX).Add("tileY", t.TileY).Add("holder", t.Parent)
                    .Add("targetX", ch.TargetX).Add("targetY", ch.TargetY)
                    .Add("playerSetsOff", Bit(t, 11)).Add("creaturesSetOff", t.Enchanted).Add("reusable", Bit(t, 10))
                    .Add("mode", mode >= 0 ? (object)mode : null)
                    .Add("chain", ch.Steps.Select(st => (object)new JLine(Step(c, m, st, ch.TargetX, ch.TargetY))).ToList()));
            }
            return list;
        }

        /// <summary>
        /// One trap or trigger of a chain: its kind by name, a summary (Triggers.Short), its
        /// parameters by meaning (Editing/TypedFields.cs, the properties panel's typed editors) and,
        /// where the editor knows them, what it acts on, resolved: the tiles and textures a change
        /// terrain trap sets, the door a door trap works, the object a create or delete trap copies or deletes.
        /// </summary>
        static JObj Step(Ctx c, LevelModel m, ObjectInfo st, int runX, int runY)
        {
            var s = c.S;
            string type = Uw1Traps.IsTrap(st.Item) ? Uw1Traps.TrapNames[st.Item - Uw1Traps.FirstTrap]
                : Uw1Traps.IsTrigger(st.Item) ? Uw1Traps.TriggerNames[st.Item - Uw1Traps.FirstTrigger] : null;
            var pars = new List<object>();
            foreach (var tf in TypedFields.For(GameKind.UW1, st))
            {
                int v = tf.Field.Get(st.Record);
                var p = new JObj().Add("name", tf.Field.Name).Add("key", ArxJson.Key(tf.Field.Name)).Add("value", v);
                var choice = tf.Choices?.FirstOrDefault(x => x.Value == v);
                if (choice != null) p.Add("meaning", choice.Label.Substring(choice.Label.IndexOf(' ') + 1));
                pars.Add(p);
            }
            var e = new JObj()
                .Add("index", st.Index).Add("item", st.Item).Add("name", st.Name).Add("type", type)
                .Add("tileX", st.TileX).Add("tileY", st.TileY)
                .Add("runsAtX", runX).Add("runsAtY", runY)
                .Add("summary", Triggers.Short(s, st))
                .Add("params", pars);
            switch (st.Item)
            {
                case TerrainPreview.ChangeTerrainTrap:
                {
                    int tt, h, f, w;
                    TerrainPreview.Values(s, st, out tt, out h, out f, out w);
                    e.Add("area", new JObj().Add("x", runX).Add("y", runY).Add("w", st.FineX + 1).Add("h", st.FineY + 1));
                    e.Add("sets", Sets(m, tt, h, f, w));
                    break;
                }
                case 0x181:
                    e.Add("destination", new JObj().Add("level", st.Z == 0 ? m.Level : st.Z).Add("x", st.Quality).Add("y", st.Owner));
                    break;
                case 0x183:
                    e.Add("special", Uw1Traps.HackTrapName(st.Quality));
                    break;
                case 0x187:
                case 0x18B:
                {
                    var target = Triggers.LinksTo(m, st);
                    e.Add("object", target == null || st.IsQuantity ? null : (object)target.Index);
                    e.Add("objectName", target == null || st.IsQuantity ? null : target.Name);
                    break;
                }
                case 0x188:
                {
                    var d = runX >= 0 && runY >= 0 && runX < 64 && runY < 64 ? m.TileObjects(runX, runY).FirstOrDefault(x => x.Item >= 0x140 && x.Item <= 0x14F) : null;
                    e.Add("door", d == null ? null : (object)d.Index);
                    break;
                }
                case 0x190:
                    e.Add("text", ObjectTexts.TextOf(s, st));
                    break;
            }
            return e;
        }

        /// <summary>What a change terrain trap sets (TerrainPreview.Values), null for what it keeps, with the textures the indexes resolve to.</summary>
        static JObj Sets(LevelModel m, int type, int height, int floor, int wall)
        {
            int ft = floor >= 0 ? m.FloorTextureNumber(floor) : -1, wt = wall >= 0 ? m.WallTextureNumber(wall) : -1;
            return new JObj()
                .Add("type", type >= 0 ? ((TileType)type).ToString() : null)
                .Add("height", height >= 0 ? (object)height : null)
                .Add("floorIndex", floor >= 0 ? (object)floor : null).Add("floorTexture", ft >= 0 ? (object)ft : null)
                .Add("wallIndex", wall >= 0 ? (object)wall : null).Add("wallTexture", wt >= 0 ? (object)wt : null);
        }

        /// <summary>
        /// The tiles that can change in play (question 7): every change terrain trap at each square
        /// it runs at (Editing/TerrainPreview.cs List), and the special traps that move a tile's
        /// floor: hack 3, the eight-position switch that sets a tile's height, and hack 0x18, the
        /// bullfrog puzzle (Uw1Traps.HackTraps), at the squares the editor finds them acting on (Triggers.Targets).
        /// </summary>
        static List<object> TileChanges(Ctx c, LevelModel m)
        {
            var s = c.S;
            var list = new List<object>();
            foreach (var ch in TerrainPreview.List(s, m))
            {
                int tt, h, f, w;
                TerrainPreview.Values(s, ch.Trap, out tt, out h, out f, out w);
                list.Add(new JLine(new JObj()
                    .Add("kind", "change terrain").Add("trap", ch.Trap.Index)
                    .Add("triggers", Ints(ch.Triggers.Select(t => t.Index)))
                    .Add("x", ch.X).Add("y", ch.Y).Add("w", ch.W).Add("h", ch.H)
                    .Add("sets", Sets(m, tt, h, f, w))
                    .Add("text", ch.What)));
            }
            foreach (var o in m.Objects)
            {
                if (o.Free || o.TileX < 0 || o.Item != 0x183 || (o.Quality != 3 && o.Quality != 0x18)) continue;
                var targets = Triggers.Targets(m, o);
                list.Add(new JLine(new JObj()
                    .Add("kind", o.Quality == 3 ? "eight-position switch height" : "bullfrog puzzle").Add("trap", o.Index)
                    .Add("triggers", Ints(Triggers.TriggersReaching(m, o).Select(t => t.Index)))
                    .Add("squares", targets.Select(t => (object)new List<object> { t.X, t.Y }).ToList())
                    .Add("text", Uw1Traps.HackTrapName(o.Quality))));
            }
            return list;
        }

        /// <summary>A new game's start (GameSession.NewGameStart: the square player_setup is called with, read from UW.EXE), on the level it is on; null elsewhere.</summary>
        static JObj PlayerStart(Ctx c, LevelModel m)
        {
            var st = c.S.NewGameStart;
            if (st == null || st.Level != m.Level) return null;
            int tx = (int)Math.Floor(st.X), ty = (int)Math.Floor(st.Y);
            double z = tx >= 0 && ty >= 0 && tx < 64 && ty < 64 ? m.FloorAt(tx, ty, st.X - tx, st.Y - ty) : double.NaN;
            return new JObj()
                .Add("level", st.Level).Add("tileX", tx).Add("tileY", ty)
                .Add("x", st.X).Add("y", st.Y).Add("z", z)
                .Add("headingDegrees", null)
                .Add("note", "The start square is read from UW.EXE (Games/StartPosition.cs: player_setup(x, y) on level 1, ui/MAINMENU.C); the player stands in its middle on its floor. The new game's facing is not read by the editor (its camera assumes 0, north): unknown.")
                .Add("description", st.Description);
        }

        /// <summary>
        /// The light sources, 0x90-0x97 (the candle, torch, taper and lantern, unlit 0x90-0x93 and
        /// lit 0x94-0x97), with OBJECTS.DAT's light table brightness (Uw1Schemas.Light; the lit ones
        /// 1-4, the unlit 0). COMOBJ.DAT's Light flag is not used: in UW1 it is set on the missiles
        /// (0x10-0x17, 0x151, 0x152) and on no light. In UW1 the 3D view's light comes from what the
        /// player carries and his spells only (GameLight.cs, map/LIGHTING.C): a light lying in the
        /// level lights nothing round it.
        /// </summary>
        static List<object> Lights(Ctx c, LevelModel m)
        {
            var light = ObjectTables.Table("Lights");
            var list = new List<object>();
            foreach (var o in m.Objects)
            {
                if (o.Free || o.TileX < 0 || o.Item < light.FirstItem || o.Item >= light.FirstItem + 8) continue;
                list.Add(new JLine(new JObj()
                    .Add("index", o.Index).Add("item", o.Item).Add("name", o.Name).Add("onTile", o.OnTile).Add("parent", o.Parent)
                    .Add("tileX", o.TileX).Add("tileY", o.TileY)
                    .Add("worldX", o.OnTile ? (object)o.WorldX : null).Add("worldY", o.OnTile ? (object)o.WorldY : null).Add("worldZ", o.OnTile ? (object)o.WorldZ : null)
                    .Add("lit", o.Item >= light.FirstItem + 4)
                    .Add("brightness", c.Tables.Get(light, o.Item - light.FirstItem, light.Schema["Brightness"]))));
            }
            return list;
        }

        /// <summary>
        /// The objects the player can stand on (question 6): those whose collision box (Hitboxes.Of:
        /// creatures and statics with a height and Touch) has COMOBJ's Solid flag (motion/MOTION.C
        /// get_pcoll). A radius of 4 covers the whole tile: a deck, a second floor over the tile
        /// (a bridge; Randomizer/NavRegions.cs NavDeck). Top and bottom in z units (8 a floor step).
        /// </summary>
        static List<object> Standable(Ctx c, LevelModel m)
        {
            var list = new List<object>();
            foreach (var h in Hitboxes.Of(m, c.ComObj).Where(b => b.Solid && !b.Creature).OrderBy(b => b.Index))
            {
                var o = m.Objects[h.Index];
                list.Add(new JLine(new JObj()
                    .Add("index", h.Index).Add("item", h.Item).Add("name", o.Name)
                    .Add("tileX", h.TileX).Add("tileY", h.TileY).Add("radius", h.Radius).Add("wholeTile", h.Radius == 4)
                    .Add("bottom", h.Bottom).Add("top", h.Top).Add("topTiles", h.Top / 32.0)));
            }
            return list;
        }
    }
}
