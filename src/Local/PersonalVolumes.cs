using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakCoop.Local
{
    /// <summary>
    /// Efeitos de tela de um jogador só (veneno, açúcar, desmaio, queda, desfoque do menu…). No PEAK eles são volumes de
    /// pós-processamento globais, que valem para toda câmera: com uma câmera por jogador, o veneno de um tingiria a tela
    /// de todos. Antes de cada câmera desenhar, cada volume pessoal fica com o peso que tem para o dono daquela câmera
    /// (o dele, se é do HUD dele; zero, se é do HUD de outro), e o peso de verdade volta ao fim do quadro.
    /// </summary>
    static class PersonalVolumes
    {
        /// <summary>Volume pessoal → peso dele para uma vaga (o jogo calcula para quem hospeda; o mod, para as outras).</summary>
        static readonly Dictionary<Volume, System.Func<int, float>> world = new Dictionary<Volume, System.Func<int, float>>();
        static readonly Dictionary<Volume, float> real = new Dictionary<Volume, float>();
        static readonly List<Volume> hudVolumes = new List<Volume>();
        static readonly Dictionary<Volume, int> hudOwner = new Dictionary<Volume, int>();
        static int scannedFrame = -1;
        static bool installed;

        public static void Install()
        {
            if (installed) return;
            installed = true;
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
            RenderPipelineManager.endContextRendering += (ctx, cams) => Restore();
        }

        /// <summary>Os volumes do HUD de cada vaga (o do jogo e as cópias): pessoais do dono do HUD.</summary>
        static void Scan()
        {
            if (Time.frameCount - scannedFrame < 30) return;
            scannedFrame = Time.frameCount;
            hudVolumes.Clear();
            hudOwner.Clear();
            Add(SeatHud.Gui(Seats.Host), Seats.Host);
            foreach (ExtraSeat s in SeatContext.Extras) Add(SeatHud.Gui(s.Seat), s.Seat);
        }

        static void Add(GUIManager gui, int seat)
        {
            if (gui == null) return;
            foreach (Volume v in gui.GetComponentsInChildren<Volume>(true))
            {
                hudVolumes.Add(v);
                hudOwner[v] = seat;
            }
        }

        static void BeforeCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (!VirtualActors.CanUse) return;
            MainCamera main = cam.GetComponent<MainCamera>();
            if (main == null) return;
            int owner = SeatCameras.SeatOf(main);
            Scan();
            foreach (Volume v in hudVolumes)
            {
                if (v == null) continue;
                if (!real.ContainsKey(v)) real[v] = v.weight;
                v.weight = hudOwner[v] == owner ? real[v] : 0f;
            }
            foreach (var pair in world)
            {
                Volume v = pair.Key;
                if (v == null) continue;
                if (!real.ContainsKey(v)) real[v] = v.weight;
                float w = owner == Seats.Host ? real[v] : pair.Value(owner);
                v.weight = w;
                v.enabled = w > 0.0001f;
            }
        }

        static void Restore()
        {
            foreach (var pair in real)
                if (pair.Key != null)
                {
                    pair.Key.weight = pair.Value;
                    if (world.ContainsKey(pair.Key)) pair.Key.enabled = pair.Value > 0.0001f;
                }
            real.Clear();
        }

        static Character CharacterOf(int seat) => seat == Seats.Host ? null : SeatContext.State(seat).Character;

        // ---- volumes do mundo que o jogo liga só para o jogador local ----

        /// <summary>Escurecer ao desmaiar: o peso é o quanto o dono da câmera está desmaiando.</summary>
        [HarmonyPatch(typeof(PassOutPost), "Start")]
        static class PassOut
        {
            static void Postfix(PassOutPost __instance)
            {
                Install();
                world[__instance.vol] = seat =>
                {
                    Character c = CharacterOf(seat);
                    return c == null || c.data.fullyPassedOut ? 0f : c.data.passOutValue;
                };
            }
        }

        /// <summary>O efeito de queda: sobe enquanto o dono da câmera está caindo e desce depois, cada um no seu ritmo.</summary>
        [HarmonyPatch(typeof(FallPost), "Start")]
        static class Fall
        {
            static readonly float[] smooth = new float[PartyAnywhere.Client.MaxSeats];
            static readonly int[] lastFrame = new int[PartyAnywhere.Client.MaxSeats];

            static void Postfix(FallPost __instance)
            {
                Install();
                world[__instance.vol] = seat =>
                {
                    Character c = CharacterOf(seat);
                    if (c == null) return 0f;
                    if (lastFrame[seat] != Time.frameCount)
                    {
                        lastFrame[seat] = Time.frameCount;
                        bool falling = c.data.fallSeconds > 0f && !c.refs.movement.rocketActive;
                        smooth[seat] = Mathf.Lerp(smooth[seat], falling ? 1f : 0f, Time.deltaTime);
                    }
                    return smooth[seat];
                };
            }
        }
    }
}
