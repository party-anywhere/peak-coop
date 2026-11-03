using HarmonyLib;
using TMPro;
using UnityEngine;

namespace PeakCoop.Local
{
    /// <summary>
    /// Pedaços do mundo que o jogo decide pela câmera de quem hospeda e que, com várias câmeras, precisam valer para
    /// todas; e o nome do modo no menu.
    /// </summary>
    static class SharedWorld
    {
        /// <summary>
        /// O jogo apaga as luzes (lanternas, fogueiras, tochas) a mais de 50 m da câmera do jogador local, para
        /// economizar. Com o sofá espalhado pela montanha, a luz de um apagaria na tela de outro: com mais de uma
        /// câmera, ficam acesas.
        /// </summary>
        [HarmonyPatch(typeof(LightCuller), "OnEnable")]
        static class KeepLightsOn
        {
            static bool Prefix(LightCuller __instance)
            {
                if (!VirtualActors.CanUse) return true;
                Light l = __instance.GetComponent<Light>();
                if (l != null) l.enabled = true;
                return false;
            }
        }

        /// <summary>No menu inicial, o "Sozinho" é o co-op local do Party Anywhere (com o Hub conectado).</summary>
        [HarmonyPatch(typeof(MainMenu), "Start")]
        static class SoloIsCoop
        {
            static void Postfix(MainMenu __instance)
            {
                if (Plugin.Hub == null || __instance.playSoloButton == null) return;
                foreach (TMP_Text t in __instance.playSoloButton.GetComponentsInChildren<TMP_Text>(true))
                {
                    // o jogo traduz o texto pela tabela de idiomas: o componente que traduz sai, o texto fica
                    foreach (MonoBehaviour mb in t.GetComponents<MonoBehaviour>())
                        if (mb.GetType().Name.Contains("Locali")) mb.enabled = false;
                    t.text = "CO-OP LOCAL";
                }
            }
        }
    }
}
