// In-game co-op setup and the between-level flow.
//  - CO-OP screen (main menu, right of QUIT): HOST A CO-OP GAME / invite Steam friends / join a friend who is hosting / LEAVE.
//    One launcher (olcoop.bat); the command-line -coophost/-coopjoin still work for same-PC testing.
//  - Between levels the story scenes (prologue, briefing, debrief, intros, entity briefings) are skipped in co-op: level results
//    (stats) -> upgrades -> level briefing -> play.
//  - Joiners' PLAY button on the level briefing reads READY UP (it tells the host this player is ready).
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Overload;
using Steamworks;
using UnityEngine;
using OlCoop.SteamNet;

namespace OlCoop.UI
{
    public static class CoopSessionMenu
    {
        public static readonly MenuState msCoop = (MenuState)121;
        public static readonly UIElementType uiCoop = (UIElementType)123;
        public const int MainMenuItemId = 40;
        const int ID_HOST = 0, ID_OVERLAY = 1, ID_LEAVE = 3, ID_READY = 4, ID_CAMPAIGN = 5, ID_CHALLENGE = 6, ID_PREV = 8, ID_NEXT = 9, ID_FRIEND0 = 10, MAX_FRIENDS = 20, ID_BACK = 100;
        static int s_page, s_per_page = 1;
        static readonly MethodInfo s_goBack = AccessTools.Method(typeof(MenuManager), "GoBack");

        static List<SteamLink.Friend> s_friends = new List<SteamLink.Friend>();
        static float s_next_refresh;

        static void Refresh()
        {
            if (Time.realtimeSinceStartup < s_next_refresh) return;
            s_next_refresh = Time.realtimeSinceStartup + 2f;
            try { s_friends = SteamLink.Friends(); } catch (Exception ex) { CoopLog.Error("friends", ex); s_friends = new List<SteamLink.Friend>(); }
        }

        /// The friends shown as buttons: hosting -> every online friend (INVITE); otherwise -> friends who are hosting (JOIN).
        static List<SteamLink.Friend> Rows()
        {
            var l = new List<SteamLink.Friend>();
            foreach (var f in s_friends)
            {
                if (CoopConfig.IsHost || f.Lobby != CSteamID.Nil) l.Add(f);
            }
            return l;
        }

        static string RoleLine()
        {
            if (CoopConfig.IsHost) return "YOU ARE HOSTING - START OR CONTINUE THE CAMPAIGN FROM THE MAIN MENU";
            if (CoopConfig.IsJoiner) return (OlCoop.Session.CoopClient.Welcomed ? "CONNECTED TO " : "JOINING ") + HostName();
            return "CHOOSE: HOST A GAME, OR JOIN A FRIEND WHO IS HOSTING";
        }

        static string HostName()
        {
            if (CoopConfig.JoinSteamId != 0) return SteamLink.Name(new CSteamID(CoopConfig.JoinSteamId)).ToUpperInvariant();
            return (CoopConfig.JoinIp ?? "").ToUpperInvariant();
        }

        public static void DrawMainMenuButton(UIElement uie, Vector2 discordPos)
        {
            var p = discordPos; p.x = 500f;
            string label = CoopConfig.IsHost ? "CO-OP: HOSTING" : CoopConfig.IsJoiner ? "CO-OP: JOINED" : "CO-OP: HOST / JOIN";
            if (OlCoop.World.PostLevel.ReadyButton) label = "CO-OP: READY UP";
            else if (CoopConfig.IsJoiner && OlCoop.World.PostLevel.ManualReady && OlCoop.World.PostLevel.HostWaiting) label = "CO-OP: WAITING FOR HOST";
            uie.SelectAndDrawHalfItem(label, p, MainMenuItemId, false);
        }

        public static void Draw(UIElement uie)
        {
            UIManager.X_SCALE = 0.35f;
            UIManager.ui_bg_dark = true;
            uie.DrawMenuBG();
            uie.DrawHeaderMedium(Vector2.up * (UIManager.UI_TOP + 20f), "CO-OP", 1f);
            uie.DrawStringSmall(CoopVersion.Full.ToUpperInvariant(), Vector2.up * (UIManager.UI_TOP + 52f), 0.45f, StringOffset.CENTER, UIManager.m_col_ui2, 1f, -1f);
            Vector2 pos = uie.m_position;
            pos.y -= 230f;
            uie.DrawStringSmall(RoleLine(), pos - Vector2.up * 75f, 0.45f, StringOffset.CENTER, UIManager.m_col_hi4, 1f, -1f);
            if (!string.IsNullOrEmpty(SteamLink.LastStatus))
                uie.DrawStringSmall(SteamLink.LastStatus, pos - Vector2.up * 52f, 0.4f, StringOffset.CENTER, UIManager.m_col_ui2, 1f, -1f);
            uie.DrawMenuSeparator(pos - Vector2.up * 32f);

            bool steam = SteamLink.Available;
            if (!CoopConfig.IsJoiner)
            {
                uie.SelectAndDrawItem(CoopConfig.IsHost ? "STOP HOSTING" : "HOST A CO-OP GAME", pos, ID_HOST, false, 1f, 0.75f);
                pos.y += 62f;
            }
            if (CoopConfig.IsHost)
            {
                // 0.6.12: start the campaign from here (the stock mission select: new game / level select)
                uie.SelectAndDrawItem("PLAY CAMPAIGN", pos, ID_CAMPAIGN, false, 1f, 0.75f);
                pos.y += 62f;
                // 0.7.0: co-op challenge mode (the stock challenge level select)
                uie.SelectAndDrawItem("PLAY CHALLENGE", pos, ID_CHALLENGE, false, 1f, 0.75f);
                pos.y += 62f;
            }
            bool hostRow = CoopConfig.IsHost;
            if (hostRow)
            {
                // 0.7.16 (user): host layout - session players on the left, a smaller INVITE THROUGH STEAM button on the right
                var roster = OlCoop.Session.CoopLobby.Current();
                float top = pos.y, left = -630f; // 0.7.18 (user): 10% further left asked (-658), held at -630 inside the UI edge (-640)
                uie.DrawStringSmall("IN THIS SESSION (" + roster.Count + "/" + OlCoop.Session.CoopLobby.MaxPlayers + "):", new Vector2(left, top - 14f), 0.42f, StringOffset.LEFT, UIManager.m_col_ui2, 1f, -1f);
                float y = top + 14f;
                if (roster.Count == 0) { uie.DrawStringSmall("NOBODY YET", new Vector2(left, y), 0.4f, StringOffset.LEFT, UIManager.m_col_ui1, 1f, -1f); y += 26f; }
                foreach (var e in roster)
                {
                    uie.DrawStringSmall(OlCoop.Session.CoopLobby.Line(e), new Vector2(left, y), 0.4f, StringOffset.LEFT, e.host ? UIManager.m_col_hi4 : UIManager.m_col_ui1, 1f, -1f);
                    y += 26f;
                }
                if (steam && SteamLink.Lobby != CSteamID.Nil)
                    uie.SelectAndDrawHalfItem("INVITE THROUGH STEAM", new Vector2(419f, top), ID_OVERLAY, false); // 0.7.19: 5% further right (was 399)
                pos.y = Mathf.Max(top + 62f, y + 20f);
            }
            if (OlCoop.World.PostLevel.ReadyButton)
            {
                uie.SelectAndDrawItem("READY UP", pos, ID_READY, false, 1f, 0.75f);
                pos.y += 62f;
            }
            else if (CoopConfig.IsJoiner && OlCoop.World.PostLevel.ManualReady && OlCoop.World.PostLevel.HostWaiting)
            {
                uie.DrawStringSmall("WAITING FOR HOST", pos, 0.45f, StringOffset.CENTER, UIManager.m_col_hi4, 1f, -1f);
                pos.y += 40f;
            }
            if (CoopConfig.IsJoiner)
            {
                uie.SelectAndDrawItem("LEAVE CO-OP", pos, ID_LEAVE, false, 1f, 0.75f);
                pos.y += 62f;
            }

            if (CoopConfig.Active && !hostRow)
            {
                var roster = OlCoop.Session.CoopLobby.Current();
                uie.DrawStringSmall("IN THIS SESSION (" + roster.Count + "/" + OlCoop.Session.CoopLobby.MaxPlayers + "):", pos + Vector2.up * 4f, 0.45f, StringOffset.CENTER, UIManager.m_col_ui2, 1f, -1f);
                pos.y += 30f;
                if (roster.Count == 0)
                {
                    uie.DrawStringSmall(CoopConfig.IsJoiner ? "WAITING FOR THE HOST..." : "NOBODY YET", pos, 0.4f, StringOffset.CENTER, UIManager.m_col_ui1, 1f, -1f);
                    pos.y += 26f;
                }
                foreach (var e in roster)
                {
                    uie.DrawStringSmall(OlCoop.Session.CoopLobby.Line(e), pos, 0.4f, StringOffset.CENTER, e.host ? UIManager.m_col_hi4 : UIManager.m_col_ui1, 1f, -1f);
                    pos.y += 26f;
                }
                pos.y += 16f;
            }

            if (!steam)
            {
                uie.DrawStringSmall("STEAM IS NOT AVAILABLE - START STEAM AND RESTART THE GAME TO PLAY WITH FRIENDS", pos + Vector2.up * 10f, 0.4f, StringOffset.CENTER, UIManager.m_col_ui2, 1f, -1f);
            }
            else if (!CoopConfig.IsJoiner)
            {
                Refresh();
                var rows = Rows();
                uie.DrawStringSmall(CoopConfig.IsHost ? "INVITE A FRIEND:" : "FRIENDS HOSTING CO-OP:", pos + Vector2.up * 4f, 0.45f, StringOffset.CENTER, UIManager.m_col_ui2, 1f, -1f);
                pos.y += 34f;
                if (rows.Count == 0)
                    uie.DrawStringSmall(CoopConfig.IsHost ? "NO FRIENDS ONLINE" : "NONE RIGHT NOW - ASK YOUR FRIEND TO HOST, OR ACCEPT THEIR STEAM INVITE",
                        pos, 0.4f, StringOffset.CENTER, UIManager.m_col_ui1, 1f, -1f);
                // 0.6.12: pages - as many rows as fit above BACK (minus one row for the page buttons), PREV/NEXT to scroll
                float room = (UIManager.UI_BOTTOM - 95f) - pos.y;
                int fit = Mathf.Max(1, (int)(room / 50f) + 1);
                s_per_page = rows.Count > fit ? Mathf.Max(1, fit - 1) : fit;
                s_per_page = Mathf.Min(s_per_page, MAX_FRIENDS);
                int pages = Mathf.Max(1, (rows.Count + s_per_page - 1) / s_per_page);
                if (s_page >= pages) s_page = pages - 1;
                if (s_page < 0) s_page = 0;
                for (int k = 0; k < s_per_page; k++)
                {
                    int i = s_page * s_per_page + k;
                    if (i >= rows.Count) break;
                    var f = rows[i];
                    string tag = CoopConfig.IsHost ? (f.InOverload ? "INVITE  (IN OVERLOAD)" : "INVITE") : "JOIN";
                    uie.SelectAndDrawItem(Clip(f.Name) + "  -  " + tag, pos, ID_FRIEND0 + k, false, 1f, 0.6f);
                    pos.y += 50f;
                }
                if (pages > 1)
                {
                    uie.DrawStringSmall("PAGE " + (s_page + 1) + " / " + pages + "  (" + rows.Count + " ONLINE)", pos + Vector2.up * 2f, 0.4f, StringOffset.CENTER, UIManager.m_col_ui2, 1f, -1f);
                    uie.SelectAndDrawHalfItem("< PREV", pos + Vector2.right * -419f, ID_PREV, false); // 0.7.19: 5% further left (was -399)
                    uie.SelectAndDrawHalfItem("NEXT >", pos + Vector2.right * 419f, ID_NEXT, false); // 0.7.19: 5% further right (was 399)
                    pos.y += 50f;
                }
            }
            pos.y = UIManager.UI_BOTTOM - 30f;
            uie.SelectAndDrawItem(Loc.LS("BACK"), pos, ID_BACK, false, 1f, 0.75f);
        }

        static string Clip(string s)
        {
            s = (s ?? "?").ToUpperInvariant();
            return s.Length > 22 ? s.Substring(0, 21) + "." : s;
        }

        public static void Update(ref float timer)
        {
            UIManager.MouseSelectUpdate();
            if (CoopConfig.IsJoiner) { try { OlCoop.Session.CoopClient.MenuTick(); } catch (Exception ex) { CoopLog.Error("coop menu tick", ex); } }
            switch (MenuManager.m_menu_sub_state)
            {
                case MenuSubState.INIT:
                    if (timer > 0.25f)
                    {
                        UIManager.CreateUIElement(UIManager.SCREEN_CENTER, 7000, uiCoop);
                        MenuManager.m_menu_sub_state = MenuSubState.ACTIVE;
                        s_next_refresh = 0f; s_page = 0;
                        MenuManager.SetDefaultSelection(OlCoop.World.PostLevel.ReadyButton ? ID_READY : CoopConfig.IsJoiner ? ID_LEAVE : ID_HOST);
                    }
                    break;
                case MenuSubState.ACTIVE:
                    UIManager.ControllerMenu();
                    if (!UIManager.PushedSelect(-1)) break;
                    int sel = UIManager.m_menu_selection;
                    if (sel == ID_BACK)
                    {
                        s_goBack.Invoke(null, null);
                        UIManager.DestroyAll();
                        MenuManager.PlaySelectSound();
                    }
                    else if (sel == ID_HOST)
                    {
                        if (CoopConfig.IsHost) { SteamLink.Leave(); CoopConfig.ClearRole(); SteamLink.LastStatus = "STOPPED HOSTING"; }
                        else { CoopConfig.SetHost(); if (SteamLink.Available) SteamLink.CreateLobby(); else SteamLink.LastStatus = "HOSTING (LAN / IP ONLY - STEAM NOT AVAILABLE)"; }
                        MenuManager.PlaySelectSound();
                    }
                    else if (sel == ID_OVERLAY) { SteamLink.OpenInviteOverlay(); MenuManager.PlaySelectSound(); }
                    else if (sel == ID_READY) { if (OlCoop.World.PostLevel.ReadyButton) OlCoop.World.PostLevel.PressReady(); MenuManager.PlaySelectSound(); MenuManager.SetDefaultSelection(ID_LEAVE); }
                    else if (sel == ID_LEAVE)
                    {
                        SteamLink.Leave();
                        try { if (Client.IsConnected()) Client.Disconnect(); } catch { }
                        CoopConfig.ClearRole();
                        SteamLink.LastStatus = "LEFT CO-OP";
                        MenuManager.PlaySelectSound();
                    }
                    else if (sel == ID_CAMPAIGN)
                    {
                        UIManager.DestroyAll();
                        MenuManager.ChangeMenuState(MenuState.MISSION_SELECT);
                        MenuManager.PlaySelectSound();
                    }
                    else if (sel == ID_CHALLENGE)
                    {
                        UIManager.DestroyAll();
                        MenuManager.m_selected_mission = GameManager.ChallengeMission;
                        MenuManager.ChangeMenuState(MenuState.CHALLENGE_SELECT);
                        MenuManager.PlaySelectSound();
                    }
                    else if (sel == ID_PREV) { s_page--; MenuManager.PlaySelectSound(); }
                    else if (sel == ID_NEXT) { s_page++; MenuManager.PlaySelectSound(); }
                    else if (sel >= ID_FRIEND0 && sel < ID_FRIEND0 + MAX_FRIENDS)
                    {
                        var rows = Rows();
                        int i = s_page * s_per_page + (sel - ID_FRIEND0);
                        if (i < rows.Count)
                        {
                            if (CoopConfig.IsHost) SteamLink.Invite(rows[i].Id);
                            else if (rows[i].Lobby != CSteamID.Nil) SteamLink.JoinLobby(rows[i].Lobby);
                        }
                        MenuManager.PlaySelectSound();
                    }
                    break;
            }
        }
    }

    /// Main menu: CO-OP button on the QUIT row, right side (mirrors OVERLOAD ON DISCORD on the left).
    [HarmonyPatch(typeof(UIElement), "DrawMainMenu")]
    static class SM1_DrawMainMenuButton
    {
        static readonly MethodInfo m_half = AccessTools.Method(typeof(UIElement), "SelectAndDrawHalfItem");
        static readonly MethodInfo m_ours = AccessTools.Method(typeof(CoopSessionMenu), "DrawMainMenuButton");
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var list = new List<CodeInstruction>(code);
            int done = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (done == 0 && list[i].opcode == OpCodes.Call && Equals(list[i].operand, m_half))
                {
                    // ... ldarg.0, ldstr "OVERLOAD ON DISCORD", ldloc.0 (position), ldc id, ldc bool, call -> reuse the position local
                    CodeInstruction posLoad = null;
                    for (int k = i - 1; k >= Math.Max(0, i - 6); k--)
                        if (list[k].opcode == OpCodes.Ldloc_0 || list[k].opcode == OpCodes.Ldloc_S || list[k].opcode == OpCodes.Ldloc) { posLoad = list[k]; break; }
                    if (posLoad == null) break;
                    list.InsertRange(i + 1, new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(posLoad.opcode, posLoad.operand), new CodeInstruction(OpCodes.Call, m_ours) });
                    done++;
                }
            }
            CoopLog.Write("UI", "main menu CO-OP button " + (done == 1 ? "added" : "NOT added (menu code differs)"));
            return list;
        }
    }

    /// SM5 (0.7.13, user): a joiner's main menu greys out LOAD SAVED GAME (5), PLAY MISSION (0), PLAY CHALLENGE MODE (1) and
    /// PLAY MULTIPLAYER (7) - the host picks the game. Faded stock items can't be clicked or reached with the controller; SM6 moves a
    /// selection that still sits on one of them (the stock default is PLAY MISSION) to OPTIONS.
    public static class JoinerMainMenu
    {
        public static bool Locked { get { return CoopConfig.IsJoiner && MenuManager.m_menu_state == MenuState.MAIN_MENU; } }
        public static bool IsBlocked(int id) { return id == 5 || id == 0 || id == 1 || id == 7; }
    }

    [HarmonyPatch(typeof(UIElement), "SelectAndDrawItem", new[] { typeof(string), typeof(Vector2), typeof(int), typeof(bool), typeof(float), typeof(float) })]
    static class SM5a_JoinerGreyItems
    {
        static void Prefix(UIElement __instance, int selection, ref bool fade)
        {
            if (__instance.m_type == UIElementType.MAIN_MENU && JoinerMainMenu.Locked && JoinerMainMenu.IsBlocked(selection)) fade = true;
        }
    }

    [HarmonyPatch(typeof(UIElement), "SelectAndDrawItemOutline")]
    static class SM5b_JoinerGreyOutlineItems
    {
        static void Prefix(UIElement __instance, int selection, ref bool fade)
        {
            if (__instance.m_type == UIElementType.MAIN_MENU && JoinerMainMenu.Locked && JoinerMainMenu.IsBlocked(selection)) fade = true;
        }
    }

    [HarmonyPatch(typeof(MenuManager), "MainMenuUpdate")]
    static class SM6_JoinerNoBlockedSelection
    {
        static int s_logged;
        [HarmonyPriority(Priority.First)]
        static void Prefix()
        {
            if (!JoinerMainMenu.Locked || MenuManager.m_menu_sub_state != MenuSubState.ACTIVE) return;
            if (!JoinerMainMenu.IsBlocked(UIManager.m_menu_selection)) return;
            if (s_logged++ < 3) CoopLog.Write("UI", "joiner main menu: selection " + UIManager.m_menu_selection + " is greyed out (host picks the game); moved to OPTIONS");
            UIManager.m_menu_selection = 2;
        }
    }

    [HarmonyPatch(typeof(MenuManager), "MainMenuUpdate")]
    static class SM2_MainMenuSelect
    {
        static void Postfix()
        {
            if (MenuManager.m_menu_state != MenuState.MAIN_MENU || MenuManager.m_menu_sub_state != MenuSubState.ACTIVE) return;
            if (UIManager.m_menu_selection == CoopSessionMenu.MainMenuItemId && UIManager.PushedSelect(-1))
            {
                if (OlCoop.World.PostLevel.ReadyButton) { OlCoop.World.PostLevel.PressReady(); MenuManager.PlaySelectSound(); return; }
                MenuManager.ChangeMenuState(CoopSessionMenu.msCoop);
                UIManager.DestroyAll();
                MenuManager.PlaySelectSound();
            }
        }
    }

    [HarmonyPatch(typeof(MenuManager), "Update")]
    static class SM3_MenuUpdate
    {
        static void Postfix(ref float ___m_menu_state_timer)
        {
            if (MenuManager.m_menu_state != CoopSessionMenu.msCoop) return;
            try { CoopSessionMenu.Update(ref ___m_menu_state_timer); } catch (Exception ex) { CoopLog.Error("SM3", ex); }
        }
    }

    [HarmonyPatch(typeof(UIElement), "Draw")]
    static class SM4_Draw
    {
        static void Postfix(UIElement __instance)
        {
            if (__instance.m_type != CoopSessionMenu.uiCoop || __instance.m_alpha <= 0f) return;
            try { CoopSessionMenu.Draw(__instance); } catch (Exception ex) { CoopLog.Error("SM4", ex); }
        }
    }
}

namespace OlCoop.World
{
    /// Skip the story scenes between levels in co-op. GoToNextBriefing walks PROLOGUE/BRIEFING/INTRO.../ENTITY_BRIEFING/UPGRADE/
    /// LEVEL_BRIEFING/PLAY_GAME by looking at the current menu state; when it picks a story scene we pretend that scene just ended and
    /// ask it for the next one, until it reaches a screen we keep (upgrades, level briefing, play). DEBRIEF (after the exit) goes
    /// straight to the level results, except after the last level (credits/victory stay).
    public static class SkipScenes
    {
        static readonly HashSet<MenuState> s_skip = new HashSet<MenuState> {
            MenuState.PROLOGUE, MenuState.BRIEFING, MenuState.INTRO, MenuState.INTRO_ALIEN, MenuState.INTRO_REVIVAL, MenuState.ENTITY_BRIEFING };
        static readonly MethodInfo m_next = AccessTools.Method(typeof(MenuManager), "GoToNextBriefing");
        static int s_depth;

        public static bool Active { get { return CoopConfig.Active && !GameplayManager.IsMultiplayer && !GameplayManager.IsChallengeMode; } }

        /// ChangeMenuState prefix. False = handled (state replaced).
        public static bool Redirect(ref MenuState state)
        {
            if (!Active) return true;
            if (state == MenuState.DEBRIEF && !GameplayManager.IsLastLevel)
            {
                CoopLog.Write("FLOW", "skipping the debrief scene -> level results");
                state = MenuState.LEVEL_RESULTS;
                return true;
            }
            if (!s_skip.Contains(state) || m_next == null) return true;
            if (s_depth >= 12) { CoopLog.Write("FLOW", "scene skip: too many steps; showing " + state); return true; }
            var prev = MenuManager.m_menu_state;
            s_depth++;
            try
            {
                CoopLog.Write("FLOW", "skipping story scene " + state);
                MenuManager.m_menu_state = state;      // GoToNextBriefing decides from the current state
                m_next.Invoke(null, null);
            }
            catch (Exception ex) { CoopLog.Error("scene skip", ex); MenuManager.m_menu_state = prev; s_depth--; return true; }
            MenuManager.m_menu_state = prev;
            s_depth--;
            return false;
        }
    }

    [HarmonyPatch(typeof(MenuManager), "ChangeMenuState")]
    static class SK1_SkipScenes
    {
        static bool Prefix(ref MenuState new_state)
        {
            try { return SkipScenes.Redirect(ref new_state); } catch (Exception ex) { CoopLog.Error("SK1", ex); return true; }
        }
    }

    /// Level briefing button: READY UP on joiners (PLAY / BEGIN SIMULATION on the host).
    /// 0.7.9: also the challenge briefing (DrawLevelBriefingCM) - a joiner's PLAY there only marks it ready.
    [HarmonyPatch]
    static class SK2_ReadyUpLabel
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(UIElement), "DrawLevelBriefing");
            yield return AccessTools.Method(typeof(UIElement), "DrawLevelBriefingCM");
        }
        static readonly MethodInfo m_ls = AccessTools.Method(typeof(Loc), "LS", new[] { typeof(string) });
        static readonly MethodInfo m_label = AccessTools.Method(typeof(SK2_ReadyUpLabel), "Label");
        public static string Label(string s) { return CoopConfig.IsJoiner && !GameplayManager.IsMultiplayer ? "READY UP" : s; }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var list = new List<CodeInstruction>(code);
            int n = 0;
            for (int i = 0; i + 1 < list.Count; i++)
            {
                if (list[i].opcode != OpCodes.Ldstr) continue;
                var str = list[i].operand as string;
                if (str != "PLAY" && str != "BEGIN SIMULATION") continue;
                if (!(list[i + 1].opcode == OpCodes.Call && Equals(list[i + 1].operand, m_ls))) continue;
                list.Insert(i + 2, new CodeInstruction(OpCodes.Call, m_label));
                n++;
            }
            CoopLog.Write("UI", "level briefing READY UP label: " + n + " button(s) patched");
            return list;
        }
    }
}

namespace OlCoop.UI
{
    /// Esc menu entry under QUIT TO MAIN MENU: LEAVE SESSION (joiner) / STOP HOSTING (host). It runs the stock quit-to-menu
    /// flow (with its ARE YOU SURE? step); quitting to the menu in co-op ends the session (Phase7Steam ST6/ST7).
    public static class PauseLeave
    {
        public const int ItemId = 30;
        public static bool Show { get { return CoopConfig.Active && !GameplayManager.IsMultiplayer && GameplayManager.LevelIsLoaded; } }
        public static string Label { get { return CoopConfig.IsHost ? "STOP HOSTING" : "LEAVE SESSION"; } }

        public static void Draw(UIElement uie, ref Vector2 pos)
        {
            if (!Show) return;
            pos.y += 62f;
            uie.SelectAndDrawItem(Label, pos, ItemId, false, 1f, 0.75f);
        }
    }

    [HarmonyPatch(typeof(UIElement), "DrawPauseMenu")]
    static class PM1_DrawLeaveItem
    {
        static readonly MethodInfo m_item = AccessTools.Method(typeof(UIElement), "SelectAndDrawItem");
        static readonly MethodInfo m_ours = AccessTools.Method(typeof(PauseLeave), "Draw");
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var list = new List<CodeInstruction>(code);
            int done = 0;
            for (int i = 0; i < list.Count && done == 0; i++)
            {
                if (list[i].opcode != OpCodes.Ldstr || (list[i].operand as string) != "QUIT TO MAIN MENU") continue;
                // ldstr, call LS, ldloc <pos>, ..., call SelectAndDrawItem
                object posLocal = null; OpCode posOp = OpCodes.Nop;
                for (int k = i + 1; k < Math.Min(list.Count, i + 4); k++)
                    if (list[k].opcode == OpCodes.Ldloc_0) { posLocal = 0; posOp = OpCodes.Ldloc_0; break; }
                    else if (list[k].opcode == OpCodes.Ldloc_S || list[k].opcode == OpCodes.Ldloc) { posLocal = list[k].operand; posOp = list[k].opcode; break; }
                if (posLocal == null) break;
                for (int j = i; j < Math.Min(list.Count, i + 12); j++)
                {
                    if (!(list[j].opcode == OpCodes.Call && Equals(list[j].operand, m_item))) continue;
                    var addr = posOp == OpCodes.Ldloc_0 ? new CodeInstruction(OpCodes.Ldloca_S, (byte)0) : new CodeInstruction(OpCodes.Ldloca_S, posLocal);
                    list.InsertRange(j + 1, new[] { new CodeInstruction(OpCodes.Ldarg_0), addr, new CodeInstruction(OpCodes.Call, m_ours) });
                    done++;
                    break;
                }
            }
            CoopLog.Write("UI", "Esc menu LEAVE SESSION / STOP HOSTING item " + (done == 1 ? "added" : "NOT added (menu code differs)"));
            return list;
        }
    }

    /// PM3 (0.6.3): the session list in the Esc menu too (left side, beside the menu items): who is in the game, state, ping.
    [HarmonyPatch(typeof(UIElement), "DrawPauseMenu")]
    static class PM3_SessionList
    {
        static void Postfix(UIElement __instance)
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer || MenuManager.m_menu_micro_state != 0) return;
            try
            {
                var roster = OlCoop.Session.CoopLobby.Current();
                var pos = new Vector2(UIManager.UI_LEFT + 40f, -124f);
                __instance.DrawStringSmall("IN THIS SESSION (" + roster.Count + "/" + OlCoop.Session.CoopLobby.MaxPlayers + ")", pos, 0.45f, StringOffset.LEFT, UIManager.m_col_ui2, 1f, -1f);
                pos.y += 30f;
                if (roster.Count == 0)
                    __instance.DrawStringSmall(CoopConfig.IsJoiner ? "WAITING FOR THE HOST..." : "NOBODY YET", pos, 0.4f, StringOffset.LEFT, UIManager.m_col_ui1, 1f, -1f);
                foreach (var e in roster)
                {
                    __instance.DrawStringSmall(OlCoop.Session.CoopLobby.ShortName(e), pos, 0.42f, StringOffset.LEFT, e.host ? UIManager.m_col_hi4 : UIManager.m_col_ui1, 1f, -1f);
                    pos.y += 22f;
                    __instance.DrawStringSmall("  " + OlCoop.Session.CoopLobby.StateText(e.state) + "  " + (e.host ? "HOST" : (e.ping > 0 ? e.ping + " MS" : "-")), pos, 0.36f, StringOffset.LEFT, UIManager.m_col_ui2, 1f, -1f);
                    pos.y += 28f;
                }
            }
            catch (Exception ex) { CoopLog.Error("PM3", ex); }
        }
    }

    /// Selecting it = selecting QUIT TO MAIN MENU (same confirmation and exit; the session ends on the way out).
    [HarmonyPatch(typeof(MenuManager), "PausedUpdate")]
    static class PM2_LeaveSelect
    {
        static bool Prefix()
        {
            if (!PauseLeave.Show || MenuManager.m_menu_sub_state != MenuSubState.ACTIVE) return true;
            UIManager.MouseSelectUpdate(); // the hovered entry, as the stock code would see it this frame
            if (UIManager.m_menu_selection != PauseLeave.ItemId || !UIManager.PushedSelect(-1)) return true;
            CoopLog.Write("UI", PauseLeave.Label + " chosen in the Esc menu");
            MenuManager.PlaySelectSound();
            try { OlCoop.SteamNet.SessionEnd.LeaveNow(); } catch (Exception ex) { CoopLog.Error("PM2 leave", ex); }
            return false;
        }
    }
}
