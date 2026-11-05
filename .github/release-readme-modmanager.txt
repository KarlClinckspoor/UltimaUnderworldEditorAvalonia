Ultima Underworlds Mod Manager VERSION
Installs mods (.uwmod files) into Ultima Underworld I and II: the DOS games (run in DOSBox)
and Underworld Exhumed, one mod at a time, with the original game always one click away.

Run UltimaUnderworldsModManager (UltimaUnderworldsModManager.exe on Windows), then add a game:
a DOS game folder (the one with UW.EXE or UW2.EXE and DATA), or an Underworld Exhumed program
(uw1port or uw2port). Import mods into the library, pick one for a game, then Use this and Play.
uwmod (uwmod.exe) is the same manager on the command line: uwmod help.

This is a beta: the manager keeps every original it replaces in its vault, but back up your
game folder and saved games all the same.

Linux: chmod +x UltimaUnderworldsModManager uwmod if the bit was lost.
macOS: the build is not notarised. In Terminal, in this folder:
    xattr -d com.apple.quarantine UltimaUnderworldsModManager uwmod
    ./UltimaUnderworldsModManager
Windows: SmartScreen may warn about an unknown publisher: More info > Run anyway.

UltimaHacks (built in) is by John Glassmyer, MIT licence: github.com/JohnGlassmyer/UltimaHacks.
Its licence is in Settings > UltimaHacks' licence.
Source and issues: https://github.com/KarlClinckspoor/UltimaUnderworldEditorAvalonia (ModManager)
