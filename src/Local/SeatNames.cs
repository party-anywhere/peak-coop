using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PeakCoop.Local
{
    /// <summary>
    /// O nome sobre a cabeça dos outros: no online cada PC mostra, no próprio HUD, o nome de quem a câmera dele está
    /// vendo (IsLookedAt, um por personagem). Aqui cada personagem é olhado por todos do sofá: o mesmo cálculo roda uma
    /// vez por quem olha, no contexto dele (câmera e HUD dele), e o próprio nome nunca aparece para o dono.
    /// </summary>
    static class SeatNames
    {
        /// <summary>O índice do nome no HUD de cada quem-olha (vaga) para cada personagem.</summary>
        static readonly Dictionary<(IsLookedAt, int), int> index = new Dictionary<(IsLookedAt, int), int>();
        static readonly HashSet<(IsLookedAt, int)> pending = new HashSet<(IsLookedAt, int)>();

        static IEnumerable<int> Viewers()
        {
            yield return Seats.Host;
            foreach (ExtraSeat s in SeatContext.Extras) if (s.Character != null && s.Hud != null) yield return s.Seat;
        }

        [HarmonyPatch(typeof(IsLookedAt), "Start")]
        static class Start
        {
            static bool Prefix() => !VirtualActors.CanUse;
        }

        [HarmonyPatch(typeof(IsLookedAt), "Update")]
        static class Update
        {
            static bool Prefix(IsLookedAt __instance)
            {
                if (!VirtualActors.CanUse || SeatContext.Active) return !VirtualActors.CanUse;
                Character target = __instance.characterInteractible.character;
                foreach (int viewer in Viewers())
                    SeatContext.Run(viewer, () => ShowFor(__instance, target, viewer));
                return false;
            }
        }

        [HarmonyPatch(typeof(IsLookedAt), "OnDisable")]
        static class Disable
        {
            static bool Prefix(IsLookedAt __instance)
            {
                if (!VirtualActors.CanUse) return true;
                foreach (int viewer in Viewers())
                    if (index.TryGetValue((__instance, viewer), out int i))
                        SeatContext.Run(viewer, () => { if (GUIManager.instance != null) GUIManager.instance.playerNames.DisableName(i); });
                return false;
            }
        }

        /// <summary>O nome de cada escalador do sofá é a vaga dele: "P2" (no lugar do nome da conta, que é o mesmo para todos).</summary>
        [HarmonyPatch(typeof(CharacterInteractible), nameof(CharacterInteractible.GetName))]
        static class SeatName
        {
            static void Postfix(CharacterInteractible __instance, ref string __result)
            {
                if (VirtualActors.CanUse && Seats.TryGetSeat(__instance.character, out int seat)) __result = $"P{seat + 1}";
            }
        }

        const float TagFontSize = 54f;

        /// <summary>A placa sobre a cabeça: só "P2", na cor da vaga, sem ícone de voz nem estrela de anfitrião.</summary>
        [HarmonyPatch(typeof(UIPlayerNames), nameof(UIPlayerNames.UpdateName))]
        static class SeatTag
        {
            static void Postfix(UIPlayerNames __instance, int index, Vector3 position)
            {
                if (!VirtualActors.CanUse || index < 0 || index >= __instance.playerNameText.Length) return;
                PlayerName tag = __instance.playerNameText[index];
                Character c = tag != null && tag.characterInteractable != null ? tag.characterInteractable.character : null;
                if (c == null || !Seats.TryGetSeat(c, out int seat)) return;
                string label = $"P{seat + 1}";
                if (tag.text.text != label) tag.text.text = label;
                Color color = Seats.Color(seat);
                if (tag.text.color != color)
                {
                    tag.text.color = color;
                    tag.text.fontStyle = TMPro.FontStyles.Bold;
                    tag.text.enableAutoSizing = false;
                    tag.text.fontSize = TagFontSize;
                    tag.text.enableWordWrapping = false;
                    tag.text.alignment = TMPro.TextAlignmentOptions.Center;
                    tag.text.outlineWidth = 0.3f;
                    tag.text.outlineColor = new Color32(0, 0, 0, 230);
                }
                // acima da cabeça, não no meio dela (o ponto do jogo é para o nome comprido com ícone de voz)
                if (MainCamera.instance != null)
                {
                    Vector3 screen = MainCamera.instance.cam.WorldToScreenPoint(position + Vector3.up * 0.55f);
                    if (screen.z > 0f) tag.transform.position = screen;
                }
                if (tag.audioImage != null && tag.audioImage.gameObject.activeSelf) tag.audioImage.gameObject.SetActive(false);
                if (tag.hostStar != null && tag.hostStar.activeSelf) tag.hostStar.SetActive(false);
            }
        }

        static void ShowFor(IsLookedAt look, Character target, int viewer)
        {
            GUIManager gui = GUIManager.instance;
            if (gui == null || Character.localCharacter == null || target == Character.localCharacter) return;
            var key = (look, viewer);
            if (!index.TryGetValue(key, out int i))
            {
                if (pending.Add(key)) Register(gui.playerNames, look, key);
                return;
            }
            bool visible = false;
            if (!look.mouth.character.data.isBlind && !look.mouth.character.refs.afflictions.isStruggling)
            {
                Transform cam = MainCamera.instance.transform;
                float dist = Vector3.Distance(cam.position, look.transform.position);
                float angle = Vector3.Angle(cam.forward, look.transform.position - cam.position);
                visible = dist < look.visibleDistance && angle < look.visibleAngle + (look.visibleDistance - dist) / look.visibleDistance * look.angleDistRatio;
            }
            gui.playerNames.UpdateName(i, look.playerNamePos.position, visible, look.mouth.amplitudeIndex);
        }

        static void Register(UIPlayerNames names, IsLookedAt look, (IsLookedAt, int) key)
        {
            // o HUD do jogo tem espaço para os nomes de um grupo do online: com até 9 outros, cria os que faltam
            if (names.indexCounter >= names.playerNameText.Length)
            {
                PlayerName model = names.playerNameText[names.playerNameText.Length - 1];
                PlayerName extra = Object.Instantiate(model, model.transform.parent);
                var grown = new PlayerName[names.playerNameText.Length + 1];
                names.playerNameText.CopyTo(grown, 0);
                grown[grown.Length - 1] = extra;
                names.playerNameText = grown;
            }
            names.Init(look.characterInteractible, result => { index[key] = result; pending.Remove(key); });
        }
    }
}
