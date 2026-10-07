using System;
using System.Reflection;
using HarmonyLib;
using Overload;
using Tobii.Gaming;
using UnityEngine;
using UnityEngine.XR;
using OlCoop.Death;

namespace OlCoop.UI
{
    /// <summary>
    /// Native "CO-OP OPTIONS" screen under OPTIONS, directly below MULTIPLAYER OPTIONS (docs/menu-design.md).
    /// Reachable from the main menu and from the pause menu (Esc -> OPTIONS). Uses the game's own menu widgets,
    /// selection, keyboard/mouse/gamepad navigation and the hover description bar.
    /// </summary>
    public static class CoopOptionsMenu
    {
        public static readonly MenuState msCoopOptions = (MenuState)120;          // stock 0-74, olmod 75-80
        public static readonly UIElementType uiCoopOptions = (UIElementType)120;  // stock 0-89, olmod 89-93
        public const int OptionsItemId = 10;                                       // free id in the OPTIONS list

        const int ID_COOLDOWN = 3, ID_FF = 4, ID_NAMES = 5, ID_FLARE = 6, ID_BACK = 100;
        const string FFDesc = "WHEN CHECKED, PLAYERS CAN DAMAGE EACH OTHER WITH SHOTS, EXPLOSIONS AND RAMMING (HALF DAMAGE)";
        const string NamesDesc = "YOUR OWN SETTING: SHOW TEAMMATES' PILOT NAMES ABOVE THEIR HEALTH BARS";
        static readonly MethodInfo s_goBack = AccessTools.Method(typeof(MenuManager), "GoBack");

        static readonly string[] Labels = { "RESPAWN", "SPECTATE", "HARDCORE" };
        static readonly DeathMode[] Modes = { DeathMode.Respawn, DeathMode.Spectate, DeathMode.Hardcore };
        static readonly string[] Descs = {
            "A DEAD PLAYER RESPAWNS NEXT TO A LIVING TEAMMATE AFTER THE COOLDOWN. IF EVERYONE IS DEAD, THE LEVEL RESTARTS",
            "A DEAD PLAYER WATCHES LIVING TEAMMATES UNTIL THE LEVEL IS COMPLETED, THEN RESPAWNS AT THE START OF THE NEXT LEVEL",
            "IF ANY PLAYER DIES, THE LEVEL RESTARTS FOR EVERYONE" };
        const string CooldownDesc = "SECONDS A DEAD PLAYER WAITS BEFORE RESPAWNING (RESPAWN MODE ONLY)";

        static bool ReadOnly { get { return CoopConfig.IsJoiner; } }

        // ------------------------------------------------------------ OPTIONS screen (stock copy + our entry)
        public static void DrawOptionsMenu(UIElement uie)
        {
            UIManager.X_SCALE = 0.35f;
            UIManager.ui_bg_dark = true;
            Vector2 position = uie.m_position;
            uie.DrawMenuBG();
            uie.DrawHeaderMedium(Vector2.up * (UIManager.UI_TOP + 20f), Loc.LS("OPTIONS"));
            position.y -= 217f; // stock 186; one extra row now
            if (XRDevice.isPresent) position.y -= 31f;
            uie.DrawMenuSeparator(position - Vector2.up * 40f);
            uie.SelectAndDrawItem(Loc.LS("CONTROL OPTIONS"), position, 0, false);
            position.y += 62f;
            uie.SelectAndDrawItem(Loc.LS("GRAPHICS OPTIONS"), position, 1, false);
            position.y += 62f;
            uie.SelectAndDrawItem(Loc.LS("SOUND OPTIONS"), position, 2, false);
            position.y += 62f;
            uie.SelectAndDrawItem(Loc.LS("COCKPIT & HUD OPTIONS"), position, 3, false);
            position.y += 62f;
            uie.SelectAndDrawItem(Loc.LS("MULTIPLAYER OPTIONS"), position, 7, false);
            position.y += 62f;
            uie.SelectAndDrawItem("CO-OP OPTIONS", position, OptionsItemId, false);
            position.y += 62f;
            uie.SelectAndDrawStringOptionItem(Loc.LS("LANGUAGE"), position, 6, Loc.CurrentLanguageName, string.Empty, 1f);
            position.y += 62f;
            uie.SelectAndDrawStringOptionItem((!GameplayManager.VRActive) ? Loc.LS("COCKPIT") : Loc.LS("COCKPIT (VR)"), position, 9,
                (!GameManager.m_player_ship.IsCockpitVisible) ? Loc.LS("OFF") : Loc.LS("ON"), string.Empty, 1f);
            if (TobiiAPI.GetUserPresence() != UserPresence.Unknown)
            {
                position.y += 62f;
                uie.SelectAndDrawItem(Loc.LS("TOBII EYE TRACKING OPTIONS"), position, 8, false);
            }
            uie.DrawMenuSeparator(position + Vector2.up * 40f);
            position.y = UIManager.UI_BOTTOM - 30f;
            uie.SelectAndDrawItem(Loc.LS("BACK"), position, 100, false);
            uie.MaybeShowMpStatus();
        }

        // ------------------------------------------------------------ CO-OP OPTIONS screen
        public static void Draw(UIElement uie)
        {
            UIManager.X_SCALE = 0.35f;
            UIManager.ui_bg_dark = true;
            uie.DrawMenuBG();
            UIElement.ToolTipActive = false;
            // 0.7.14 (user): no "CO-OP OPTIONS" title line; the version line stays
            uie.DrawStringSmall(CoopVersion.Full.ToUpperInvariant() + (CoopConfig.IsHost ? " - HOST" : CoopConfig.IsJoiner ? " - JOINER" : ""),
                Vector2.up * (UIManager.UI_TOP + 52f), 0.45f, StringOffset.CENTER, UIManager.m_col_ui2, 1f, -1f);
            Vector2 position = uie.m_position;
            position.y -= 248f; // 0.6.8: half a row higher again to fit FLARE COLOR
            bool ro = ReadOnly;
            uie.DrawLabelSmall(position - Vector2.up * 80f, ro
                ? (CoopSettings.FromHost ? "WHEN A PLAYER DIES - SET BY THE HOST" : "WHEN A PLAYER DIES - WAITING FOR THE HOST'S SETTINGS")
                : "WHEN A PLAYER DIES", 400f);
            uie.DrawMenuSeparator(position - Vector2.up * 40f);
            for (int i = 0; i < 3; i++)
            {
                uie.SelectAndDrawCheckboxItem(Labels[i], position, i, CoopSettings.Mode == Modes[i], ro, 1f);
                if (UIManager.m_menu_selection == i && !ro)
                {
                    UIElement.ToolTipActive = true;
                    UIElement.ToolTipTitle = Labels[i];
                    UIElement.ToolTipDescription = Descs[i];
                }
                position.y += 62f;
            }
            bool cdFade = ro || CoopSettings.Mode != DeathMode.Respawn;
            uie.SelectAndDrawStringOptionItem("RESPAWN COOLDOWN", position, ID_COOLDOWN,
                CoopSettings.RespawnDelay.ToString("0") + " SEC", CooldownDesc, 1.5f, cdFade);
            position.y += 62f;
            uie.SelectAndDrawCheckboxItem("FRIENDLY FIRE", position, ID_FF, CoopSettings.FriendlyFire, ro, 1f);
            if (UIManager.m_menu_selection == ID_FF && !ro)
            {
                UIElement.ToolTipActive = true;
                UIElement.ToolTipTitle = "FRIENDLY FIRE";
                UIElement.ToolTipDescription = FFDesc;
            }
            position.y += 62f;
            uie.SelectAndDrawCheckboxItem("SHOW PLAYER NAMES", position, ID_NAMES, CoopSettings.ShowNames, false, 1f); // local, never read-only
            if (UIManager.m_menu_selection == ID_NAMES)
            {
                UIElement.ToolTipActive = true;
                UIElement.ToolTipTitle = "SHOW PLAYER NAMES";
                UIElement.ToolTipDescription = NamesDesc;
            }
            position.y += 62f;
            uie.SelectAndDrawStringOptionItem("FLARE COLOR", position, ID_FLARE, OlCoop.World.CoopFlares.Name(CoopSettings.FlareColor),
                "YOUR OWN SETTING: THE COLOR OF YOUR FLARES, AS EVERY PLAYER SEES THEM", 1.5f, false); // local, never read-only
            uie.DrawMenuSeparator(position + Vector2.up * 40f);
            uie.DrawMenuToolTip(position + Vector2.up * 40f);
            position.y = UIManager.UI_BOTTOM - 30f;
            uie.SelectAndDrawItem(Loc.LS("BACK"), position, ID_BACK, false);
            uie.MaybeShowMpStatus();
        }

        static void StepDelay(int dir)
        {
            float d = CoopSettings.RespawnDelay;
            if (dir > 0) d = d < 5f ? 5f : d + 5f;
            else d = d <= 5f ? CoopSettings.MinDelay : d - 5f;
            if (d > CoopSettings.MaxDelay) d = CoopSettings.MinDelay;      // wrap like stock option items
            else if (d < CoopSettings.MinDelay) d = CoopSettings.MaxDelay;
            CoopSettings.RespawnDelay = Mathf.Clamp(Mathf.Round(d), CoopSettings.MinDelay, CoopSettings.MaxDelay);
        }

        static void Changed()
        {
            CoopSettings.Save();
            CoopSettings.Broadcast();
            CoopLog.Write("SETTINGS", "changed to " + CoopSettings.Describe() + " friendlyFire=" + CoopSettings.FriendlyFire);
        }

        static int IndexOf(DeathMode m) { for (int i = 0; i < 3; i++) if (Modes[i] == m) return i; return 0; }

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
                        MenuManager.SetDefaultSelection(ReadOnly ? ID_BACK : IndexOf(CoopSettings.Mode));
                    }
                    break;
                case MenuSubState.ACTIVE:
                    UIManager.ControllerMenu();
                    if (!UIManager.PushedSelect(ID_BACK) && (!MenuManager.option_dir || !UIManager.PushedDir()))
                        break;
                    MenuManager.MaybeReverseOption();
                    int sel = UIManager.m_menu_selection;
                    if (sel == ID_BACK)
                    {
                        s_goBack.Invoke(null, null);
                        UIManager.DestroyAll();
                        MenuManager.PlaySelectSound();
                    }
                    else if (!ReadOnly && sel >= 0 && sel <= 2)
                    {
                        if (CoopSettings.Mode != Modes[sel]) { CoopSettings.Mode = Modes[sel]; Changed(); }
                        MenuManager.PlaySelectSound();
                    }
                    else if (sel == ID_NAMES)
                    {
                        CoopSettings.ShowNames = !CoopSettings.ShowNames;
                        CoopSettings.Save();
                        CoopLog.Write("SETTINGS", "show player names " + (CoopSettings.ShowNames ? "ON" : "OFF") + " (local)");
                        MenuManager.PlaySelectSound();
                    }
                    else if (sel == ID_FLARE)
                    {
                        int n = OlCoop.World.CoopFlares.Count;
                        CoopSettings.FlareColor = ((CoopSettings.FlareColor + (UIManager.m_select_dir < 0 ? -1 : 1)) % n + n) % n;
                        CoopSettings.Save();
                        OlCoop.World.CoopFlares.MyColorChanged();
                        CoopLog.Write("SETTINGS", "flare color " + OlCoop.World.CoopFlares.Name(CoopSettings.FlareColor) + " (local)");
                        MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
                    }
                    else if (!ReadOnly && sel == ID_FF)
                    {
                        CoopSettings.FriendlyFire = !CoopSettings.FriendlyFire;
                        Changed();
                        MenuManager.PlaySelectSound();
                    }
                    else if (!ReadOnly && sel == ID_COOLDOWN && CoopSettings.Mode == DeathMode.Respawn)
                    {
                        StepDelay(UIManager.m_select_dir);
                        Changed();
                        MenuManager.PlayCycleSound(1f, UIManager.m_select_dir);
                    }
                    MenuManager.UnReverseOption();
                    break;
            }
        }
    }

    /// OPTIONS screen: stock layout plus CO-OP OPTIONS directly below MULTIPLAYER OPTIONS.
    [HarmonyPatch(typeof(UIElement), "DrawOptionsMenu")]
    static class M1_DrawOptionsMenu
    {
        static bool Prefix(UIElement __instance)
        {
            try { CoopOptionsMenu.DrawOptionsMenu(__instance); return false; }
            catch (Exception ex) { CoopLog.Error("M1 DrawOptionsMenu", ex); return true; }
        }
    }

    /// Selecting CO-OP OPTIONS in the OPTIONS screen.
    [HarmonyPatch(typeof(MenuManager), "OptionsUpdate")]
    static class M2_OptionsUpdate
    {
        static void Postfix()
        {
            if (MenuManager.m_menu_state != MenuState.OPTIONS || MenuManager.m_menu_sub_state != MenuSubState.ACTIVE) return;
            if (UIManager.m_menu_selection == CoopOptionsMenu.OptionsItemId && UIManager.PushedSelect(-1))
            {
                MenuManager.ChangeMenuState(CoopOptionsMenu.msCoopOptions);
                UIManager.DestroyAll();
                MenuManager.PlaySelectSound();
            }
        }
    }

    /// Run our menu state.
    [HarmonyPatch(typeof(MenuManager), "Update")]
    static class M3_MenuUpdate
    {
        static void Postfix(ref float ___m_menu_state_timer)
        {
            if (MenuManager.m_menu_state != CoopOptionsMenu.msCoopOptions) return;
            try { CoopOptionsMenu.Update(ref ___m_menu_state_timer); } catch (Exception ex) { CoopLog.Error("M3", ex); }
        }
    }

    /// Draw our UI element.
    [HarmonyPatch(typeof(UIElement), "Draw")]
    static class M4_UIElementDraw
    {
        static void Postfix(UIElement __instance)
        {
            if (__instance.m_type != CoopOptionsMenu.uiCoopOptions || __instance.m_alpha <= 0f) return;
            try { CoopOptionsMenu.Draw(__instance); } catch (Exception ex) { CoopLog.Error("M4", ex); }
        }
    }

    /// Always-visible version on the main menu (0.6.9: 20% of the screen height lower; 0.6.10: 40% of the width to the right - it was cut off at the edges).
    [HarmonyPatch(typeof(UIElement), "Draw")]
    static class M5_MainMenuVersion
    {
        static void Postfix(UIElement __instance)
        {
            if (__instance.m_type != UIElementType.MAIN_MENU || __instance.m_alpha <= 0f) return;
            try
            {
                __instance.DrawStringSmall(CoopVersion.Full.ToUpperInvariant() + (CoopConfig.IsHost ? " - HOST" : CoopConfig.IsJoiner ? " - JOINER" : ""),
                    new Vector2(UIManager.UI_LEFT + 12f + (UIManager.UI_RIGHT - UIManager.UI_LEFT) * 0.4f, UIManager.UI_TOP + 14f + (UIManager.UI_BOTTOM - UIManager.UI_TOP) * 0.2f), 0.4f, StringOffset.LEFT, UIManager.m_col_ui2, __instance.m_alpha, -1f);
            }
            catch (Exception ex) { CoopLog.Error("M5", ex); }
        }
    }
}
