using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PeakCoop.Local
{
    /// <summary>
    /// Com a tela dividida, cada visão desenha a cena de novo: o custo cresce com o número de câmeras, não de pixels.
    /// Enquanto houver mais de uma, o mod corta o que pesa e pouco se nota numa parte pequena da tela (distância e
    /// cascatas de sombra, resolução do mapa de sombra, nível de detalhe), só na memória: as configurações de vídeo da
    /// pessoa não mudam, e voltam inteiras com um jogador só. O tamanho de renderização não é reduzido, para o HUD de
    /// cada um continuar nítido.
    /// </summary>
    static class SplitGraphics
    {
        static int appliedFor = 1;
        static bool saved;
        static float shadowDistance, lodBias;
        static int cascades, shadowRes;

        public static void Apply(int views)
        {
            int tier = views <= 1 ? 1 : views <= 4 ? 2 : views <= 7 ? 5 : 8;
            if (tier == appliedFor) return;
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)) return;
            if (!saved)
            {
                shadowDistance = urp.shadowDistance;
                cascades = urp.shadowCascadeCount;
                shadowRes = urp.mainLightShadowmapResolution;
                lodBias = QualitySettings.lodBias;
                saved = true;
            }
            appliedFor = tier;
            if (tier == 1)
            {
                urp.shadowDistance = shadowDistance;
                urp.shadowCascadeCount = cascades;
                urp.mainLightShadowmapResolution = shadowRes;
                QualitySettings.lodBias = lodBias;
                saved = false;
                Plugin.Log.LogInfo("Vídeo: um jogador, as configurações da pessoa de volta");
                return;
            }
            float dist = tier == 2 ? 75f : tier == 5 ? 45f : 30f;
            urp.shadowDistance = Mathf.Min(shadowDistance, dist);
            urp.shadowCascadeCount = 1;
            urp.mainLightShadowmapResolution = Mathf.Min(shadowRes, tier == 2 ? 2048 : 1024);
            QualitySettings.lodBias = Mathf.Min(lodBias, tier == 2 ? 0.7f : tier == 5 ? 0.5f : 0.4f);
            Plugin.Log.LogInfo($"Vídeo: {views} visões, sombras até {urp.shadowDistance} m, detalhe {QualitySettings.lodBias}");
        }

        /// <summary>A pessoa mudou o vídeo no menu: a próxima divisão parte das escolhas novas.</summary>
        public static void Forget() { saved = false; appliedFor = 1; }
    }
}
