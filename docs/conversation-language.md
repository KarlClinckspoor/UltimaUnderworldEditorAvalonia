# The conversation language

The Dialogue tab of the conversation editor shows each conversation in this language. It is
rebuilt from the bytecode (`src/UwEditor.Core/Conversations/StructuredFront.cs`,
`StructuredBuild.cs`, `StructuredPrint.cs`), so it is what the decompiler can say about the
code; it is also meant to be the language a compiler turns back into bytecode through the
assembler (`Assembly.cs`). This file defines it. Everything the Dialogue tab prints is
written in it, with nothing left out: constants passed to built-ins are shown in the call,
and each menu's choices are written out.

The machine it runs on is the conversation machine of the game, `conv/BABL.C` (babl_run),
with the built-in functions of `conv/CONVERSE.C`, `conv/BABLHACK.C` and `conv/BARTER.C`
(the decompiled UW1 source). Words are 16 bits, signed.

## A conversation

```
// 34 variables in memory (imports and private globals), string block 0x0E01

main() {
    global33 = 0;
    hast_thou_come_to();
    end_conversation();
}

npc_not_hostile() {    // 1 local    // never called
    int local1;
    if ((npc_goal == 5 || npc_goal == 6 || npc_goal == 9) && npc_gtarg == 1 || npc_attitude == 0) {
        return 0;
    } else {
        return 1;
    }
}

hast_thou_come_to() {    // 22 locals
    if (met_before == 1) {
        hast_thou_returned_to();
    } else {
        global0 = 0;
        say "Hast thou come to add to my torment?" #1;
        menu {
            "Of what torment dost thou speak?" #2:
                why_the_torment_of();
            "Of course not.  If I may, I shall ease thy pain." #3:
                well_i_am_beyond();
            "If my presence offends thee, I shall go." #4:
                wait_my_cause_is();
            "Indeed I have, thou mewling child!" #5:
                flee_and_end();
        }
    }
}
```

A conversation is its functions, in the order of their code. The first, `main`, starts at
word 0 and runs when the conversation starts. The first line is a comment giving the
conversation's variable count and string block (from its header).

## Words

- **Names** are letters, digits and `_`, not starting with a digit. Functions, labels,
  locals and private globals have the names of the names files (`docs/conversations`, and
  the user's renames: see the manual's "Names and F2"), or placeholders: `func_02B5`,
  `L02CF`, `local22`, `arg1`, `global33`. Imported functions and variables have the game's
  names (`get_quest`, `npc_attitude`). A name may not be one of the keywords.
- **Keywords:** `if else while break continue return goto exit end say respond menu
  fmenu switch case default int true streq ptr word save_reg`.
- **Numbers** are decimal, with an optional `-` (`-1`). A number is a 16-bit word.
- **Strings** are in double quotes, with `\"`, `\\` and `\n` as escapes, and the game's
  own escapes (`@SS1`, `\\m`) as they are in STRINGS.PAK. A string stands for its number
  in the conversation's string block; `#n` after it gives the number (`"Tell me more." #7`).
  The Dialogue tab always writes the number. A compiler takes a string without one as a new
  string, or the existing string with the same text.
- **Comments** run from `//` to the end of the line. The view's comments (`// 22 locals`,
  `// never called`) carry nothing a compiler needs.

## Functions

```
name(argN, ..., arg2, arg1) {    // n locals
    int a, b[5];
    statements
}
```

- **Arguments** are addresses: a caller passes each argument as a word, and the function
  reads what it points at with `*arg1` and `arg1[i]`. They are listed in the order a caller
  writes them; `arg1` is the last one (the word just under the return address, bp - 2).
- **Locals** are declared on the first line, in the order of their slots from 1: `int
  a, b[5];` puts `a` in slot 1 and the array `b` in slots 2 to 6. The compiler's
  temporaries (below) are not declared. The comment after the brace gives the function's
  whole frame (locals and temporaries), as its prologue `PUSHI n ADDSP` reserves it.
- **Returning a value:** a function with `return e;` returns e. It keeps the value in
  local 1 (always `local1` in the shipped games: 213 UW1 and 470 UW2 functions) and leaves
  through one exit at its end that returns local 1. Running off the end of such a function
  returns local 1 as it is.
- The function ends at its closing brace; a function that does not end in `return`, `exit`
  or a jump ends with `return;`.

## Statements

| Statement | Meaning |
|---|---|
| `lvalue = e;` | stores e |
| `f(args);` | calls f and drops its result |
| `say s;` / `respond s;` | the talker says, or the player is shown to say, string s (any expression giving a string number; usually a string) |
| `if (e) { ... }` / `} else if (e) {` / `} else {` | e not 0 runs the first block |
| `while (e) { ... }` | repeats while e is not 0 |
| `while (true) { ... }` | repeats until a `break`; with `break;` as its last statement it runs once, and `continue` starts it again (see "Loops") |
| `break;` / `continue;` | leaves the innermost loop / goes back to its start |
| `return;` / `return e;` | leaves the function (with e) |
| `exit;` | ends the conversation (EXIT) |
| `end;` | main's return: the conversation ends |
| `menu { ... }`, `fmenu { ... }` | shows a menu and runs the chosen choice's statements (see "Menus") |
| `x = menu { ... };` | shows a menu and keeps the answer in x |
| `switch (e) { case k: ... default: ... }` | runs the statements of the case equal to e |
| `goto label;` / `if (e) goto label;` | jumps to `label:` in the same function |
| `label:` | a place a goto goes to (shown only where one does) |
| `word 0x0544;` | a word that is not an instruction (UW1 has two); babl_run stops there |
| `save_reg e;` | puts e in the return register without returning (SAVE_REG elsewhere than a function's exit; the games have none) |

There is no `do ... while`: the games' code has no conditional jump backwards.

### Loops

`while (e) { ... }` is the compiler's loop: the condition at the top, a jump back at the
end. Some scripts repeat a part by jumping back to its start from inside it (a menu's
choice that asks again). The view shows that as

```
while (true) {
    say "What will ye be needin' fixed, then?" #28;
    menu {
        "Oh, here it is." #29:
            continue;
        "Oh, never mind..." #30:
            farewell_then();
    }
    break;
}
```

The `break;` at the end is the view's: in the code the part simply ends there. A
`while (true)` whose last statement is `break;` has no jump back.

### Menus

```
menu {
    "Tell me more." #7:
        statements
    "Oh, is that all?" #8:
        statements
    "Nothing." #9;
    default:
        statements
}
```

The choices are listed in the order the player sees them. A choice with `:` has statements
(`{}` when there are none); a choice with `;` has none (no code tests for it). `default:`
runs when no choice with statements was taken (the games have none). `fmenu` is the
filtered menu (babl_fmenu): each choice can have a condition, `"text" #50 if (quest_4):`,
and is shown only when the condition is not 0; a choice without one is always shown.

`x = menu { "a" #1; "b" #2; }` keeps the answer in x: the choice's number from 1 for
`menu`, its string's number for `fmenu`.

What a menu does (conv/CONVERSE.C conv_choice, conv_fmenu): it fills a temporary array of
21 words with the choices' string numbers and a 0 after the last (for `fmenu`, a second
array of 21 words with the conditions, each condition stored before its string), calls
babl_menu (or babl_fmenu), keeps the answer in a temporary, and compares the answer with
each choice that has statements, in order.

### Switch

```
switch (global33) {
case 0:
    goto menu_loop;
case 1:
    goto menu_loop_2;
}
```

The value is kept in a temporary and compared with each case in order; a case's statements
end the switch. There is no falling from one case into the next.

## Expressions

From the tightest to the loosest:

| | Operators | |
|---|---|---|
| 8 | `-e` `!e` `*e` `&lvalue` | negation, not (1 when e is 0, else 0), the word at address e, an address |
| 7 | `*` `/` `%` | division and remainder by 0 give 0x7FFF (BABL_DIV) |
| 6 | `+` `-` | |
| 5 | `<` `<=` `>` `>=` | 1 or 0 |
| 4 | `==` `!=` | 1 or 0 |
| 3 | `&&` | 1 when both are not 0 |
| 2 | `\|\|` | 1 when either is not 0 |

Operators of one level group from the left (`a - b - c` is `(a - b) - c`); the view puts
parentheses where the code's order needs them. Both sides of `&&` and `||` are always
worked out: there is no short cut. Arithmetic wraps at 16 bits.

The other expressions:

- a number, a string (its number), `true` (1);
- a variable: a local, an argument (its value is an address), a global by name. An
  imported array shows its element as `play_name[2]`;
- `a[i]`: element i, counted from 1. For a local or global array, the array's i-th word;
  for an argument (or any variable holding an address), the i-th word from where it points
  (`arg1[1]` is `*arg1`). Locals are arrays when declared so;
- `streq(a, b)`: 1 when strings a and b are the same after @-substitution, else 0 (the
  STRCMP instruction; not C's strcmp, which gives 0 when they are equal);
- a call, `f(args)`: its result (a function that does not return a value leaves the last
  result there, as the register holds it).

## Calls and arguments

A script function or a built-in is called by name. Each argument is passed as an address:

- `&x`, `&a[i]`, an array's name, `&global`: that variable's address; the function can
  change it;
- an argument of the calling function (`arg1`): the address it holds, passed on;
- any other expression: its value goes into a new temporary local, and the temporary's
  address is passed. So `get_quest(32)` and `sex("lad" #16, "lass" #17)`: the built-in
  reads the 32, or the string numbers, from the temporaries.
- `ptr(e)`: e's value passed as the address (the games never need it).

A built-in's string arguments are shown as strings (`print`, `sex`, `compare`, `append`...).
The built-ins and what each argument means are those of the game (conv/CONVERSE.C,
BABLHACK.C, BARTER.C; the editor's simulator, `Simulator.cs`, follows them).

## Temporaries

The compiler gives a function a temporary local for each argument passed by value, for each
menu (21 words of choices, 21 more for `fmenu`'s conditions, and 1 for the answer), and for
each switch's value. They come after the declared locals, in the order the code uses them,
so the frame is the declared locals and then the temporaries (one UW1 function has a
declared local after a temporary). They are never declared or named in the language.

## From the language to the code (for the compiler)

How each statement is coded, as the shipped conversations have it (counts: UW1 / UW2):

| Construct | Code |
|---|---|
| function | `PUSHBP SPTOBP PUSHI n ADDSP`, the body, `BPTOSP POPBP RET` (main: `START` first, and its `RET` ends the script) |
| `x = e;` | address of x, e, `STO` |
| temporary `t = e` | e, `PUSHI_EFF t`, `SWAP`, `STO` |
| `x = f(...)` | address of x, arguments, the call, `POP`s, `PUSH_REG`, `STO` |
| built-in call | the arguments, `PUSHI count`, `CALLI n`, count + 1 `POP`s. The count is the number of arguments, except babl_menu and babl_fmenu: 0 (816 / 2309 calls, and 1167 / 1346 menus) |
| script call | the arguments, `CALL f`, one `POP` an argument |
| `return e;` | `local1 = e`, `JMP` to the exit (427 / 837); the exit is `PUSHI_EFF 1 FETCHM SAVE_REG POP` before the epilogue |
| `return;` | `JMP` to the epilogue (58 / 116; 0 / 2 use `BRA`) |
| `if (e) { A }` | e, `BEQ end`, A |
| `if (e) { A } else { B }` | e, `BEQ else`, A, `BRA end`, B (567 / 630 use `BRA`; 128 / 97 use `JMP`) |
| `while (e) { A }` | e, `BEQ end`, A, `BRA top` (101 / 60; 1 / 17 jump back with `JMP`) |
| `while (true) { A }` | A, `JMP top` (6 / 41) |
| `while (true) { A; break; }` | A, with `continue` as `JMP top` (31 / 81) |
| `break;`, `continue;`, `goto L;` | `JMP` (`goto` once with `BRA`) |
| `if (e) goto L;` | the opposite of e (a compare turned over, `!x` as x), `BEQ L` (5 / 5) |
| menu | the array stores, `PUSHI_EFF choices PUSHI 0 CALLI babl_menu POP POP PUSH_REG`, the answer stored as a temporary, then for each choice with statements: `answer == k`, `BEQ next`, the statements, `JMP end` (always `JMP`, 2771 / 3328) |
| switch | e stored as a temporary, then for each case `k == value`, `BEQ next`, the statements, `JMP end` |

A jump after a jump, a return or an exit cannot run; the compiler writes one after each
`return e;` and `goto` in an `if` with an `else` (the `BRA` over the else), and after each
statement that jumps in a menu's choice (the `JMP` to the menu's end). The view does not
show them.

### What the view does not pin down

Decompiling and compiling back gives the same bytes only where the table above has one
answer. Where it does not, the view chose the readable form, and a compiler that wants the
same bytes needs more than the text:

- `if`/`else` and `while` loops made with `JMP` instead of `BRA` (the scripts' own jumps,
  shown structured): 128 + 1 + 6 in UW1, 97 + 17 + 41 in UW2;
- jumps to where control goes anyway (a `goto` to the end of an enclosing `if`), not
  shown: 172 in UW1, 265 in UW2;
- `-n` is `PUSHI n NEG` in 269 UW1 and 107 UW2 places, `PUSHI -n` in 260 UW2 places;
- an `if` whose first block is empty is shown with the condition turned over;
- 5 UW1 temporaries that no construct explains are shown as assignments.

For these, the compiler can keep the original assembly of a function that was not edited
(it is the same bytes), and an edited function can be checked by running it in the
simulator rather than by its bytes.

### Gotos

`goto` is left where the code's jumps cannot be put as if, while, menu or switch without
repeating code: 277 in UW1, 895 in UW2. Nearly all (271 and 890) are `JMP`s, the scripts'
own jumps between labelled parts: a menu's choice going to a later part of the
conversation (`goto end_of_menu_7;`) or back to an earlier one (`goto menu_loop_2;`).
UW1's conversation 4 (`why_you_here`) holds many: its parts are reached by a switch of
gotos on a global. Every `BRA`, the compiler's own jump, is structured but one.
