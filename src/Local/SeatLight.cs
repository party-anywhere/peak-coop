using HarmonyLib;
using UnityEngine;

namespace PeakCoop.Local
{
    /// <summary>
    /// A luz do peito (CharacterLight, uma luz curta que deixa o jogador se ver no escuro e na noite) só fica no corpo
    /// do jogador local: o jogo tira a dos outros. Cada escalador do sofá ganha a sua, igual à de quem hospeda, no
    /// mesmo ponto do corpo.
    /// </summary>
    static class SeatLight
    {
        const string LightName = "CharacterLight";

        public static void Ensure(ExtraSeat s, Character host)
        {
            if (s.Character == null || host == null || s.HasLight) return;
            Light source = Find(host.transform);
            if (source == null) return;
            if (Find(s.Character.transform) == null)
            {
                // o mesmo pai no corpo da vaga (pelo caminho a partir da raiz do personagem)
                Transform parent = s.Character.transform.Find(PathFrom(host.transform, source.transform.parent)) ?? s.Character.transform;
                Light mine = Object.Instantiate(source, parent, false);
                mine.name = LightName;
                mine.enabled = true;
                mine.gameObject.SetActive(true);
                Plugin.Log.LogInfo($"P{s.Seat + 1}: luz do peito ({parent.name})");
            }
            s.HasLight = true;
        }

        static Light Find(Transform root)
        {
            foreach (Light l in root.GetComponentsInChildren<Light>(true))
                if (l.name == LightName) return l;
            return null;
        }

        static string PathFrom(Transform root, Transform t)
        {
            string path = "";
            for (; t != null && t != root; t = t.parent) path = path.Length == 0 ? t.name : t.name + "/" + path;
            return path;
        }

        /// <summary>O nome do escalador (espectador, fim da expedição, carregar) é a vaga: "P2", não o nome da conta.</summary>
        [HarmonyPatch(typeof(Character), nameof(Character.characterName), MethodType.Getter)]
        static class SeatName
        {
            static void Postfix(Character __instance, ref string __result)
            {
                if (VirtualActors.CanUse && !__instance.isBot && Seats.TryGetSeat(__instance, out int seat)) __result = $"P{seat + 1}";
            }
        }
    }
}
