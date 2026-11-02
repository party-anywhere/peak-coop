using System.Collections.Generic;
using HarmonyLib;
using PartyAnywhere;
using PeakCoop.Local;
using UnityEngine;

namespace PeakCoop.Input
{
    /// <summary>
    /// Cada escalador lê o controle da própria vaga pelo SDK, não o teclado, o mouse e os controles do Windows (um
    /// teclado que é o P3 no Party Anywhere também chega ao jogo, e moveria o P1). Sem o Hub, ou com a vaga de quem
    /// hospeda vazia, o P1 joga como sempre.
    ///
    /// Mapa (igual ao <see cref="PhoneLabels"/> e ao `controls` da ficha): analógico esquerdo anda, direito olha;
    /// A pula, B agacha, X usa, Y solta o item; RT agarra/escala, LT usa o item; LB corre, RB marca (ping),
    /// RS emotes; direcional: ◀ ▶ trocam o item, ▼ guarda, ▲ mochila; Start pausa.
    /// </summary>
    [HarmonyPatch(typeof(CharacterInput), "Sample")]
    static class SeatInput
    {
        /// <summary>Graus por segundo com o analógico no fim (o celular soma o arrastar do dedo, em graus).</summary>
        const float LookYawSpeed = 200f, LookPitchSpeed = 140f;
        const float StickDeadZone = 0.12f;

        /// <summary>O quanto cada escalador guiado pelo SDK vai girar a câmera neste quadro, em graus.</summary>
        static readonly Dictionary<CharacterInput, Vector2> lookDegrees = new Dictionary<CharacterInput, Vector2>();

        static bool Prefix(CharacterInput __instance, bool playerMovementActive)
        {
            Client hub = Plugin.Hub;
            Character character = __instance.GetComponent<Character>();
            if (hub == null || !Seats.TryGetSeat(character, out int seat) || !Seats.Occupied(seat))
            {
                lookDegrees.Remove(__instance);
                return true;
            }
            Fill(__instance, hub, seat, playerMovementActive);
            return false;
        }

        static void Fill(CharacterInput input, Client hub, int seat, bool movementActive)
        {
            input.ResetInput();
            // vaga reconectando: o escalador fica parado e protegido até a pessoa voltar
            hub.GetPad(seat, out Pad pad);
            Buttons held = pad.Held, pressed = pad.Pressed, released = pad.Released;
            bool rt = pad.Rt > 0.5f, lt = pad.Lt > 0.5f;

            // a interface dele (rodas, cancelar, abas) lê o controle dele
            if (seat != Seats.Host) SeatMenus.Feed(seat, pad);
            // com um menu dele aberto (pausa, passaporte, cartão de embarque, fim da expedição), o controle navega nele
            GameObject menu = seat != Seats.Host ? SeatWindows.OpenMenu(seat) : null;
            if (menu != null)
            {
                if ((pressed & Buttons.B) != 0) SeatWindows.Back(seat);
                else SeatMenus.Navigate(seat, pad, menu);
                input.pauseWasPressed = (pressed & Buttons.Start) != 0;
                lookDegrees.Remove(input);
                return;
            }

            input.pauseWasPressed = (pressed & Buttons.Start) != 0;
            input.interactWasPressed = (pressed & Buttons.X) != 0;
            input.interactIsPressed = (held & Buttons.X) != 0;
            input.interactWasReleased = (released & Buttons.X) != 0;
            input.emoteIsPressed = (held & Buttons.RS) != 0;
            input.selectSlotForwardWasPressed = (pressed & Buttons.Right) != 0 && !input.itemSwitchBlocked;
            input.selectSlotBackwardWasPressed = (pressed & Buttons.Left) != 0 && !input.itemSwitchBlocked;
            input.unselectSlotWasPressed = (pressed & Buttons.Down) != 0 && !input.itemSwitchBlocked;
            if (!movementActive)
            {
                lookDegrees.Remove(input);
                return;
            }

            input.movementInput = Vector2.ClampMagnitude(DeadZone(new Vector2(pad.Lx, pad.Ly)), 1f);
            bool sprint = (held & (Buttons.LB | Buttons.LS)) != 0;
            input.sprintIsPressed = sprint;
            input.sprintWasPressed = (pressed & (Buttons.LB | Buttons.LS)) != 0;
            input.jumpWasPressed = (pressed & Buttons.A) != 0;
            input.jumpIsPressed = (held & Buttons.A) != 0;
            input.crouchWasPressed = (pressed & Buttons.B) != 0;
            input.crouchIsPressed = (held & Buttons.B) != 0;
            input.dropWasPressed = (pressed & Buttons.Y) != 0;
            input.dropIsPressed = (held & Buttons.Y) != 0;
            input.dropWasReleased = (released & Buttons.Y) != 0;
            Trigger(rt, ref Triggers(input).rt, out input.usePrimaryWasPressed, out input.usePrimaryIsPressed, out input.usePrimaryWasReleased);
            Trigger(lt, ref Triggers(input).lt, out input.useSecondaryWasPressed, out input.useSecondaryIsPressed, out input.useSecondaryWasReleased);
            input.pingWasPressed = (pressed & Buttons.RB) != 0;
            input.selectBackpackWasPressed = (pressed & Buttons.Up) != 0;
            input.spectateLeftWasPressed = input.selectSlotBackwardWasPressed;
            input.spectateRightWasPressed = input.selectSlotForwardWasPressed;
            input.unselectSlotWasPressed = (pressed & Buttons.Down) != 0 && !input.itemSwitchBlocked;

            Vector2 stick = DeadZone(new Vector2(pad.Rx, pad.Ry));
            // curva: precisão no começo do analógico, giro rápido no fim
            stick *= stick.magnitude;
            Vector2 deg = new Vector2(stick.x * LookYawSpeed, stick.y * LookPitchSpeed) * Time.deltaTime;
            if (hub.TakeLook(seat, out float dx, out float dy)) deg += new Vector2(dx, dy);
            lookDegrees[input] = deg;
            // a animação de virar a cabeça lê lookInput: o mesmo sinal, na escala do mouse
            input.lookInput = deg;
        }

        static Vector2 DeadZone(Vector2 v)
        {
            float m = v.magnitude;
            return m < StickDeadZone ? Vector2.zero : v * ((m - StickDeadZone) / (1f - StickDeadZone) / m);
        }

        // gatilhos são eixos no pad: o "apertou/soltou" sai da comparação com o quadro anterior
        class TriggerState { public bool rt, lt; }
        static readonly Dictionary<CharacterInput, TriggerState> triggers = new Dictionary<CharacterInput, TriggerState>();

        static TriggerState Triggers(CharacterInput input)
        {
            if (!triggers.TryGetValue(input, out TriggerState t)) triggers[input] = t = new TriggerState();
            return t;
        }

        static void Trigger(bool now, ref bool before, out bool wasPressed, out bool isPressed, out bool wasReleased)
        {
            wasPressed = now && !before;
            isPressed = now;
            wasReleased = !now && before;
            before = now;
        }

        /// <summary>
        /// A câmera de quem joga pelo SDK gira em graus, sem a sensibilidade do mouse ou do controle do Windows (que
        /// é uma só para o jogo todo). Inverter eixos segue a configuração do jogo.
        /// </summary>
        [HarmonyPatch(typeof(CharacterMovement), "CameraLook")]
        static class CameraLookPatch
        {
            static bool Prefix(CharacterMovement __instance)
            {
                Character character = __instance.GetComponent<Character>();
                if (character == null || !lookDegrees.TryGetValue(character.input, out Vector2 deg)) return true;
                character.data.lookValues.x += deg.x;
                character.data.lookValues.y = Mathf.Clamp(character.data.lookValues.y + deg.y, -85f, 85f);
                character.RecalculateLookDirections();
                return false;
            }
        }
    }
}
