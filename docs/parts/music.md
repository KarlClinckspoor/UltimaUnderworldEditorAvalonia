#### Music

The **Music** tab lists the game's themes. Each theme has two files in `SOUND`: one for the
FM cards (AdLib, Sound Blaster; UW1 `AWnn.XMI`, UW2 `UWAnn.XMI`) and one for the Roland
MT-32 (UW1 `UWnn.XMI`, UW2 `UWRnn.XMI`). The two digits are the theme number in octal: theme 8
is `xx10.XMI`. The list shows each theme's number, when the game plays it, both files' sizes,
how many timbres each needs, and the result of the checks below. Select a theme to see the
full report: when it plays and what follows it, the checks of both files and the timbres they
use.

The editor does not play music. To listen to a theme, export it and open the MIDI file in a
player. A MIDI player uses General MIDI instruments, not the game's timbres, so it sounds
different from the game; the MT-32 file plays with the wrong instruments on a General MIDI
device, because MT-32 program numbers name other instruments.

- **Export FM MIDI…** and **Export MT-32 MIDI…** save the selected theme as a standard MIDI
  file (format 0). The timing is exact: the game's music runs at 120 ticks a second, and so
  does the file (60 ticks a beat at 120 beats a minute). The game's own controllers are kept;
  its loops (controllers 116 and 117) also get `loopStart` and `loopEnd` markers, which other
  programs read. The file carries the theme's timbre list in an event other programs ignore,
  so exporting and importing a theme again gives the same file.
- **Export all…** saves both files of every theme into a folder (`AW01.mid`, `UW01.mid`...).
- **Import MIDI…** makes the selected theme from a MIDI file (format 0 or 1). **Import makes**
  picks which files: both (the usual case: the game needs both), FM only or MT-32 only.
  **MT-32 programs**: a MIDI file made for General MIDI goes through the editor's General MIDI
  → MT-32 map (each instrument to the MT-32 preset with the closest name); **Keep the program
  numbers** is for a file made for the MT-32; or give **a second MIDI file** for the MT-32
  version.
  - Note-ons and note-offs become notes with durations; tempo changes are followed.
  - Drums are on channel 10. The FM file can only use the drum keys `UW.AD` has (24 in UW1,
    26 in UW2). **Replace drums UW.AD lacks** plays the nearest drum it has instead (the
    report lists them); unchecked, such a theme is refused.
  - The game plays channels 2 to 10 only. **Move channels 1 and 11–16 to free ones** moves
    them to unused channels from 2 to 9.
  - Loops: `loopStart`/`loopEnd` markers (a number after `loopStart` plays the loop that many times)
    become the game's FOR/NEXT loops. **Controller 111 starts a loop** reads RPG Maker's loop
    mark. **Loop the whole theme** loops everything endlessly. Most themes need no loop: the
    game restarts or replaces a theme when it ends.
  - Bank selects for General MIDI (controllers 0 and 32) are dropped; controller 114 selects
    the game's timbre bank. Track names and other text are left out to save room.

  The report shows what the conversion changed and the checks. A theme with an error is
  refused and nothing changes. Otherwise the theme is marked changed; nothing is written until
  you save.
- **Revert to the original** puts back both files of the selected theme as the game shipped
  them (from the `.ORIG` copies the editor keeps), when you save.

The checks follow the game and its sound drivers. Errors:

- **Too big.** UW1 reads a theme into 6,300 bytes, UW2 into 9,000. The game does not check the
  length: a bigger file overwrites memory.
- **A missing timbre.** Every timbre a theme lists must be in the timbre library: `UW.AD` for
  the FM file (UW2 also `UW.OPL`, the same file), `UW.MT` for the MT-32 file. If one is
  missing, the game turns the music off for the rest of the session. The FM file can use the
  128 programs of bank 0 and the drum keys of bank 127. The MT-32 file uses the MT-32's own
  128 presets (bank 0), as all the shipped themes do; `UW.MT` holds the effects' timbres.
- **More than 32 notes at once.** The game's sequencer holds 32 sounding notes; the next one
  takes the first one's place and that note never stops.
- **Loops nested more than 4 deep**, when a loop end then jumps back to the wrong place.
- **An endless loop in a theme the game waits for** (the victory theme; UW1 theme 11), and a
  missing end.

Warnings and notes: notes on channels the game does not play; programs or drum keys missing
from the timbre list; MT-32 drum keys outside 35–76. The sound effects borrow channels from
the music: up to 4 in UW1, 3 in UW2, each time the one of channels 2–9 with the fewest notes
sounding (the highest on a tie). While an effect plays, the music's notes on its channel are
dropped. A theme that uses at most 4 (UW1) or 5 (UW2) of channels 2–9 leaves them free. FM
cards have 9 voices (18 on an OPL3), shared with the effects.

UW1 theme 11 is never played: no call in the game asks for it.
