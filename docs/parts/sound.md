### Sound and music

**Tools ▸ Sound and music…** plays, exports and replaces the games' digitised sound and edits
the effects list. The files are in the game folder's `SOUND` folder; a saved game uses the
same ones. Nothing is written until **Save**; each file written keeps `NAME.EXT.BKP`, and
the first version `NAME.EXT.ORIG`. Closing the window with unsaved files asks first.

**Play** plays a file as it is now, unsaved imports too; **Stop** stops it. Sound goes out
through OpenAL. Without a sound device the status line says so and nothing else happens.
**Export WAV…** writes the sound as an 8-bit mono WAV file at the file's rate. **Import
WAV…** replaces it. **Revert to the original** goes back to the file as shipped (its `.ORIG`
copy), or removes a file the editor added; an unsaved import is simply dropped.

**Importing.** Any WAV file is read: 8, 16, 24 or 32-bit PCM or 32-bit float, any rate,
mono or stereo. It is mixed to mono, its silent start and end are cut (**Trim silence**),
it is resampled (a windowed-sinc filter), raised to full volume (**Normalise**) and
dithered down to 8 bits. It is written the way the game reads it: one plain block of 8-bit
samples. The game skips the file's first 32 bytes and plays the rest as sound, so any other
shape would click. **Import at** sets the rate: the game's own (12048 Hz in Underworld,
11111 Hz in Underworld II), or 8000 to 22050 Hz. Lower rates fit longer clips. A clip over
the file's limit is refused, or, as **Over the limit** says, shortened or written at a lower
rate so it fits; the status line gives the length in seconds.

The limits, from the games' code:

| File | Limit |
|---|---|
| Underworld `nn.VOC` (speech) | 98,304 bytes (8.2 s at 12048 Hz); the rest is never played |
| Underworld II `BSPnn.VOC` (speech) | none: it is streamed |
| Underworld II `SPnn.VOC`, `UWnn.VOC` (effects, trap sounds) | under 65,536 bytes (5.9 s at 11111 Hz) |

Underworld II's digitised effects share a cache of four 16 KB pages: a big one pushes the
others out, so keep them short.

- **Speech.** Each speech file with the cutscene and the subtitle line it goes with (STRINGS
  block 0xC00 + the cutscene), its length, rate and size against the limit. In Underworld
  all 42 files are the introduction's (cutscene 0), one a subtitle line. In Underworld II
  the eight `BSPnn.VOC` are spoken in cutscenes 4–7 and 24–27; the introduction has no
  speech. The cutscene's say command starts the file and the next one cuts it, so a new line
  should take about as long as the old. Conversations have no speech in either game.
  **Export all…** writes every file into a folder.
- **Effects** (`SOUNDS.DAT`): every effect, with what plays it in the game, taken from the
  games' code, and the creatures whose footsteps or death cry it is. Select one to change
  its patch (a timbre of bank 1 in `UW.AD` and `UW.MT`), note, volume and length (in 1/256
  seconds). Underworld has 24 effects and Underworld II 49; the games hold no more.
  Underworld's effects are all notes played on the music card (FM or MT-32), which the
  editor can't play. **Revert SOUNDS.DAT** goes back to the file as saved.
  - Underworld II: an effect with **Digital** ticked plays `SOUND\SPnn.VOC` (nn its number)
    on the Sound Blaster; the others are music-card notes. Ticking **Digital** on any effect
    makes it digitised: import its `SPnn.VOC` (until there is one, the game plays the note).
    **Priority** decides which sound keeps the digital channel when two play at once.
  - Underworld II trap sounds: effects 100 to 163 are `UW00.VOC` to `UW63.VOC`. A
    special-effects trap (0x184) with quality 2 and owner nn plays `UWnn.VOC` when it is set
    off. The list shows the ones there are, and the traps in the levels that play each (the
    game ships `UW00` and `UW01`, each played by traps in one level). **Add a trap sound
    from WAV…** makes a new `UWnn.VOC`: then put a trap with that owner in a level. New voice
    lines can be added this way, as data alone.

Make a mod (**Tools ▸ Mods**) carries the changed and new files of `SOUND` with the rest.
