## Trap recipes

**Edit ▸ Trap recipes…** makes a working trigger and trap in one step. Choose a recipe on
the left, fill in what it needs and press **Make**. Squares and objects are chosen with
**Pick in view** (then click in the main view; Esc cancels) or **Use selection** (what is
selected in the main view). Each recipe is one Edit ▸ Undo step on its level, and the
level's list check must still pass, or nothing is changed. The new trigger is selected
afterwards, so the properties panel and the triggers window show its chain.

- **Lever opens a door:** a lever, switch, button or pull chain opens, closes or toggles a
  door. Use one already in the level (one that holds no trigger yet) or put a new one on a
  wall of a tile: on the side you choose, or the first wall found. The lever holds a use
  trigger aimed at the door's square, and a door trap lies there. In Ultima Underworld the
  door trap always takes the door's lock off and puts on the lock it carries, so the
  recipe gives it a copy of the door's own lock; **Unlock the door** leaves it without one.
  In Underworld II the lock is left alone unless you unlock it.
- **Teleport:** stepping on a square sends the player to another square, on this level or
  another one (change level in the main window before picking). **And a way back** adds a
  second pair; it must start and arrive on other squares than the first, or the player
  would be sent straight back. Only the player is teleported.
- **Pressure plate:** stepping on a square runs a trap. In Underworld II it can be a
  pressure plate, which fires when weight comes onto the square; otherwise a move trigger.
- **Object trigger:** picking up, using or looking at an object runs a trap. The object
  must be a single one (not a stack, a creature or an enchanted item) and hold no trigger
  yet; it is made to hold the trigger.

The plate and the object trigger run one of these traps:
- **Damage** to whoever set it off, or poison (strength 1-15, the player only);
- **Show a message**: a new string added to STRINGS.PAK block 9, the text traps' messages.
  Save levels does not write it: save it in Tools ▸ Strings (or when the editor asks on
  closing). Undo removes the trap, not the string;
- **Set a variable**: in Ultima Underworld a game variable 1-63 or a quest bit 0-31, in
  Underworld II a numbered variable (0-255 variables, 256-383 quest flags);
- **Create an object or creature**: a copy appears on the square you choose (or the
  trigger's) each time; the template is kept inside the trap. A creature can be hostile;
- **Work a door**: open, close or toggle it, as above;
- **Remove an object** lying on its tile. Such a trigger works once.

**Works every time** makes the trigger reusable; without it the game deletes the chain
after it runs once. **Creatures set it off too** sets the trigger's enchanted bit.
