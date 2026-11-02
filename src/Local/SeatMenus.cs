using System.Collections.Generic;
using HarmonyLib;
using PartyAnywhere;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Zorro.ControllerSupport;
using Zorro.Core;

namespace PeakCoop.Local
{
    /// <summary>
    /// Os menus e rodas do PEAK com o controle de cada escalador do sofá. O jogo lê a interface de um lugar só (o
    /// UIInputHandler: confirmar, cancelar, abas, a direção da roda; e o EventSystem: o botão selecionado), ligado ao
    /// controle do Windows, que é de quem hospeda. Cada vaga ganha o seu UIInputHandler, preenchido pelo controle dela,
    /// e uma seleção própria nos menus dela: direcional ou analógico move, A confirma, B volta, LB/RB trocam a aba.
    /// No contexto de uma vaga, o jogo acha que a entrada é de controle (as rodas usam o analógico).
    /// </summary>
    static class SeatMenus
    {
        class SeatUi
        {
            public UIInputHandler Handler;
            public Selectable Selected;
            public float NextMove;
        }

        static readonly SeatUi[] seats = new SeatUi[Client.MaxSeats];
        static readonly System.Reflection.FieldInfo handlerField =
            AccessTools.Field(typeof(Singleton<UIInputHandler>), "_instance");
        const float StickThreshold = 0.6f, RepeatDelay = 0.28f;

        public static void Create(ExtraSeat s)
        {
            if (s.InteractionObject == null || Singleton<UIInputHandler>.Instance == null) return;
            var ui = seats[s.Seat] ?? (seats[s.Seat] = new SeatUi());
            if (ui.Handler != null) return;
            SeatContext.Adopt(s, handlerField, () =>
            {
                ui.Handler = s.InteractionObject.AddComponent<UIInputHandler>();
                // a leitura dela vem daqui, não das ações do Windows
                ui.Handler.enabled = false;
            });
        }

        /// <summary>Uma vez por quadro, no contexto da vaga: o controle dela vira a entrada da interface dela.</summary>
        public static void Feed(int seat, in Pad pad)
        {
            SeatUi ui = seats[seat];
            if (ui?.Handler == null) return;
            Buttons p = pad.Pressed;
            ui.Handler.confirmWasPressed = (p & Buttons.A) != 0;
            ui.Handler.cancelWasPressed = (p & Buttons.B) != 0;
            ui.Handler.tabLeftWasPressed = (p & Buttons.LB) != 0;
            ui.Handler.tabRightWasPressed = (p & Buttons.RB) != 0;
            Vector2 left = new Vector2(pad.Lx, pad.Ly), right = new Vector2(pad.Rx, pad.Ry);
            ui.Handler.wheelNavigationVector = right.sqrMagnitude > left.sqrMagnitude ? right : left;
        }

        /// <summary>Com um menu da vaga aberto: move a seleção e confirma. true se o controle foi usado pelo menu.</summary>
        public static bool Navigate(int seat, in Pad pad, GameObject menu)
        {
            SeatUi ui = seats[seat] ?? (seats[seat] = new SeatUi());
            if (ui.Selected == null || !ui.Selected.isActiveAndEnabled || !ui.Selected.IsInteractable() || !ui.Selected.transform.IsChildOf(menu.transform))
                Select(ui, First(menu));
            if (ui.Selected == null) return false;

            Vector2 dir = new Vector2(pad.Lx, pad.Ly);
            Buttons held = pad.Held;
            if ((held & Buttons.Up) != 0) dir.y = 1f;
            if ((held & Buttons.Down) != 0) dir.y = -1f;
            if ((held & Buttons.Left) != 0) dir.x = -1f;
            if ((held & Buttons.Right) != 0) dir.x = 1f;
            if (dir.magnitude < StickThreshold) ui.NextMove = 0f;
            else if (Time.unscaledTime >= ui.NextMove)
            {
                ui.NextMove = Time.unscaledTime + RepeatDelay;
                if (ui.Selected is Slider slider && Mathf.Abs(dir.x) > Mathf.Abs(dir.y))
                    slider.value += Mathf.Sign(dir.x) * (slider.wholeNumbers ? 1f : (slider.maxValue - slider.minValue) * 0.1f);
                else
                {
                    Selectable next = Mathf.Abs(dir.x) > Mathf.Abs(dir.y)
                        ? (dir.x > 0 ? ui.Selected.FindSelectableOnRight() : ui.Selected.FindSelectableOnLeft())
                        : (dir.y > 0 ? ui.Selected.FindSelectableOnUp() : ui.Selected.FindSelectableOnDown());
                    if (next != null && next.transform.IsChildOf(menu.transform)) Select(ui, next);
                }
            }
            if ((pad.Pressed & Buttons.A) != 0)
                ExecuteEvents.Execute(ui.Selected.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            return true;
        }

        static Selectable First(GameObject menu)
        {
            Selectable best = null;
            foreach (Selectable sel in menu.GetComponentsInChildren<Selectable>())
            {
                if (!sel.isActiveAndEnabled || !sel.IsInteractable() || sel.navigation.mode == Navigation.Mode.None) continue;
                // o de cima à esquerda, como o primeiro botão de um menu
                if (best == null || sel.transform.position.y > best.transform.position.y + 1f
                    || (Mathf.Abs(sel.transform.position.y - best.transform.position.y) <= 1f && sel.transform.position.x < best.transform.position.x))
                    best = sel;
            }
            return best;
        }

        static void Select(SeatUi ui, Selectable next)
        {
            var evt = new BaseEventData(EventSystem.current);
            if (ui.Selected != null && ui.Selected != next) ui.Selected.OnDeselect(evt);
            ui.Selected = next;
            next?.OnSelect(evt);
        }

        /// <summary>O jogo pede para selecionar um botão (abrir um menu): no contexto de uma vaga, é a seleção dela.</summary>
        [HarmonyPatch(typeof(UIInputHandler), nameof(UIInputHandler.SetSelectedObject))]
        static class SelectForSeat
        {
            static bool Prefix(GameObject obj)
            {
                if (!SeatContext.Active) return true;
                SeatUi ui = seats[SeatContext.Current] ?? (seats[SeatContext.Current] = new SeatUi());
                Selectable sel = obj != null ? obj.GetComponent<Selectable>() : null;
                if (sel != null) Select(ui, sel);
                return false;
            }
        }

        /// <summary>No contexto de uma vaga, a entrada é sempre de controle (as rodas e os menus seguem o analógico).</summary>
        [HarmonyPatch(typeof(InputHandler), nameof(InputHandler.GetCurrentUsedInputScheme))]
        static class SeatUsesGamepad
        {
            static void Postfix(ref InputScheme __result)
            {
                if (SeatContext.Active) __result = InputScheme.Gamepad;
            }
        }
    }
}
