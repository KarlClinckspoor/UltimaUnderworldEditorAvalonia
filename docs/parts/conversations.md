<!-- The first paragraph goes at the end of "## Conversations", after the paragraph on Save. The two sections below go under Conversations. -->

Saving CNV.ARK also keeps the conversations' private globals in step. The game reads a
conversation's private globals from its record in the save's `BGLOBALS.DAT` (at most as many
words as the script has variables), and a new game makes that file from `DATA\BABGLOBS.DAT`
(conv/BABL.C `init_babl`, `bab_get_globals`). When a conversation's variable count grows, or
an empty slot gets a conversation, saving grows or adds its record in `BABGLOBS.DAT`, and in
the open save's `BGLOBALS.DAT` too (new words 0, the others kept). Records are never made
smaller. Other saved games are not changed.

### Conversation simulator

**Simulate…** in the conversation window (Ctrl+R), or **Tools ▸ Conversation simulator…**:
runs the shown conversation as the game does. It follows the conversation machine of
conv/BABL.C and the game's built-in functions (conv/CONVERSE.C, BABLHACK.C, BARTER.C).
Nothing is written to the game's files.
- **Run** (F5) runs until a menu, a question, the end, a stop address or the instruction
  limit. **Step** (F10) runs one instruction. **Reset** starts again.
- The NPC's lines, your choices and the narration (`print`) appear on the left, with the
  @ variables filled in (`@GS8` the player's name, and so on). A menu's answers are buttons;
  click one or press its number. With **Run on after a choice** ticked, the conversation goes
  on to the next menu. When the script asks for typed text (`babl_ask`), a box appears.
- **Stop at:** code addresses (hex, as the Script tab shows them) where Run stops.
  **Follow in the script** selects the current instruction in the Script tab.
- **Variables:** the values now. First the script's memory: the variables the game fills in
  (`npc_attitude`, `play_name`...) and the conversation's private globals. Then the quests,
  game variables, X clocks and skills the conversation uses (or all of them). Type a new
  value to change it; it counts from the next instruction.
- **Start-up:** what only the game knows, filled in from the open game:
  - the talker (one of the creatures that use the slot, from its level object: hit points,
    goal, attitude, home and inventory), or default values;
  - with a saved game open, the player from `PLAYER.DAT` (name, sex, skills, quests, game
    variables, X clocks, inventory) and the private globals from `BGLOBALS.DAT`. `DATA` has
    no player, so a new game's zeros are used;
  - how `random` rolls (a seed, or always one result);
  - whether the talker accepts an offer (`do_offer`) and gives in to a demand
    (`do_demand`);
  - the talker's inventory and the player's trade slots, one object a line: the item number,
    `x` and a quantity, `q` and a quality, and `selected` for an item offered.
  **Reset with these** starts again with them.
- **Log:** each instruction (if **Log every instruction** is ticked), each call of a
  built-in with its arguments and result, and each write to memory.
- At the end the status line says what the game would keep: the private globals saved in
  `BGLOBALS.DAT`, and the talker's and player's values copied back (a new attitude, a goal,
  experience).

Built-ins that act on the world outside the conversation (doors, teleports, attitudes of
other creatures, pit fights) are only noted in the log. An object of the level the simulator
was not given (index 1 is the player) gets a stand-in with every field 0. The simulator runs
the applied code: apply the Script tab's edits first.

### Conversation graph

**Graph…** in the conversation window, or **Tools ▸ Conversation graph…**: the shown
conversation's flow as a graph, read from its code. The code is cut where it jumps; each
node shows the readable view's lines of its piece of code.
- Brown nodes are where the NPC speaks, blue ones show a menu with its numbered answers,
  grey ones are other code. A node's title is its label (`L02AE`) or its function; it says
  when the conversation ends or the function returns there.
- Blue lines are a menu's answers: the answer's text over its condition (`local22 == 1`).
  Amber lines are other conditions, in the readable view's words. Dashed lines are calls of
  the script's functions. Lines going back up (loops) run round the right side. Calls of a
  function used from many places (the conversation's end, for one) are drawn only for the
  selected node.
- Code that only passes on, tests a condition or calls a function is folded into the lines
  around it. Functions never called are left out.
- Drag to move the view, the wheel to zoom; **Start** goes back to the top, **Fit** shows it
  all. **Statements** shows or hides the other statements in the nodes.
- A click on a node selects its first instruction in the Script tab and shows the node on
  the right: its strings, its code, and where it leads (click one to go there).
- Editing a string on the right edits that string of the conversation, as the Strings tab
  does: it is marked changed and saved with the conversation. **Revert conversation** undoes
  it.
