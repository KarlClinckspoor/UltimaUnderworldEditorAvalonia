# UltimaUnderworldEditorFree: plan for a minimal working example

A level editor for Ultima Underworld 1 and 2 that does not use Unity. It runs on Windows, Linux
and macOS. The window has a large 3D view in the middle, tool panels on both sides and a menu
bar on top.

This plan covers the **minimal working example (MWE)**:

- open a UW1 or UW2 game folder (DATA or a SAVEn folder);
- pick a level;
- see it in 3D;
- click an object or a tile and see all its properties.

The MWE does not edit or save. It is built so editing can be added later without changing its
structure.

Written 2026-10-31. Sources: the Unity editor (`../UltimaUnderworldEditorUnity`, branch
`modding`), its project notes, and the mod manager's status.

---

## Status (2026-10-31)

The skeleton is built. See README.md for building and running it.

**Done, and seen running** (Linux, software OpenGL, screenshots checked):
- Window, menu, side panels, status bar.
- Opening a game folder, a DATA folder or a SAVEn folder.
- UW1 and UW2 level pickers.
- The textured 3D view: floors, walls, slopes, diagonals, ceilings (UW2 Void levels have none).
- Object sprites.
- Orbit camera over the new-game start square, or over the player in a saved game.
- Click picking.
- Object list with a filter.
- Object and tile properties generated from the schemas.
- The level check.

So M0, M1 and M2 are done, and most of M3 and M4.

**Changed from the plan below:**
- **Formats code:** copied into `src/Underworld.Formats` (see its SOURCE.md) instead of read
  from a sibling checkout.
- **UW2 ceiling texture:** found in the reference. The ceiling is floor slot 0xF (uw2
  3d/GRIDDB.C, `gr_ccall(pts, sqmod, 0xF)`); UW1's is slot 9.

**Added the same day:**
- The object list is ordered by index, with contents nested under their objects.
- Sprites always face the camera.
- The built-in 3D models: doors, levers, writings, furniture. Door rules come from 3d/DRAWOBJ.C
  `do_door`: an open door is drawn 3 steps lower; the leaf turns by flags & 7 sixteenths,
  one way or the other by bit 13; the portcullis is model 0xC; the secret door is model 0xF in
  the wall texture.
- A yellow grid every N tiles, with `X: n` / `Y: n` labels at the view's edges.
- The rest of the Unity editor's features are planned in ROADMAP.md.
- Object list:
  - search over names, NPC names and object texts;
  - quick filters by kind and Enchanted;
  - icons;
  - Find in all levels.
- Interface tests, two kinds:
  - headless Avalonia tests of the windows;
  - screenshot tests of the OpenGL view on Xvfb, with picture baselines and a state file.

**Added later the same day: conversations** (in `src/UwEditor.Core/Conversations`):
- CNV.ARK read and written through the archive code (UW2 compressed). Only changed blocks are
  written.
- The format: header, import table, code.
- A disassembler and assembler, with an exact round trip of every shipped conversation.
- A readable view by stack simulation, with menus, says and folded ifs.
- STRINGS.PAK editing: unchanged blocks are copied byte for byte; a changed block is encoded
  with the file's own tree.
- The global variables the scripts reach, with values from PLAYER.DAT and BGLOBALS.DAT
  (offsets from include/player.h; built-in semantics from conv/BABLHACK.C), and every use
  found from constant arguments.
- Avalonia windows: Tools ▸ Conversations and Tools ▸ Global variables.

The Unity editor's node-graph compiler (ConversationConverter.cs) is not used yet. It compiles
the node editor's language and cannot read back the shipped conversations, so editing works on
the code the game runs. The language and graph views of ROADMAP.md phase E can sit on top
later.

**Added after that: the player and graphics** (`src/UwEditor.Core/Player`, `src/UwEditor.Core/Graphics`):
- **Player record editor** with checks:
  - UW2's end-screen cheat flag, from STR + DEX + INT > 64 (game/SKILLS.C);
  - values creation cannot give, from SKILLS.DAT and the caps;
  - derived maxima (`player_compute`), and experience levels from `level_table`.
- **Inventory** (inv/INVSAVE.C): the tree, slots, editing; saving renumbers depth first.
- **The hand bit's meaning differs** between the games (CHRGEN.DAT option strings).
- **A PNG codec** (indexed export; every non-interlaced PNG read back).
- **Pictures:** GR/TR/BYT/BYT.ARK, including PANELS.GR's bare panels.
- **Critter art:** UW1 pages and UW2 pages with CR.AN, PG.MP and AS.AN; 4- and 5-bit
  run-length frames.
- **LPF cutscenes:** decoding, a run/skip/dump encoder, repacking pages within the player's
  limits.
- **Import guards** for size, colours, transparency and the games' buffer limits.

**Not yet:**
- Run on Windows and macOS.
- GitHub Actions.
- Critter animations.
- Editing and saving (ROADMAP.md phase A).

## 1. What the MWE does

| Area | In the MWE |
|---|---|
| Open | File > Open game folder… (native folder picker). The game is detected with `GameProfile.Detect` (UW2 has T64.TR or SCD.ARK). File names match in any case (`GameFiles`). A SAVEn folder reads LEV.ARK from the save and everything else from DATA. |
| Level picker | UW1: levels 1–9. UW2: a world dropdown (`GameProfile.Uw2Worlds`) and the 8 slots of that world, plus slots 73–80. Empty slots are listed but greyed out. |
| 3D view | Floors, walls, ceilings, slopes and diagonal tiles, textured with the level's texture map and palette 0. Objects are drawn as camera-facing sprites from OBJECTS.GR. Orbit camera by default, fly camera on demand, top-down toggle. |
| Selection | Click in the view to select the object or tile under the mouse. The selection is outlined. Selecting in the object list selects in the view, and the other way round. |
| Left panel | Level picker, object list (filter box; tile → object chain; container contents under their container), toggles (ceiling, objects, invisible objects, traps and triggers). |
| Right panel | **Object:** name, index, static or mobile, tile, then every record field from `Uw1Schemas.StaticObject` / `MobileObject`. Each field shows decimal and hex, with its description and source as a tooltip and a mark for Inferred / Unknown certainty. **Tile:** every field of `Uw1Schemas.Tile`, the resolved wall and floor texture numbers with thumbnails, and the first object. |
| Menu | File (Open game folder, Recent, Exit) · View (camera mode, toggles, frame selection F, wireframe) · Level (previous / next, PgUp / PgDn) · Tools (placeholders, disabled) · Help (About, keyboard). |
| Status bar | Tile under the mouse (x, y, type, height), object under the mouse, game and level, frame time. |

**Not in the MWE:** editing, saving, undo, the built-in 3D models (Uw1ExeModels), door frames,
wall decals (TMOBJ / TMFLAT), critter animations, lighting beyond a distance fade,
conversations, strings, the Tools windows.

### Window layout

```
┌ File  View  Level  Tools  Help ──────────────────────────────────────────────┐
├───────────────┬──────────────────────────────────────────────┬───────────────┤
│ LEVEL         │                                              │ OBJECT        │
│ World ▾       │                                              │ #312 lever    │
│ Slot  ◂ 3 ▸   │                                              │ static, (31,2)│
│               │                                              │ ───────────── │
│ OBJECTS       │                 3D view                      │ Item id  0x171│
│ [filter…]     │                                              │ Z          24 │
│ ▸ (31,2)      │                                              │ Heading     2 │
│   lever #312  │                                              │ …             │
│ ▸ (32,5)      │                                              │ TILE          │
│               │                                              │ Type   open   │
│ SHOW          │                                              │ Height     12 │
│ ☑ ceiling …   │                                              │ Wall tex  [▣] │
├───────────────┴──────────────────────────────────────────────┴───────────────┤
│ tile (31,2) open h12 · lever #312 · UW1 level 3 · 2.1 ms                     │
└──────────────────────────────────────────────────────────────────────────────┘
```

The side panels are resizable (GridSplitter) and can be collapsed. Docking (panels torn off
into their own windows) comes later, if wanted.

---

## 2. UI toolkit

### Options

| | Avalonia 12 + OpenGL control | Silk.NET window + Dear ImGui | Godot 4 (C#) | Web (TypeScript + three.js) |
|---|---|---|---|---|
| Reuses the C# Formats library | yes | yes | yes | no: rewrite |
| Real widgets (text boxes, lists, trees, grids, native file pickers) | yes | drawn by ImGui; no native dialogs | yes, game-style | yes |
| Crisp text and high DPI | yes | needs care (one atlas per size) | yes | yes |
| Long text editing (future conversation editor) | AvaloniaEdit | weak | fair | good |
| 3D view | your own GL in `OpenGlControlBase` | your own GL, simplest | built in | three.js |
| Another engine to fight | no | no | yes (same kind of problem as Unity) | no |
| Already used in the project | mod manager (Avalonia 12) | the paused Unity ImGui shell | no | skill check site plan |

### Choice: Avalonia 12 with Silk.NET.OpenGL inside `OpenGlControlBase`

- The mod manager already uses Avalonia 12. Styles, settings code and know-how carry over.
- An editor is mostly forms: tables, lists, text, pickers. The future Tools windows (strings,
  conversations, object tables) need real text boxes and grids.
- Skia composes the GL picture with the rest of the window. Menus, popups and tooltips can
  overlap the 3D view (WPF's HwndHost cannot do this).
- File and folder pickers come from `IStorageProvider`. The old dialog classes were removed in 12.

**Fallback:** if the GL control fails on some platform, the renderer can be hosted in a
Silk.NET window with ImGui panels instead. To keep that possible, the renderer knows nothing
about Avalonia (it gets a GL loader and a size), and the view models know nothing about GL. The
Unity ImGui crashes came from UImGui's native build inside Unity, not from ImGui itself.

### GL details

- **Contexts.** On Windows, Avalonia gives an OpenGL ES 3.0 context through ANGLE by default.
  On Linux it gives desktop GL (GLX/EGL). On macOS it gives desktop GL 3.2 core.
- **Shaders.** Write them once in the common subset. Prepend `#version 300 es` +
  `precision mediump float;` on GLES, or `#version 330 core` on desktop GL. The control's
  `GlVersion.Type` says which.
- **Features used:** VAOs, `GL_TEXTURE_2D_ARRAY` for wall and floor textures (64×64 layers;
  UW1's 32×32 floors scaled up nearest-neighbour), one RGBA atlas for object sprites. Nothing
  beyond GLES 3.0.
- **Rendering on demand.** Call `RequestNextFrameRendering()` only when the camera, the
  selection or the level changes. The view is idle the rest of the time.

---

## 3. Code layout

```
UltimaUnderworldEditorFree/
  UltimaUnderworldEditorFree.sln
  Directory.Build.props          EditorRoot: where the Unity editor's sources are
  PLAN.md
  src/
    Underworld.Shared/           compiles $(EditorRoot)/Assets/Scripts/Formats/**/*.cs
                                 (the same way as the mod manager's src/Underworld.Shared)
    UwEditor.Core/               no UI, no GL; net10.0
      GameSession.cs             open a folder, detect the game, DATA vs SAVEn, archive
      LevelList.cs               slots, worlds, names, empty slots
      LevelModel.cs              tiles, objects, tile chains, container contents, texture map
      TileInfo.cs                decode through Uw1Schemas.Tile
      ObjectInfo.cs              decode through Uw1Schemas.StaticObject / MobileObject
      Names.cs                   object names (STRINGS.PAK block 4)
      TextureSet.cs              palette 0, walls/floors/sprites as RGBA
      Geometry/LevelMesh.cs      port of WalkGeometry (System.Numerics)
      Geometry/ObjectPlacement.cs  tile + (fine + 0.5) / 8; height Z / 32
      Picking.cs                 ray through the tile grid (DDA) and against sprite quads
      Selection.cs               selected tile / object, change events
    UwEditor.Render/             Silk.NET.OpenGL; no Avalonia
      GlRenderer.cs              init(proc loader), resize, draw(scene, camera)
      Shaders/level.glsl, sprite.glsl, outline.glsl
      Camera.cs                  orbit / fly / top-down
    UwEditor.App/                Avalonia 12 + CommunityToolkit.Mvvm
      MainWindow.axaml           menu, three columns, status bar
      LevelView.cs               OpenGlControlBase → GlRenderer; mouse and keys
      ViewModels/                Main, LevelPanel, ObjectList, Properties, Status
      Settings.cs                recent folders (JSON in the user's app data)
  tests/
    UwEditor.Core.Tests/         xUnit; game files from UW_GAMEDATA, like the editor's tests
```

**How the Formats code is found.** Use the same three places as the mod manager, in order:

1. `-p:EditorRoot=…`;
2. `external/UltimaUnderworldEditorUnity`;
3. the sibling folder `../UltimaUnderworldEditorUnity`.

The code must build as .NET Standard 2.0 and C# 8, which is what the `Underworld.Formats`
asmdef requires. One copy of the format code is then shared by three programs, and the Unity
editor's parity tests keep guarding it.

### What exists and what is new

Already in `Assets/Scripts/Formats`, Unity-free:

- `GameProfile`: game detection, block numbers (`LevelBlock(l)`, `TextureBlock(l)`), texture
  counts, world names.
- `GameFiles`: file names in any case.
- `Archive`: UW1 and UW2 LEV.ARK, with LZSS for UW2.
- `LevelBlock`: tile words, object records, `Next` / `Link` chains, the game's object-list check.
- `Uw1Schemas` / `FieldDef`: every tile and object field, with its description, source and
  certainty. Both games share these layouts.
- `StringsPak`: names.
- `GraphicsCodec` and `GrFile`: palettes, `.TR` textures, `.GR` pictures.
- `Uw1ExeModels`: for later.

New in `UwEditor.Core` (small; each piece is ported from a file named here):

- **Texture map decode.** From `Data/LevArk.cs` `ReadTextureBlock` (about 20 lines):
  - UW1: 48 wall words, then 10 floor words, then 6 door bytes;
  - UW2: one list of 64 words, whose first 16 are also the floors, then door bytes at 0x80.
- **Level mesh.** Ported from `Extensions/View/WalkGeometry.cs` (247 lines; it uses only
  Unity's `Vector2`, `Vector3` and `Color`), with `Tests/Formats.Tests/WalkViewTests.cs`
  ported beside it.
  - Scale: a tile is 1.0, a floor step 0.25, the ceiling at height 16.
  - UW2 Void levels (world 8 except level 68) have no ceiling.
- **Object placement.** From `Extensions/View/WalkObjects.cs`. Includes the open-door offset
  (24 z) for the later door models.
- **Names.** UW1 uses STRINGS.PAK block 4. UW2's names should match `Uw2Items` (512 names);
  check this in a test.

Once these settle, move the texture-map decode, the tile and object view types, and the
geometry into `Formats` on the Unity side. Then both editors draw the same level from the same
code.

---

## 4. Steps

Each step ends with something that runs, plus its tests.

### M0 — Skeleton (½–1 day)
- Solution, the three projects, `Directory.Build.props` with `EditorRoot`. Use the same
  Avalonia 12 version and target framework as the mod manager.
- A main window with the menu, three columns, GridSplitters and a status bar.
- A `LevelView` that clears to a colour and draws one textured quad.
- A GitHub Actions matrix that builds on windows, ubuntu and macos.
- **Done when:** the window opens, the view resizes cleanly and redraws on demand, on Windows
  and Linux. macOS works if a Mac is available; otherwise it builds in CI.

### M1 — Loading (1 day)
- `GameSession.Open(folder)`:
  - detect the game;
  - for a SAVEn folder, take LEV.ARK from the save and the rest from DATA;
  - load the archive;
  - build the level list with world names and empty slots.
- `LevelModel` from `LevelBlock`:
  - 64×64 `TileInfo`;
  - the 1024 objects, marking free ones (from the free lists) and the ones reached from tiles;
  - tile chains through `Next`;
  - container contents through `Link` (only on objects that use link as contents; follow the
    Unity editor's rule);
  - the texture map.
- Object names.
- **Tests:**
  - every UW1 level and every filled UW2 slot decodes;
  - `LevelBlock.Check` passes on every shipped level;
  - UW2 slot 73 has 1,046 open tiles and 427 objects (figures from the notes);
  - the names match the Unity editor's for a sample of ids.

### M2 — Textures (½ day)
- Palette 0 from PALS.DAT.
- UW1: W64.TR and F32.TR. UW2: T64.TR for both walls and floors.
- OBJECTS.GR pictures through `GrFile`, packed into one atlas with their UV rectangles.
  UW2 has 464 pictures.
- Palette index 0 is transparent in sprites.
- **Tests:** texture counts per game; a decoded wall matches the Unity editor's pixels for one
  known texture (the parity tests have the reference).

### M3 — 3D view (2 days)
- `LevelMesh` (the WalkGeometry port): one mesh per level, with a texture layer per vertex. It
  is rebuilt when the level changes.
- The level shader: unlit, with a distance fade toward black, as in the walk view.
- Sprites: one quad per drawn object, standing on its Z, facing the camera around the vertical
  axis.
- Hidden by default (toggles in View):
  - invisible objects;
  - traps and triggers (0x180–0x1BF in UW1; UW2 uses its own ranges);
  - objects inside containers.
- Camera:
  - orbit: right-drag turns, middle-drag pans, the wheel zooms;
  - fly: hold right button + WASD / QE, Shift for speed;
  - top-down: T;
  - F frames the selection;
  - Home goes to the start square (`StartPosition`), when the level is the start level.
- **Done when:** UW1 level 1 and UW2 level 1 look like the Unity walk view, with no gaps at
  slopes, diagonals or height changes.

### M4 — Selection and properties (1½ days)
- **Picking** on the CPU:
  - walk the ray through the tile grid (DDA);
  - test the floor, ceiling and wall faces of each tile passed, and the sprite quads on it;
  - take the nearest hit.
  - No GPU ID buffer is needed at this scale.
- **Highlight:** an outline of the selected sprite's quad, or of the selected tile's floor and
  walls.
- **Properties panel**, generated from the schemas:
  - one row per field: name | value | hex;
  - read-only for now;
  - tooltip with description, source and certainty;
  - the mobile part only for mobile objects (index < 256).
- **Tile section:** decoded type name, height, light, no-magic, door bit (with its real
  meaning, from the schema text), and the wall and floor texture numbers with thumbnails.
- **Object list:** grouped by tile, with a filter on name or id. It stays in sync with the
  view in both directions.
- **Status bar:** shows what is under the mouse.
- **Tests:**
  - picking on synthetic levels: a ray down onto a tile; a ray at a wall; a sprite in front of
    a wall;
  - a property row's value equals `FieldDef.Get` on the record.

### M5 — Finish the MWE (½ day)
- Recent folders.
- Clear errors for a wrong folder or missing files, instead of crashes.
- Remember the window and panel sizes.
- A README with build and run steps and screenshots.
- Self-contained `dotnet publish` for win-x64, linux-x64 and osx-arm64.

About 6–7 working days in total.

---

## 5. After the MWE, so the design allows it now

- **Editing.** Every change goes through a command that calls `FieldDef.Set` on the level's
  `LevelBlock` bytes and can be undone. `LevelBlock` keeps the original bytes, so whatever the
  editor does not model stays as it was. The properties rows become editable, with ranges from
  `FieldDef.Min` / `Max`.
- **Saving.** The Unity editor's safety rules:
  - write only what changed;
  - write through a temporary file;
  - keep `.BKP` originals;
  - run the object-list check before crunching free records.

  This needs the save half of `Data/LevArk.cs` moved into Formats first.
- **Drawing.** The built-in models from `Uw1ExeModels`; door frames and leaves; wall decals
  from TMOBJ and TMFLAT (UW2's doors start at TMOBJ + 0x40); critter animation frames;
  LIGHT.DAT and SHADES.DAT shading.
- **Other views and tools.**
  - A 2D top-down map as a second tab, for painting tiles.
  - Tile tools: type, height, texture brush.
  - The Unity editor's Tools windows, ported one by one: Fixes, search across levels, automap,
    strings.
  - UW2 trap descriptions, after `Uw2Items` and `Uw2Traps` move into Formats.
- **Mods.** Export through the shared `Underworld.Modding` code, the same as the mod manager.

---

## 6. Risks

| Risk | What to do |
|---|---|
| `OpenGlControlBase` misbehaves on macOS or some Linux driver | Find out in M0. Fallback: the Silk.NET + ImGui host (§2). The renderer does not change. |
| ANGLE (GLES 3.0) on Windows differs from desktop GL | Use only the common subset. Test on Windows first, since that is the main machine. Desktop GL (WGL) can be asked for if needed. |
| Formats code changes on the Unity side | Build both editors in CI. Formats is guarded by the Unity repo's parity tests. |
| Game files in a public repo | Never commit them. Tests read `UW_GAMEDATA` or a git-ignored `gamedata/` folder, as in the Unity repo. |
| Licence of krokots' code, which Formats came from | Before publishing the new repo, check what the Unity repo's licence allows for the shared code. |

---

## 7. Decisions for Karl (defaults in bold)

1. Toolkit: **Avalonia 12 + OpenGL**, or Silk.NET + ImGui.
2. Repo: **a new GitHub repo, `KarlClinckspoor/UltimaUnderworldEditorFree`, local until you say
   to push**.
3. How it gets the Formats code: ~~from a sibling checkout~~ **copied for now** (Karl, 2026-10-31).
4. Camera at start: **orbit over the start square**, or fly at eye height.
