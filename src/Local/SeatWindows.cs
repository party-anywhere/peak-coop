using System.Collections.Generic;
using HarmonyLib;

namespace PeakCoop.Local
{
    /// <summary>
    /// Janelas (pausa, passaporte, cartão de embarque, roda da mochila…) são de quem abriu: o jogo trata as janelas
    /// abertas como uma lista só, e uma janela aberta pelo P3 travaria todo mundo. Aqui cada HUD só enxerga as janelas
    /// dele, e o B do controle fecha as janelas do dono (os menus do jogo navegam pelo controle do Windows, que é de
    /// quem hospeda).
    /// </summary>
    static class SeatWindows
    {
        static readonly List<MenuWindow> mine = new List<MenuWindow>();

        public static int OwnerOf(MenuWindow w) => SeatComponents.TryGetOwnerSeat(w, out int seat) ? seat : Seats.Host;

        [HarmonyPatch(typeof(GUIManager), nameof(GUIManager.UpdateWindowStatus))]
        static class OnlyMyWindows
        {
            static void Prefix(out List<MenuWindow> __state)
            {
                __state = MenuWindow.AllActiveWindows;
                if (!VirtualActors.CanUse) return;
                mine.Clear();
                foreach (MenuWindow w in __state)
                    if (w != null && OwnerOf(w) == SeatContext.Current) mine.Add(w);
                MenuWindow.AllActiveWindows = mine;
            }

            static void Finalizer(List<MenuWindow> __state)
            {
                if (__state != null) MenuWindow.AllActiveWindows = __state;
            }
        }

        /// <summary>
        /// A tela de fim abre no HUD de quem hospeda (o jogo a chama uma vez): abre também a de cada escalador do sofá,
        /// no contexto dele, com o diário e o resultado dele (venceu, morreu, "seus amigos venceram").
        /// </summary>
        [HarmonyPatch(typeof(MenuWindow), nameof(MenuWindow.Open))]
        static class EndScreenForAll
        {
            static void Postfix(MenuWindow __instance)
            {
                if (!(__instance is EndScreen) || SeatContext.Active || !VirtualActors.CanUse || OwnerOf(__instance) != Seats.Host) return;
                foreach (ExtraSeat s in SeatContext.Extras)
                {
                    if (s.Hud == null || s.Character == null) continue;
                    SeatContext.Run(s.Seat, () =>
                    {
                        EndScreen mine = GUIManager.instance != null ? GUIManager.instance.endScreen : null;
                        if (mine != null && mine != __instance && !mine.isOpen) mine.Open();
                    });
                }
            }
        }

        /// <summary>Fecha as janelas da vaga (o B do controle dela, com uma janela aberta).</summary>
        public static bool CloseFor(int seat)
        {
            bool any = false;
            for (int i = MenuWindow.AllActiveWindows.Count - 1; i >= 0; i--)
            {
                MenuWindow w = MenuWindow.AllActiveWindows[i];
                if (w == null || OwnerOf(w) != seat || !w.isOpen) continue;
                w.ForceClose();
                any = true;
            }
            return any;
        }

        /// <summary>
        /// A pausa de um escalador do sofá é só dele: continuar, conquistas e controles. Sair da partida, convidar e
        /// as configurações (que são do PC todo) ficam na pausa de quem hospeda.
        /// </summary>
        [HarmonyPatch(typeof(PauseMenuMainPage), "OnEnable")]
        static class SeatPause
        {
            static void Postfix(PauseMenuMainPage __instance)
            {
                if (!VirtualActors.CanUse || OwnerOf(__instance) == Seats.Host) return;
                foreach (UnityEngine.UI.Button b in new[] { __instance.m_quitButton, __instance.m_inviteButton, __instance.m_settingsButton, __instance.copyRoomCodeButton })
                    if (b != null) b.gameObject.SetActive(false);
            }

            static int OwnerOf(UnityEngine.Component c) => SeatComponents.TryGetOwnerSeat(c, out int seat) ? seat : Seats.Host;
        }

        /// <summary>O menu da vaga que está na frente: a janela aberta mais recente dela, ou a pausa dela.</summary>
        public static UnityEngine.GameObject OpenMenu(int seat)
        {
            for (int i = MenuWindow.AllActiveWindows.Count - 1; i >= 0; i--)
            {
                MenuWindow w = MenuWindow.AllActiveWindows[i];
                if (w != null && w.isOpen && OwnerOf(w) == seat) return w.gameObject;
            }
            GUIManager gui = SeatHud.Gui(seat);
            return gui != null && gui.pauseMenu != null && gui.pauseMenu.activeSelf ? gui.pauseMenu : null;
        }

        /// <summary>O B: fecha a pausa da vaga, ou a janela da frente dela.</summary>
        public static void Back(int seat)
        {
            GUIManager gui = SeatHud.Gui(seat);
            if (gui != null && gui.pauseMenu != null && gui.pauseMenu.activeSelf)
            {
                gui.pauseMenu.SetActive(false);
                return;
            }
            for (int i = MenuWindow.AllActiveWindows.Count - 1; i >= 0; i--)
            {
                MenuWindow w = MenuWindow.AllActiveWindows[i];
                if (w == null || !w.isOpen || OwnerOf(w) != seat) continue;
                w.ForceClose();
                return;
            }
        }

        public static bool AnyOpen(int seat)
        {
            foreach (MenuWindow w in MenuWindow.AllActiveWindows)
                if (w != null && w.isOpen && OwnerOf(w) == seat) return true;
            return false;
        }
    }
}
