# Native "CO-OP OPTIONS" menu — design

Goal: replace the IMGUI F8 window (`src/olcoop/CoopMenu.cs`) with a native Overload menu screen,
reached from OPTIONS (main menu and pause menu), directly under "MULTIPLAYER OPTIONS".

Paths below are relative to `refs/Assembly-CSharp/Overload/` (game) and `/home/claude/olmod/GameMod/` (olmod).
Items marked **UNVERIFIED** were reasoned from the source, not tested in game.

---

## 1. How the stock Options menu works

### 1.1 State machine
- `enum MenuState` (`MenuState.cs:3-80`): 75 values, `NONE`=0 … `OPTIONS`=9, `MP_OPTIONS`=15, `MP_QC_OPTIONS`=16,
  `PAUSE_MENU`=23, last is `GAMEON`=74.
- `enum UIElementType`: `NUM_UIELEMENT_TYPES`=89 is the last value. Stock types include `OPTIONS_MENU`, `MP_OPTIONS` and `PAUSE_MENU`.
- `enum MenuSubState`: INIT / ACTIVE / BACK / … (used per screen).
- `MenuManager` fields:
  - `public static MenuState m_menu_state` (:209)
  - `public static MenuSubState m_menu_sub_state` (:213)
  - `public static int m_menu_micro_state` (:215)
  - `private static float m_menu_state_timer` (:217)
  - `private static Stack<MenuState> m_back_stack` (:223)
  - `public static bool option_dir` (:391)
  - `public static bool m_game_paused` (:423)
- `public static void MenuManager.Update()` (:816) bumps `m_menu_state_timer`, then a `switch (m_menu_state)`. That switch
  has **no `default:` case**, so an unknown state number does nothing. Relevant cases:
  `OPTIONS → OptionsUpdate()` (:893), `MP_OPTIONS → MpOptionsUpdate()` (:908), `PAUSE_MENU → PausedUpdate()` (:878).
- `public static void ChangeMenuState(MenuState new_state, bool dont_save_back = false)` (:1157) pushes the current state onto
  `m_back_stack`, sets the next state, sets sub-state INIT and resets the timer to 0.
- `private static void GoBack()` (:1170) pops the stack and calls `ChangeMenuState(popped, dont_save_back:true)`. **It is private.**
- `public static void SetDefaultSelection(int)` (:1186), `PlayCycleSound(float vol=1, float cycle_dir=1)` (:1210),
  `PlaySelectSound(float vol=1)` (:1218), `UpdateMPStatus()` (:7718), `GetToggleSetting(int)` (:10268).
- Global shortcut (:830): when **not** `GameplayManager.IsMultiplayer`, pressing PAUSE (or Shift+Back) in any sub-menu whose back
  stack contains `PAUSE_MENU` or `MAIN_MENU` jumps to `EXIT_ALL`. Our state gets this behaviour for free.

### 1.2 Pattern for each screen (`OptionsUpdate`, MenuManager.cs:4826-4925)
```
UpdateMPStatus(); UIManager.MouseSelectUpdate();
INIT:   if (m_menu_state_timer > 0.25f) { UIManager.CreateUIElement(UIManager.SCREEN_CENTER, 7000, UIElementType.OPTIONS_MENU);
                                          m_menu_sub_state = ACTIVE; SetDefaultSelection(0); }
ACTIVE: UIManager.ControllerMenu();
        if (!UIManager.PushedSelect(100) && (!option_dir || !UIManager.PushedDir())) break;
        MaybeReverseOption();            // ids 500..999 = "left arrow" of option item → id-500 and m_select_dir = -1
        switch (UIManager.m_menu_selection) { case 0..9: ChangeMenuState(X); UIManager.DestroyAll(); PlaySelectSound(); ...
                                               case 100: m_menu_sub_state = BACK; DestroyAll(); ... }
BACK:   if (timer > 0.25f) GoBack();
```
Option ids already used in OPTIONS: 0,1,2,3,4,6(+506),7 (MULTIPLAYER OPTIONS),8 (Tobii),9 (cockpit),100 (BACK).
**Free ids: 5, 10+.** The plan below uses **10**.

`MpOptionsUpdate` (:8636-8703) handles BACK directly with `GoBack(); UIManager.DestroyAll(); PlaySelectSound();` and calls
`UnReverseOption()` after the switch. That is the simpler template we copy.

### 1.3 Drawing (`UIElement`)
- `public void Draw()` (UIElement.cs:422) returns at once if `m_alpha <= 0`, then runs `switch (m_type)`. That switch has no default.
  `OPTIONS_MENU → DrawOptionsMenu()` (:495), `MP_OPTIONS → DrawMpOptions()` (:652).
- `public void DrawOptionsMenu()` (:2724-2758):
  - `position = m_position; position.y -= 186f;` (another −31 when `XRDevice.isPresent`).
  - Items are 62 px apart, in this order:
    1. CONTROL OPTIONS (id 0)
    2. GRAPHICS OPTIONS (1)
    3. SOUND OPTIONS (2)
    4. COCKPIT & HUD OPTIONS (3)
    5. **"MULTIPLAYER OPTIONS" (id 7, 5th item, line 2745)**
    6. LANGUAGE (6, string option)
    7. COCKPIT / COCKPIT (VR) (9)
    8. TOBII EYE TRACKING OPTIONS (8, optional)
  - Separators sit at ±40. BACK (100) is at `UI_BOTTOM - 30`. Then `MaybeShowMpStatus()`.
  - The Options screen itself has no tooltip line.
- `DrawMpOptions()` (:9254-9280) is the template for an options screen with help text: `ToolTipActive = false;` at the start,
  then items, then `DrawMenuToolTip(position + Vector2.up * 40f)` under the bottom separator.
- Helper signatures (UIElement.cs):
  - `void DrawMenuBG()` (:2760)
  - `void DrawHeaderMedium(Vector2 pos, string s, …)`
  - `void DrawMenuSeparator(Vector2 pos)` (:1216)
  - `void DrawLabelSmall(Vector2 pos, string s, float w=200, float h=24, float alpha=1)` (:1127)
  - `void DrawStringSmall(string s, Vector2 pos, float scale, StringOffset so, Color c1, float alpha_mod, float max_width=-1)` (:6297)
  - `void SelectAndDrawItem(string s, Vector2 pos, int selection, bool fade, float width_scale=1, float text_size=0.75f)` (:5482):
    sets `option_dir=false` when highlighted.
  - `void SelectAndDrawStringOptionItem(string s, Vector2 pos, int selection, string s2, string tool_tip="", float width_scale=1.5f, bool fade=false)` (:5628):
    - draws the `< value >` arrows; the left arrow is selection id+500.
    - sets `option_dir=true` when highlighted.
    - **when highlighted and `tool_tip != ""` it sets `ToolTipActive/ToolTipTitle/ToolTipDescription`.** This is the native description mechanism.
  - `void SelectAndDrawCheckboxItem(string s, Vector2 pos, int selection, bool check, bool fade=false, float width=1, int icon=-1)` (:10868):
    native checkbox. It does not set a tooltip.
  - `void SelectAndDrawSliderItem(string s, Vector2 pos, int selection, float amt)` (:5710): 0..1 slider. olmod has its own wrapper.
  - `public void DrawMenuToolTip(Vector2 pos, float offset=15f)` (:2781): if `ToolTipActive`, draws a bar with
    `"[" + ToolTipTitle + "] - " + ToolTipDescription`.
  - Static fields: `ToolTipActive` (:107), `ToolTipTitle` (:109), `ToolTipDescription` (:111). Difficulty select (:2671-2707)
    sets them by hand for the highlighted item, which is the pattern we copy for the checkbox rows.
  - `MaybeShowMpStatus()` (:9197). `m_position`, `m_alpha`, `m_type` are public fields.
- **fade=true** skips `TestMouseInRect`, so the item is **not added to the selection list**. Mouse and controller then skip it,
  and it is drawn dimmed. This is the native "greyed / read-only" look.

### 1.4 Selection / input (`UIManager`)
- `public static int m_menu_selection` (:262) and `m_select_dir` (:302, ±1).
- `MouseSelectUpdate()`, `ControllerMenu()` (:8038) move the selection over the list built by `TestMouseInRect` → `AddToSelectionList`.
- `PushedDir()` (:7072) handles left/right on option items.
- `PushedSelect(int back_selection=-1)` (:7106): MENU_SELECT, or MENU_BACK, which sets the selection to `back_selection`.
- `SliderMouseDown()` (:7123).
- `CreateUIElement(Vector2 pos, int sort, UIElementType ut)` (:7419), `DestroyAll(bool instant=false)` (:7533).

### 1.5 Pause menu → Options
- `GameManager.OpenPauseMenu()` (GameManager.cs:1146) → `DoPauseGameplay()` (:1053). If `m_gameplay_state==PLAYING && !dying`
  it calls `SwitchToMenu(PAUSE_MENU)`.
- PAUSE is read in `PlayerShip.UpdateReadImmediateControls` (PlayerShip.cs:4661):
  `JustPressed(PAUSE) && (!IsMultiplayerActive || NetworkMatch.GetMatchState() != POSTGAME) && !m_dying && !m_dead`.
- `PausedUpdate()` (MenuManager.cs:6090): `ResetBackStack()`, `m_game_paused=true`; selection **1 → `ChangeMenuState(OPTIONS)`**.
- `DrawPauseMenu()` (UIElement.cs:3755) always draws RESUME(0) and **OPTIONS(1)** (:3800). It also draws them when
  `GameplayManager.IsMultiplayer`; only STATS/SAVE etc. are hidden then.
- In olcoop co-op, `GameplayManager.IsMultiplayer` is **false** (`m_game_type` stays campaign) and `IsMultiplayerActive` is true.
  So the single-player pause menu is shown, and **OPTIONS is reachable**.
- `GameplayManager.GamePaused` (GameplayManager.cs:652) is false while `IsMultiplayerActive`, so the simulation keeps running behind
  the menu. That is fine.
- From OPTIONS, BACK pops to PAUSE_MENU as normal. **Nothing has to be added to the pause menu.**
- **UNVERIFIED:** the `NetworkMatch.GetMatchState() != POSTGAME` check during co-op. If olcoop ever leaves the match state at
  POSTGAME, the pause key is ignored. Check this once in game.
- Pause cannot be opened while dead or dying, which is stock behaviour.
- The SP pause menu items (VIEW STATS, RESTART LEVEL, SAVE/LOAD) are visible in co-op. That is out of scope here.

---

## 2. How olmod adds menus

- **Custom state numbers** (cast ints, no enum changes):
  - Menus.cs:15-25: `msAutoSelect=(MenuState)77`, `msAxisCurveEditor=78`, `msChangeTeam=80`, `uiAutoSelect=(UIElementType)91`,
    `uiAxisCurveEditor=92`. Commented out / reserved: 75, 76, 79 and 89, 90, 93.
  - MPServerBrowser.cs:22-23: `msServerBrowser=(MenuState)75`, `uiServerBrowser=(UIElementType)89`.
  - **olmod occupies MenuState 75-80 and UIElementType 89-93.** No other casts exist in olmod (grepped).
- **Update hook**: Harmony **Postfix on `MenuManager.Update`** that runs its own update when `m_menu_state == custom`.
  The private timer is reached with `ref float ___m_menu_state_timer` (MPAutoSelectionUI.cs:84-91). Example update body at :95-115:
  INIT → `CreateUIElement(SCREEN_CENTER, 7000, uiAutoSelect)` …
- **Draw hook**: **Postfix on `UIElement.Draw`** that checks `__instance.m_type == custom` (MPAutoSelectionUI.cs:437-444;
  MPServerBrowser.cs:487-495 also checks `m_alpha > 0`).
- **Entering the custom state from an existing menu**: Postfix on the existing `*Update` that re-tests
  `UIManager.PushedSelect(100) || (option_dir && PushedDir())` and switches on a new id (MPAutoSelectionUI.cs:63-80, id 121 in
  ControlsOptionsUpdate). New items are drawn by patching the draw method: either a Prefix that fully replaces it (`DrawMpOptions`,
  Menus.cs:798-…, returns false) or transpilers (Menus.cs SoundOptions :1641-1704, Controls :2616-2740).
- **Conflicts**:
  - olmod does **not** patch `DrawOptionsMenu` or `OptionsUpdate`. Grepped for those names, `OPTIONS_MENU` and `MenuState.OPTIONS`: no hits.
  - olmod fully replaces `DrawMpOptions` (Prefix returning false). We must not touch MP_OPTIONS.
  - We must avoid MenuState 75-80 and UIElementType 89-93.
  - Item id 10 in OPTIONS is unused by both stock and olmod.

---

## 3. Implementation plan for olcoop

### 3.1 Numbers
- `MenuState  msCoopOptions = (MenuState)120;` (far above stock 0-74 and olmod 75-80)
- `UIElementType uiCoopOptions = (UIElementType)120;` (stock 0-89, olmod 89-93)
- OPTIONS item id: **10**.
- Ids inside the co-op screen:

| id | item |
|---|---|
| 0 | RESPAWN |
| 1 | SPECTATE |
| 2 | HARDCORE |
| 3 | RESPAWN COOLDOWN (string option; arrow id 503) |
| 100 | BACK |

### 3.2 Patches
1. **`UIElement.DrawOptionsMenu` – Transpiler**
   - Find the `ldstr "MULTIPLAYER OPTIONS"`, then the next `call UIElement::SelectAndDrawItem`.
   - Right after that call, insert: `ldarg.0; ldloca <position>; call CoopOptionsMenu.DrawOptionsEntry(UIElement, ref Vector2)`.
     The helper does `pos.y += 62; SelectAndDrawItem("CO-OP OPTIONS", pos, 10, false)`. All following items move down by 62 automatically.
   - Also change the first `ldc.r4 186f` to `217f` (moves the list up by half a row), so the bottom separator does not collide
     with BACK when Tobii is present.
   - The `position` local is taken from the `ldloc*` that pushes the position argument just before `ldc.i4.7`.
   - Fallback if the pattern isn't found: log and leave the menu untouched. Item id 10 then simply never appears.
   - *Alternative:* a Prefix that copies all of DrawOptionsMenu. That needs `using Tobii.Gaming` (Assembly-CSharp-firstpass) and
     drifts from other mods, so the transpiler is preferred.
2. **`MenuManager.OptionsUpdate` – Postfix**: if `m_menu_sub_state == ACTIVE && UIManager.PushedSelect(-1) && m_menu_selection == 10`,
   run `ChangeMenuState(msCoopOptions); UIManager.DestroyAll(); PlaySelectSound();`.
   - Don't pass 100 to PushedSelect here, because that would rewrite the selection on Back.
   - The stock switch does nothing for id 10.
3. **`MenuManager.Update` – Postfix** (`ref float ___m_menu_state_timer`): `if (m_menu_state == msCoopOptions) CoopOptionsUpdate(ref timer)`.
4. **`UIElement.Draw` – Postfix**: `if (__instance.m_type == uiCoopOptions && __instance.m_alpha > 0) DrawCoopOptions(__instance)`.
5. **BACK**: call private `MenuManager.GoBack` through a cached `AccessTools.Method` delegate, as `MpOptionsUpdate` does.
   It pops back to OPTIONS, which then pops back to MAIN_MENU or PAUSE_MENU.

### 3.3 Behaviour
- **Who can edit**: anyone except a connected joiner, i.e. `!(CoopConfig.IsJoiner && CoopSettings.FromHost)`. Simplest:
  `bool ro = CoopConfig.IsJoiner;`
- **Read-only (joiner)**:
  - All four items are drawn with `fade:true`, so they are dimmed and not selectable.
  - A label "SET BY HOST" / "SETTINGS FROM HOST NOT RECEIVED YET" is shown.
  - The default selection is 100 (BACK).
  - The checkboxes still show the host's mode, because `CoopSettings` is updated by msg 172.
- **Mode rows**: `SelectAndDrawCheckboxItem(label, pos, id, CoopSettings.Mode == m, fade:ro)`. The check mark shows the current mode.
  Description text is set by hand when `m_menu_selection == id` (pattern from DrawDifficultySelect), then
  `DrawMenuToolTip(pos + up*40)` under the bottom separator.
- **Cooldown**: `SelectAndDrawStringOptionItem("RESPAWN COOLDOWN", pos, 3, delay + " SEC", tooltip, 1.5f, fade: ro || Mode != Respawn)`.
  Left/right arrows or select move it by 5 s (3 → 5 → 10 … 60, clamped). It is greyed when the mode isn't Respawn.
- **On change (host)**: `CoopSettings.Save()` plus a broadcast to joiners.
  - `CoopMenu.Broadcast()` is currently `static void` (private) in CoopMenu.cs:118. Move it to `CoopSettings.Broadcast()` (public)
    when deleting CoopMenu.
  - Broadcasting on every left/right press is fine because the message is tiny. Optionally broadcast only on leaving the screen.
- **Description strings**:
  - RESPAWN — "DEAD PLAYERS RESPAWN AFTER THE COOLDOWN WHILE A TEAMMATE IS ALIVE"
  - SPECTATE — "DEAD PLAYERS WATCH A TEAMMATE UNTIL THE LEVEL ENDS"
  - HARDCORE — "IF ANY PLAYER DIES THE LEVEL RESTARTS FOR EVERYONE"
  - RESPAWN COOLDOWN — "SECONDS BEFORE A DEAD PLAYER RESPAWNS (RESPAWN MODE ONLY)"
- **Remove** the F8 IMGUI `CoopMenu` MonoBehaviour and its creation.

### 3.4 Sample code (C# 7.2, net35; UNVERIFIED — not compiled)
```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Overload;
using UnityEngine;
using OlCoop.Death;

namespace OlCoop.UI
{
    public static class CoopOptionsMenu
    {
        public static readonly MenuState msCoopOptions = (MenuState)120;
        public static readonly UIElementType uiCoopOptions = (UIElementType)120;
        public const int OptionsItemId = 10;

        static readonly MethodInfo s_goBack = AccessTools.Method(typeof(MenuManager), "GoBack");
        static void GoBack() { s_goBack.Invoke(null, null); }

        static bool ReadOnly { get { return CoopConfig.IsJoiner; } }

        static readonly string[] Labels = { "RESPAWN", "SPECTATE", "HARDCORE" };
        static readonly DeathMode[] Modes = { DeathMode.Respawn, DeathMode.Spectate, DeathMode.Hardcore };
        static readonly string[] Descs = {
            "DEAD PLAYERS RESPAWN AFTER THE COOLDOWN WHILE A TEAMMATE IS ALIVE",
            "DEAD PLAYERS WATCH A TEAMMATE UNTIL THE LEVEL ENDS",
            "IF ANY PLAYER DIES THE LEVEL RESTARTS FOR EVERYONE" };
        const string CooldownDesc = "SECONDS BEFORE A DEAD PLAYER RESPAWNS (RESPAWN MODE ONLY)";

        // ---- called from the DrawOptionsMenu transpiler, right after "MULTIPLAYER OPTIONS"
        public static void DrawOptionsEntry(UIElement uie, ref Vector2 position)
        {
            position.y += 62f;
            uie.SelectAndDrawItem(Loc.LS("CO-OP OPTIONS"), position, OptionsItemId, false);
        }

        // ---- the screen
        public static void Draw(UIElement uie)
        {
            UIManager.X_SCALE = 0.35f;
            UIManager.ui_bg_dark = true;
            uie.DrawMenuBG();
            UIElement.ToolTipActive = false;
            uie.DrawHeaderMedium(Vector2.up * (UIManager.UI_TOP + 20f), Loc.LS("CO-OP OPTIONS"));
            Vector2 position = uie.m_position;
            position.y -= 155f;
            bool ro = ReadOnly;
            if (ro)
                uie.DrawLabelSmall(position - Vector2.up * 80f,
                    CoopSettings.FromHost ? "SET BY HOST" : "SETTINGS FROM HOST NOT RECEIVED YET", 300f);
            uie.DrawMenuSeparator(position - Vector2.up * 40f);
            for (int i = 0; i < 3; i++)
            {
                uie.SelectAndDrawCheckboxItem(Labels[i], position, i, CoopSettings.Mode == Modes[i], ro, 1f);
                if (UIManager.m_menu_selection == i)
                {
                    UIElement.ToolTipActive = true;
                    UIElement.ToolTipTitle = Labels[i];
                    UIElement.ToolTipDescription = Descs[i];
                }
                position.y += 62f;
            }
            bool cdFade = ro || CoopSettings.Mode != DeathMode.Respawn;
            uie.SelectAndDrawStringOptionItem("RESPAWN COOLDOWN", position, 3,
                CoopSettings.RespawnDelay.ToString("0") + " SEC", CooldownDesc, 1.5f, cdFade);
            uie.DrawMenuSeparator(position + Vector2.up * 40f);
            uie.DrawMenuToolTip(position + Vector2.up * 40f);
            position.y = UIManager.UI_BOTTOM - 30f;
            uie.SelectAndDrawItem(Loc.LS("BACK"), position, 100, false);
            uie.MaybeShowMpStatus();
        }

        static void StepDelay(int dir)
        {
            float d = CoopSettings.RespawnDelay;
            // 3 -> 5 -> 10 ... 60 ; 60 -> 55 ... 5 -> 3
            if (dir > 0) d = d < 5f ? 5f : d + 5f;
            else d = d <= 5f ? CoopSettings.MinDelay : d - 5f;
            CoopSettings.RespawnDelay = Mathf.Clamp(Mathf.Round(d), CoopSettings.MinDelay, CoopSettings.MaxDelay);
        }

        static void Changed()
        {
            CoopSettings.Save();
            CoopSettings.Broadcast();   // move CoopMenu.Broadcast() here and make it public
        }

        public static void Update(ref float timer)
        {
            MenuManager.UpdateMPStatus();
            UIManager.MouseSelectUpdate();
            switch (MenuManager.m_menu_sub_state)
            {
                case MenuSubState.INIT:
                    if (timer > 0.25f)
                    {
                        UIManager.CreateUIElement(UIManager.SCREEN_CENTER, 7000, uiCoopOptions);
                        MenuManager.m_menu_sub_state = MenuSubState.ACTIVE;
                        MenuManager.SetDefaultSelection(ReadOnly ? 100 : (int)IndexOf(CoopSettings.Mode));
                    }
                    break;
                case MenuSubState.ACTIVE:
                    UIManager.ControllerMenu();
                    if (!UIManager.PushedSelect(100) && (!MenuManager.option_dir || !UIManager.PushedDir()))
                        break;
                    MenuManager.MaybeReverseOption();
                    int sel = UIManager.m_menu_selection;
                    if (sel == 100)
                    {
                        GoBack();
                        UIManager.DestroyAll();
                        MenuManager.PlaySelectSound();
                    }
                    else if (!ReadOnly && sel >= 0 && sel <= 2)
                    {
                        if (CoopSettings.Mode != Modes[sel]) { CoopSettings.Mode = Modes[sel]; Changed(); }
                        MenuManager.PlaySelectSound();
                    }
                    else if (!ReadOnly && sel == 3 && CoopSettings.Mode == DeathMode.Respawn)
                    {
                        StepDelay(UIManager.m_select_dir);
                        Changed();
                        MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
                    }
                    MenuManager.UnReverseOption();
                    break;
            }
        }

        static int IndexOf(DeathMode m) { for (int i = 0; i < 3; i++) if (Modes[i] == m) return i; return 0; }
    }

    // 1) add the entry under MULTIPLAYER OPTIONS
    [HarmonyPatch(typeof(UIElement), "DrawOptionsMenu")]
    static class Patch_DrawOptionsMenu
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instrs)
        {
            var codes = new List<CodeInstruction>(instrs);
            MethodInfo sadi = AccessTools.Method(typeof(UIElement), "SelectAndDrawItem");
            MethodInfo hook = AccessTools.Method(typeof(CoopOptionsMenu), "DrawOptionsEntry");
            bool movedTop = false, done = false;
            for (int i = 0; i < codes.Count && !done; i++)
            {
                if (!movedTop && codes[i].opcode == OpCodes.Ldc_R4 && (float)codes[i].operand == 186f)
                { codes[i].operand = 217f; movedTop = true; }

                if (codes[i].opcode == OpCodes.Ldstr && (string)codes[i].operand == "MULTIPLAYER OPTIONS")
                {
                    int posLocal = -1, call = -1;
                    for (int j = i + 1; j < codes.Count && j < i + 12; j++)
                    {
                        if (posLocal < 0) posLocal = LdlocIndex(codes[j]);
                        if (codes[j].opcode == OpCodes.Call || codes[j].opcode == OpCodes.Callvirt)
                            if (codes[j].operand as MethodInfo == sadi) { call = j; break; }
                    }
                    if (posLocal < 0 || call < 0) break;
                    codes.InsertRange(call + 1, new[] {
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldloca_S, (byte)posLocal),   // UNVERIFIED: Harmony accepts byte/int operand
                        new CodeInstruction(OpCodes.Call, hook) });
                    done = true;
                }
            }
            if (!done) Debug.Log("[olcoop] DrawOptionsMenu transpiler: pattern not found, CO-OP OPTIONS not added");
            return codes;
        }

        static int LdlocIndex(CodeInstruction c)
        {
            if (c.opcode == OpCodes.Ldloc_0) return 0;
            if (c.opcode == OpCodes.Ldloc_1) return 1;
            if (c.opcode == OpCodes.Ldloc_2) return 2;
            if (c.opcode == OpCodes.Ldloc_3) return 3;
            if (c.opcode == OpCodes.Ldloc_S || c.opcode == OpCodes.Ldloc)
            {
                var lb = c.operand as LocalBuilder;
                if (lb != null) return lb.LocalIndex;
                if (c.operand is byte) return (byte)c.operand;
                if (c.operand is int) return (int)c.operand;
            }
            return -1;
        }
    }

    // 2) handle selecting the entry
    [HarmonyPatch(typeof(MenuManager), "OptionsUpdate")]
    static class Patch_OptionsUpdate
    {
        static void Postfix()
        {
            if (MenuManager.m_menu_state != MenuState.OPTIONS || MenuManager.m_menu_sub_state != MenuSubState.ACTIVE) return;
            if (UIManager.PushedSelect(-1) && UIManager.m_menu_selection == CoopOptionsMenu.OptionsItemId)
            {
                MenuManager.ChangeMenuState(CoopOptionsMenu.msCoopOptions);
                UIManager.DestroyAll();
                MenuManager.PlaySelectSound();
            }
        }
    }

    // 3) run our state
    [HarmonyPatch(typeof(MenuManager), "Update")]
    static class Patch_MenuUpdate
    {
        static void Postfix(ref float ___m_menu_state_timer)
        {
            if (MenuManager.m_menu_state == CoopOptionsMenu.msCoopOptions)
                CoopOptionsMenu.Update(ref ___m_menu_state_timer);
        }
    }

    // 4) draw our element
    [HarmonyPatch(typeof(UIElement), "Draw")]
    static class Patch_UIElementDraw
    {
        static void Postfix(UIElement __instance)
        {
            if (__instance.m_type == CoopOptionsMenu.uiCoopOptions && __instance.m_alpha > 0f)
                CoopOptionsMenu.Draw(__instance);
        }
    }
}
```

### 3.5 Things to check / UNVERIFIED
- `MenuManager.MaybeReverseOption`/`UnReverseOption` are `public static` (MenuManager.cs:4927, :4941). Verified in source.
- `UIElement.DrawHeaderMedium` visibility and exact overloads: assumed public (olmod calls it from outside, Menus.cs:809).
- `X_SCALE`/`ui_bg_dark` are public static on UIManager (olmod sets them). The background width 0.35 matches OPTIONS.
- The IL shape of `DrawOptionsMenu` (`ldloc position` follows `call Loc.LS`; `186f` is the first `ldc.r4 186`) is inferred from the
  decompiled C#. Dump it once with Harmony debug or monodis to confirm.
- `Ldloca_S` with an int/byte operand in Harmony 2 (`lib/0Harmony.dll`). Alternatively pass the `LocalBuilder` taken from the matched ldloc's operand.
- Pause key in co-op depends on `NetworkMatch.GetMatchState() != POSTGAME` (PlayerShip.cs:4661).
- Joiner flow: `CoopSettings.FromHost` is set by the msg 172 handler (Phase3Death.cs:359). The screen just re-reads the statics every frame.
- `Loc.LS("CO-OP OPTIONS")` returns the key unchanged for unknown strings (assumed; olmod relies on this).
