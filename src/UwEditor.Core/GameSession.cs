using System;
using System.Collections.Generic;
using System.IO;
using UltimateEditor.Games;

namespace UwEditor.Core
{
    /// <summary>Where the camera starts: a level, a point in tiles (x east, y north, z up) and a facing.</summary>
    public sealed class StartPoint
    {
        public int Level;
        public double X, Y, Z;
        /// <summary>Clockwise from north, in degrees (the game's headings: 0 north, 90 east).</summary>
        public double FacingDegrees;
        /// <summary>What the point is, for the status bar ("start square (32, 2)", "player in SAVE1").</summary>
        public string Description;
        public bool FromSave;
    }

    /// <summary>
    /// An opened game: its folders, LEV.ARK, names, graphics and where the camera starts.
    /// A saved game (SAVEn next to DATA) has its own LEV.ARK and PLAYER.DAT; everything else
    /// is read from DATA, as the game does (uw2 inv/INVSAVE.C, docs/FORMATS.md).
    /// </summary>
    public sealed class GameSession
    {
        public GameKind Kind { get; private set; }
        public GameProfile Profile { get; private set; }
        /// <summary>The folder holding DATA and the executable.</summary>
        public string GameFolder { get; private set; }
        public string DataFolder { get; private set; }
        /// <summary>The SAVEn folder, or null when DATA's levels are open.</summary>
        public string SaveFolder { get; private set; }
        public string SaveDescription { get; private set; }
        public string LevArkPath { get; private set; }
        public Archive LevArk { get; private set; }
        public ObjectNames Names { get; private set; }
        public GameStrings Strings { get; private set; }
        /// <summary>The accent table: which codes stand for which letters in the editors (kept per game folder in the editor's settings).</summary>
        public AccentTable Accents { get; set; } = new AccentTable();
        public GameGraphics Graphics { get; private set; }
        /// <summary>The picture files opened for editing, with their unsaved replacements: one copy of each file for every window (graphics, pixel editor, item slots).</summary>
        public Graphics.PictureStore Pictures { get { return pictures ?? (pictures = new Graphics.PictureStore(DataFolder, Kind)); } }
        Graphics.PictureStore pictures;

        /// <summary>Reads the graphics the view draws again (after OBJECTS.GR or the textures were saved).</summary>
        public void ReloadGraphics() { Graphics = GameGraphics.Load(DataFolder, Kind); }
        public PlayerDat Player { get; private set; }
        /// <summary>The 3D models built into UW.EXE / UW2.EXE, or null when the executable is missing.</summary>
        public UltimateEditor.Uw1.Uw1ExeModels ExeModels { get; private set; }
        /// <summary>COMOBJ.DAT's render field per item: 0 sprite, 1 critter, 2 model, 3 texture-mapped model.</summary>
        public int[] RenderTypes { get; private set; }
        public StartPoint Start { get; private set; }
        /// <summary>Where a new game puts the player (always level 1): from the executable, else the shipped square.</summary>
        public StartPoint NewGameStart { get; private set; }
        /// <summary>A saved game's player (PLAYER.DAT), or null.</summary>
        public StartPoint PlayerPoint { get; private set; }
        /// <summary>The executable holding the new-game start square, or null.</summary>
        public string ExePath { get; private set; }

        public bool IsSave { get { return SaveFolder != null; } }
        public bool IsUw2 { get { return Kind == GameKind.UW2; } }

        public string Title
        {
            get
            {
                string s = Kind == GameKind.UW2 ? "Ultima Underworld II" : "Ultima Underworld";
                if (IsSave) s += " — " + Path.GetFileName(SaveFolder) + (string.IsNullOrEmpty(SaveDescription) ? "" : " (" + SaveDescription + ")");
                return s;
            }
        }

        /// <summary>
        /// Opens a game from a path: the game folder, its DATA folder, a SAVEn folder, or a file
        /// in one of them (UW.EXE, UW2.EXE, LEV.ARK).
        /// </summary>
        /// <summary>
        /// Opens a game: the game folder, its DATA folder, or a saved game. A saved game is any
        /// folder with LEV.ARK and PLAYER.DAT (SAVE1, or a copy named anything); it is read with
        /// the DATA folder beside it, or with gameFolder's (the game folder or its DATA) when it lies
        /// elsewhere.
        /// </summary>
        public static GameSession Open(string path, string gameFolder)
        {
            if (string.IsNullOrWhiteSpace(gameFolder)) return Open(path);
            path = Path.GetFullPath(path.Trim().Trim('"'));
            string g = Path.GetFullPath(gameFolder.Trim().Trim('"'));
            string data = Sub(g, "DATA") ?? g;
            if (!Has(data, "LEV.ARK")) throw new FileNotFoundException("No DATA folder with LEV.ARK in " + gameFolder + ".");
            if (!LooksLikeSave(path)) throw new FileNotFoundException(path + " is not a saved game (it needs LEV.ARK and PLAYER.DAT).");
            return OpenWith(data, path);
        }

        /// <summary>A folder holding a saved game's LEV.ARK and PLAYER.DAT.</summary>
        public static bool LooksLikeSave(string path)
        {
            // DATA itself ships a PLAYER.DAT (301 bytes, read by nothing): it is the game's data, not a save
            return Directory.Exists(path) && !string.Equals(Path.GetFileName(path.TrimEnd('/', '\\')), "DATA", StringComparison.OrdinalIgnoreCase)
                && Has(path, "LEV.ARK") && Has(path, "PLAYER.DAT") && Sub(path, "DATA") == null;
        }

        public static GameSession Open(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("No folder given.");
            path = Path.GetFullPath(path.Trim().Trim('"'));
            if (File.Exists(path)) path = Path.GetDirectoryName(path);
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException("The folder " + path + " does not exist.");

            string data = null, save = null;
            string sub = Sub(path, "DATA");
            if (sub != null && Has(sub, "LEV.ARK"))
                data = sub;
            else if (IsSaveFolderName(Path.GetFileName(path)) || LooksLikeSave(path))
            {
                string parent = Path.GetDirectoryName(path);
                string d = parent == null ? null : Sub(parent, "DATA");
                if (d == null || !Has(d, "LEV.ARK"))
                    throw new SaveWithoutGameException(path);
                if (!Has(path, "LEV.ARK")) throw new FileNotFoundException("The saved game " + path + " has no LEV.ARK.");
                data = d;
                save = path;
            }
            else if (Has(path, "LEV.ARK"))
                data = path;
            if (data == null)
                throw new FileNotFoundException("No Ultima Underworld data in " + path + ". Choose the game folder (with DATA in it), the DATA folder, or a SAVE folder.");
            return OpenWith(data, save);
        }

        static GameSession OpenWith(string data, string save)
        {
            var s = new GameSession();
            s.DataFolder = data;
            s.SaveFolder = save;
            s.GameFolder = Path.GetDirectoryName(data);
            s.Kind = GameProfile.Detect(data);
            s.Profile = GameProfile.For(s.Kind);
            s.LevArkPath = GameProfile.Find(save ?? data, "LEV.ARK");
            s.LevArk = Archive.Load(s.LevArkPath, s.Kind);
            s.Strings = GameStrings.Load(data);
            s.Names = ObjectNames.From(s.Strings);
            s.Graphics = GameGraphics.Load(data, s.Kind);
            string comobj = GameProfile.Find(data, "COMOBJ.DAT");
            s.RenderTypes = ObjectModels.ReadRenderTypes(comobj != null ? File.ReadAllBytes(comobj) : null);
            try
            {
                string exe = StartPosition.ExePath(data, s.Kind);
                s.ExeModels = exe != null ? UltimateEditor.Uw1.Uw1ExeModels.Load(File.ReadAllBytes(exe)) : null;
            }
            catch (Exception) { s.ExeModels = null; }
            if (save != null)
            {
                string desc = GameProfile.Find(save, "DESC");
                s.SaveDescription = desc != null ? ReadDesc(desc) : null;
                string pd = GameProfile.Find(save, "PLAYER.DAT");
                if (pd != null)
                {
                    try { s.Player = PlayerDat.Parse(File.ReadAllBytes(pd), s.Kind); }
                    catch (Exception) { s.Player = null; }
                }
            }
            s.ExePath = StartPosition.ExePath(data, s.Kind);
            s.NewGameStart = s.FindNewGameStart();
            s.PlayerPoint = s.FindPlayer();
            s.Start = s.PlayerPoint ?? s.NewGameStart;
            return s;
        }

        /// <summary>Reads what the session keeps from COMOBJ.DAT again (the render types), after the object tables were saved.</summary>
        public void ReloadObjectData()
        {
            string comobj = GameProfile.Find(DataFolder, "COMOBJ.DAT");
            RenderTypes = ObjectModels.ReadRenderTypes(comobj != null ? File.ReadAllBytes(comobj) : null);
        }

        /// <summary>The new-game start square (from the executable) or the saved player's position.</summary>
        StartPoint FindPlayer()
        {
            return PlayerAt(Player);
        }

        /// <summary>Where a PLAYER.DAT puts the player, or null.</summary>
        public StartPoint PlayerAt(PlayerDat p)
        {
            var Player = p;
            if (Player != null && Player.SavedLevel >= 1 && Player.SavedLevel <= Profile.LevelSlots)
            {
                // record 0x54-0x5D: x and y in 1/256 of a tile, z with 0x40 a floor step
                // (PN.x = (x << 8) + 0x80, PN.z = hgt_val[height]: motion/PHYSICS.C player_setup),
                // facing a 16-bit angle, level 1-based (game/PLAYDATA.C)
                return new StartPoint
                {
                    Level = Player.SavedLevel,
                    X = Player.SavedX / 256.0,
                    Y = Player.SavedY / 256.0,
                    Z = Player.SavedZ / 256.0,
                    FacingDegrees = Player.SavedFacing / 65536.0 * 360.0,
                    FromSave = true,
                    Description = string.Format("player in {0}, tile ({1}, {2})", Path.GetFileName(SaveFolder), Player.TileX, Player.TileY),
                };
            }
            return null;
        }

        /// <summary>The new-game start square the game ships with (uw1 ui/MAINMENU.C player_setup(0x20, 2); uw2 (0x13, 0x30)), whatever the executable now says.</summary>
        public StartPoint ShippedNewGameStart()
        {
            int x = Kind == GameKind.UW2 ? 0x13 : 0x20, y = Kind == GameKind.UW2 ? 0x30 : 2;
            return new StartPoint { Level = 1, X = x + 0.5, Y = y + 0.5, Z = double.NaN, FacingDegrees = 0, Description = string.Format("the shipped new game start square ({0}, {1})", x, y) };
        }

        StartPoint FindNewGameStart()
        {
            // a new game: player_setup(x, y, how) on level 1 (uw1 ui/MAINMENU.C 0x20, 2;
            // uw2 0x13, 0x30), read from the executable in case it was patched
            int x, y;
            bool fromExe = StartPosition.Load(DataFolder, Kind, out x, out y);
            if (!fromExe) { var d = ShippedNewGameStart(); x = (int)d.X; y = (int)d.Y; }
            return new StartPoint
            {
                Level = 1,
                X = x + 0.5,
                Y = y + 0.5,
                Z = double.NaN,   // the floor of the square, filled in when the level is loaded
                FacingDegrees = 0,
                Description = string.Format("new game start square ({0}, {1}){2}", x, y, fromExe ? "" : ", the shipped default (no executable found)"),
            };
        }

        /// <summary>
        /// Moves the new-game start square: patches the player_setup call in the executable
        /// (keeping UW.EXE.BKP and UW.EXE.ORIG). The level is always 1.
        /// </summary>
        public void SetNewGameStart(int x, int y)
        {
            if (ExePath == null) throw new InvalidOperationException("No " + (IsUw2 ? "UW2.EXE" : "UW.EXE") + " next to DATA: the start square is stored in the executable.");
            var exe = File.ReadAllBytes(ExePath);
            if (!StartPosition.Patch(exe, Kind, x, y)) throw new InvalidOperationException("The start call was not found in " + Path.GetFileName(ExePath) + " (another version?).");
            UwEditor.Core.Conversations.SafeFile.Write(ExePath, exe);
            NewGameStart = FindNewGameStart();
            if (PlayerPoint == null) Start = NewGameStart;
        }

        /// <summary>After the player was moved in PLAYER.DAT (the player window keeps the sheet).</summary>
        public void PlayerMoved(PlayerDat p)
        {
            Player = p;
            PlayerPoint = PlayerAt(p);
            Start = PlayerPoint ?? NewGameStart;
        }

        /// <summary>The levels LEV.ARK has room for, with whether each slot holds a level.</summary>
        public List<LevelSlot> LevelSlots()
        {
            var list = new List<LevelSlot>();
            for (int l = 1; l <= Profile.LevelSlots; l++)
            {
                int b = Profile.LevelBlock(l);
                bool present = false;
                if (LevArk.Has(b))
                {
                    byte[] block = LevArk.Read(b);
                    present = block != null && block.Length >= LevelBlock.MagicAt + 2 && new LevelBlock(block, Kind).MagicOk;
                }
                list.Add(new LevelSlot(this, l, present));
            }
            return list;
        }

        /// <summary>Takes a rewritten LEV.ARK (after saving levels).</summary>
        public void ReplaceLevArk(byte[] bytes) { LevArk = Archive.Open(bytes, Kind); }

        /// <summary>Reads STRINGS.PAK again (after it was saved), and the names from it.</summary>
        public void ReloadStrings()
        {
            Strings = GameStrings.Load(DataFolder);
            Names = ObjectNames.From(Strings);
        }

        public LevelModel LoadLevel(int level)
        {
            return LevelModel.Load(this, level);
        }

        static string Sub(string folder, string name)
        {
            foreach (var d in Directory.GetDirectories(folder))
                if (string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }

        static bool Has(string folder, string name) { return GameProfile.Find(folder, name) != null; }

        public static bool IsSaveFolderName(string name)
        {
            if (name == null) return false;
            name = name.ToUpperInvariant();
            return name.Length == 5 && name.StartsWith("SAVE") && char.IsDigit(name[4]);
        }

        /// <summary>DESC: the save's description, raw text with no terminator (GAMEWRAP.C SaveGame).</summary>
        static string ReadDesc(string path)
        {
            var b = File.ReadAllBytes(path);
            var c = new List<char>();
            foreach (var x in b) { if (x == 0) break; c.Add((char)x); }
            return new string(c.ToArray()).Trim();
        }
    }

    /// <summary>One slot of LEV.ARK, for the level picker.</summary>
    public sealed class LevelSlot
    {
        public int Level { get; }
        public bool Present { get; }
        public int World { get; }
        public string Name { get; }

        public LevelSlot(GameSession s, int level, bool present)
        {
            Level = level;
            Present = present;
            World = s.Profile.WorldOf(level);
            if (s.Kind == GameKind.UW1) Name = "Level " + level;
            else Name = string.Format("Level {0}  ·  slot {1}", s.Profile.PlaceInWorld(level), level);
            if (!present) Name += " (empty)";
        }

        public override string ToString() { return Name; }
    }

    /// <summary>A saved game with no game folder round it: it needs a DATA folder to be read with (GameSession.Open(path, gameFolder)).</summary>
    public sealed class SaveWithoutGameException : FileNotFoundException
    {
        public string SaveFolder { get; private set; }
        public SaveWithoutGameException(string save) : base("A saved game is read with the game's DATA folder, and there is no DATA folder with LEV.ARK beside " + save + ".") { SaveFolder = save; }
    }
}
