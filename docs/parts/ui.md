<!--
Manual changes for the ui branch (2026-11-02). Each part names the section of docs/MANUAL.md
it belongs to and the sentences it replaces or follows. Headings are the manual's own.
-->

## Opening a game

<!-- Add after the first paragraph ("... the editor remembers it."). -->

**File ▸ Open recent** lists the last games opened. A long path shows its start (the drive
or `/` and the first folder) and its end (the last folders), with `…` for the middle; the
whole path is in the item's tip.

## The 3D view

<!-- Replaces, in "Moving the camera.", the sentence "W A S D or the arrows move (Shift is
faster), Q and E turn, and R and V go up and down a floor step." -->

W A S D or the arrows move and Q and E turn for as long as they are held, smoothly: the
view picks up speed over a moment and slows down the same way when the key comes up. The
speed grows with the distance from the point looked at, and Shift makes it three times
faster (turning twice). R and V go up and down a floor step.

## Finding and selecting

<!-- Add to the "Finding objects:" list, after "Each line shows the object's picture as the
game shows it." -->

- Names are coloured by what the object is, in the list and at the top of the properties
  panel: creatures by their attitude (hostile red, upset brown, mellow green, friendly blue),
  enchanted objects purple. Traps and triggers are underlined. A creature keeps its
  attitude's colour; what it carries has its own line and colour.

<!-- Add after the "Properties:" paragraph. -->

Typing a value changes only that object's line in the list and the rows of the panel that
changed: the list keeps the lines you opened and its place.

### Selecting, copying and moving

<!-- Replaces the bullet "**Drag** a selected object to move it (and the others selected with
it) to another tile. Dropping it on a container, a creature or a trigger puts it inside (into
the trigger's chain for a trap). Esc cancels a drag." -->

- **Drag** a selected object to move it (and the others selected with it) to another tile or
  to another place in its own tile. While dragging, the object is drawn where it would land,
  a small yellow square marks the eighth of the tile under the mouse, and the status line
  reads the place as tile.fine: `(10.2, 20.5)` is tile (10, 20) at fine X 2, fine Y 5. Moved
  within its tile, an object keeps its place in the tile's list. Dropping it on a container, a
  creature or a trigger puts it inside (into the trigger's chain for a trap). Esc cancels a
  drag.

## Pixel editor

<!-- Replaces the bullet "**Zoom** (+ and −) and **Grid**; a preview shows the picture at its
size and twice it." -->

- **Zoom** (+ and −) and **Grid**. Under the drawing, a preview shows the picture at its
  size and twice it (a large picture at a size that fits). The tool and colour columns
  scroll when the window is too low for them.

<!-- Add after the first paragraph (after "... shows as ∅ over a checkerboard."). -->

**Cycling colours.** The games animate some palette entries by turning them round instead of
redrawing pixels: water, lava and fire. A corner of black and white stripes marks those
swatches, and the tooltip names the run of entries the colour turns through. Whatever is
drawn with such a colour changes colour in the game. In play both games turn their runs one
place four times a second (`gfx/COLCYCLE.C` cycle_colors, called every frame from
`ui/INTERACT.C`), in whatever palette is loaded:

| Game | Entries | Direction |
|---|---|---|
| UW1 | 0x30–0x3F, in four runs of four (blues) | each takes the next entry's colour |
| UW1 | 0x10–0x14 and 0x15–0x17 (fire reds and yellows) | each takes the previous entry's colour |
| UW2 | 0xE0–0xEF, in four runs of four (blues) | each takes the next entry's colour |
| UW2 | 0x03–0x07 and 0x08–0x0A (fire reds and yellows) | each takes the previous entry's colour |

UW1's main menu (`OPSCR.BYT`) also turns 0x40–0x7F while it waits (`ui/MAINMENU.C`). A 4-bit
picture's colour cycles when its auxiliary palette points at one of these entries. Cutscenes
have their own cycles in each animation file, not marked. **Animate the cycling colours**
shows the drawing and the preview as the game would, turning.

## Keys and mouse

<!-- Replace these rows of the table: -->

| Where | Key | What it does |
|---|---|---|
| View | W A S D, arrows | move while held (Shift: faster) |
| View | Q, E | turn while held |
| First person | W A S D, wheel | walk (held keys glide), step |
| First person | Q E, drag | turn |
| First person | R, V | look up, down while held |

<!-- Help ▸ Controls now shows the keys in two columns. -->
