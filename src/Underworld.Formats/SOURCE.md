# Where this code comes from

A copy of `Assets/Scripts/Formats` from KarlClinckspoor/UltimaUnderworldEditorUnity, branch
`modding`, commit `df9d09e` (2026-10-31), without the Unity `.meta` and `.asmdef` files.

Fix bugs in both places, or move this folder back to one shared source (see PLAN.md §3) once
the two editors settle. The reference for what the files mean is
abedegno/underworld-exhumed (`uw1/src`, `uw2/src`, `docs/FORMATS.md`).

Changes made here since the copy (to carry back to the Unity editor):
- `Games/PlayerDat.cs`: `WithRest(byte[])`, the record with a new inventory workspace (the free
  editor's inventory editor changes its length).
