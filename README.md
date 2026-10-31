# Underworld Editor (free of Unity)

A level editor for **Ultima Underworld I** and **II** that runs on Windows, Linux and macOS.
It uses Avalonia 12 for the window and OpenGL for the 3D view. This is the skeleton. It
opens a game, shows any level in 3D, and shows the properties of the selected object and
tile. It does not edit or save yet. The plan is in [PLAN.md](PLAN.md); the road to the Unity
editor's other features is in [ROADMAP.md](ROADMAP.md).

![UW1, level 1, the sack next to the start selected](docs/uw1-level1.png)

## What it does

- **File ▸ Open game folder…** (Ctrl+O). Choose the game folder (the one with `DATA` in it),
  the `DATA` folder itself, or a saved game (`SAVE1`…). The game is recognised by its
  files. A saved game takes its levels and player from the save, and everything else from
  `DATA`.
- **The camera starts over the start square of a new game.** The square is read from
  `UW.EXE` / `UW2.EXE`, so a patched start square shows too. In a saved game the camera
  starts over the player, facing where the player faces. The magenta cross marks the
  spot.
- **Picking a level:**
  - UW1: levels 1–9.
  - UW2: pick the world, then a slot in it. Empty slots are marked.
  - PgUp and PgDn step through the levels.
- **The 3D view:**
  - Floors, walls, slopes and diagonal walls, textured through the level's texture map.
  - The ceiling (G), seen only from below, as in the game.
  - Objects drawn the game's way, from COMOBJ.DAT's render field:
    - doors in their frames (open doors swung by their flags, the portcullis raised, the
      secret door in the wall texture);
    - levers, switches, writings and paintings on their walls;
    - furniture, pillars, bridges and so on as the 3D models built into `UW.EXE` /
      `UW2.EXE`;
    - everything else as its picture, always facing the camera.
  - M switches the models off. Invisible objects and traps or triggers can be shown too.
  - A yellow grid every N tiles (H; N set in the Show box), laid on the floors. The view's
    edges name each line, as `X: 30` or `Y: 45`.
  - Darkness, centred on the point the camera looks at.
- **Finding objects:**
  - The search box matches item names, NPC names ("human (Lord British)"), the text of
    books, scrolls, writings, gravestones and text traps, `#index` and `0x` item ids.
  - The chips narrow the list to weapons, food, monsters, keys, traps or triggers (any of
    those chosen), and Enchanted to enchanted objects.
  - Each line shows the object's picture as the game shows it.
  - **Level ▸ Find in all levels…** (Ctrl+Shift+F) runs the same search, with every item
    kind, over all levels. Double-click a result to open its level with the object selected.
- **Selecting:**
  - Click an object or a tile in the view, or pick one in the object list.
  - The list shows the objects lying on tiles in index order. Whatever is inside an object
    (container contents, inventories, a trigger's trap, a door's lock) hangs under it.
  - The filter takes a name, `#index` or `0x` item id.
- **Properties:** every field of the object and tile records, with its value in decimal and
  hex. The tooltip gives the field's meaning and its source in the reconstructed game code.
  Fields whose meaning is only inferred or unknown are marked. The tile panel also shows
  its wall and floor textures.
- **Level ▸ Check the object lists:** runs the game's own consistency check (Obj_ListOkay).

Help ▸ Controls lists the mouse and keys.

![UW2, Britannia, the start room](docs/uw2-britannia.png)

## Building and running

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet run --project src/UwEditor.App
dotnet run --project src/UwEditor.App -- "C:\Games\UW2\SAVE1"      # open a folder at start
dotnet run --project src/UwEditor.App -- "C:\Games\UW2" --level 66  # and show a level
```

To build a self-contained program:

```
dotnet publish src/UwEditor.App -c Release -r win-x64 --self-contained
```

Use `-r linux-x64` or `-r osx-arm64` for the other systems. On Linux without a GPU driver,
Mesa's software renderer is used, which Avalonia normally refuses. The view is slower that
way, but it works.

## Tests

```
dotnet test
```

There are three test projects:

| Project | What it tests | Needs |
|---|---|---|
| `tests/UwEditor.Core.Tests` | loading, levels, geometry, models, picking, the grid, on the shipped data of both games | the game files |
| `tests/UwEditor.App.Tests` | the main window and the find window in Avalonia's headless mode: typing, clicking chips, keys, selection, the properties panel, find and go to, saved games; plus pictures of the panels compared with baselines | the game files |
| `tests/UwEditor.Screenshots` | the real program with its OpenGL view on a private virtual screen (Xvfb + Mesa): what it draws, clicking an object in the view, camera keys, the grid; pictures compared with baselines, facts read from the program's `--state-file` | Linux with `Xvfb`, `xdotool` and ImageMagick; skipped elsewhere |

The game files come from `UW_GAMEDATA`, a folder holding `UW` and/or `UW2`, each with its
`DATA` folder and executable (UW2's `SAVE1`/`SAVE2` are used if present). Failing that, they
come from the repository's `gamedata/` folder, which git ignores. Tests whose game is missing
pass without checking anything. Set `UW_REQUIRE_GAMEDATA=1` to make them fail instead.

**Picture baselines.** The first run of a picture test records its baseline under
`tests/<project>/baselines/` and reports the test as skipped. Look at the pictures; later runs
compare against them. A pixel differs when a colour channel is off by more than 40, and up to
1% (headless) or 2% (OpenGL) of the pixels may differ. A mismatch writes the actual picture and
a diff, with differing pixels in magenta, to `TestResults/snapshots/`. After an intended change
of the look, run with `UPDATE_SNAPSHOTS=1` to record new baselines. The baselines show the
game's graphics, so they are made from your own game files and not committed.

The core tests check, among other things:
- every level of both games loads and passes the game's object-list check;
- UW2 has 42 levels, and slot 73 has 1,046 open tiles and 427 objects;
- the start squares in the executables;
- a saved game's player position;
- names, textures and object pictures;
- that the view is not mirrored;
- picking;
- that every level builds a valid mesh;
- that levers, doors and furniture become models, and a wall lever lies on its wall;
- that every shipped open door is 24 above its floor, so it is drawn on the floor (the
  reference's rule);
- the grid lines and their labels.

**Options for scripted runs** (used by the screenshot tests):
- `--level N`: show this level first;
- `--select N`: select and frame this object;
- `--size WxH`: the window's size;
- `--ready-file F`: written once the first level has been drawn;
- `--state-file F`: level, selection, camera and view rectangle as JSON after every change.

## Layout

| Project | What it is |
|---|---|
| `src/Underworld.Formats` | The Unity editor's format code, copied (see its `SOURCE.md`). .NET Standard 2.0. |
| `src/UwEditor.Core` | Opening games, levels, tiles and objects, the level mesh, the camera, picking, property rows. No UI. |
| `src/UwEditor.Render` | The OpenGL drawing (Silk.NET), GL 3.3 or GLES 3.0. No window toolkit. |
| `src/UwEditor.App` | The Avalonia program: window, menus, panels, the GL view control. |
| `tests/UwEditor.Core.Tests` | xUnit tests on the real game files. |
| `tests/UwEditor.App.Tests` | Headless interface tests of the Avalonia windows. |
| `tests/UwEditor.Screenshots` | Screenshot tests of the program with its OpenGL view (Linux). |
| `tests/Shared` | Picture comparison and baselines, shared by the two interface test projects. |

The meaning of the files comes from
[underworld-exhumed](https://github.com/abedegno/underworld-exhumed), the reconstructed source
of both games, which is the reference.
