using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakCoop.Local
{
    /// <summary>
    /// Cada câmera esconde só o corpo do dono dela (e o amigo que ele carrega nas costas), como o PEAK faz para o
    /// jogador local. O jogo esconde o corpo "achatando" a malha no shader (_VertexGhost); aqui isso é decidido antes
    /// de cada câmera desenhar (o URP desenha uma câmera depois da outra), em vez de uma vez por quadro.
    /// </summary>
    static class BodyPerCamera
    {
        static readonly int VertexGhost = Shader.PropertyToID("_VertexGhost");
        static readonly List<HideTheBody> bodies = new List<HideTheBody>();
        static readonly Dictionary<HideTheBody, Material[]> materials = new Dictionary<HideTheBody, Material[]>();
        static readonly Dictionary<HideTheBody, bool> hidden = new Dictionary<HideTheBody, bool>();
        static bool installed;

        public static void Install()
        {
            if (installed) return;
            installed = true;
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
        }

        /// <summary>O jogo não decide mais sozinho: quem decide é a câmera que vai desenhar.</summary>
        [HarmonyPatch(typeof(HideTheBody), "Update")]
        static class TakeOver
        {
            static bool Prefix(HideTheBody __instance)
            {
                // só com mais de uma câmera; um jogador sozinho é o jogo de sempre
                if (!Split())
                {
                    if (bodies.Count > 0) GiveBack();
                    return true;
                }
                Install();
                if (!materials.ContainsKey(__instance)) Track(__instance);
                return false;
            }
        }

        /// <summary>Há alguma câmera de escalador extra desenhando (senão, só a do jogo).</summary>
        static bool Split() => VirtualActors.CanUse && SeatCameras.Any;

        /// <summary>Voltou a um jogador só: todos os corpos aparecendo, e o jogo decide de novo a partir daí.</summary>
        static void GiveBack()
        {
            foreach (HideTheBody b in bodies)
            {
                if (b == null) continue;
                foreach (Material m in materials[b]) if (m != null) m.SetFloat(VertexGhost, 0f);
                b.isShowing = true;
            }
            bodies.Clear();
            materials.Clear();
            hidden.Clear();
        }

        static void Track(HideTheBody b)
        {
            var list = new List<Material>();
            void Add(Renderer r) { if (r != null) list.AddRange(r.materials); }
            Add(b.body); Add(b.headRend); Add(b.sash); Add(b.medal);
            foreach (Renderer r in b.costumes) Add(r);
            if (b.face != null) foreach (Renderer r in b.face.GetComponentsInChildren<Renderer>(true)) Add(r);
            if (b.refs != null) foreach (Renderer r in b.refs.playerHats) Add(r);
            materials[b] = list.ToArray();
            bodies.Add(b);
            // a sombra vem do próprio corpo, que agora aparece para as outras câmeras
            if (b.shadowCaster != null) b.shadowCaster.SetActive(false);
            if (b.shadowCasterHat != null) b.shadowCasterHat.SetActive(false);
            b.isShowing = true;
        }

        static void BeforeCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (bodies.Count == 0 || !Split()) return;
            Character owner = OwnerOf(cam);
            for (int i = bodies.Count - 1; i >= 0; i--)
            {
                HideTheBody b = bodies[i];
                if (b == null) { bodies.RemoveAt(i); continue; }
                if (b.character == null) continue;
                Character c = b.character;
                bool hide = owner != null && !b.isDummy
                    && ((c == owner && !c.data.fullyPassedOut && !c.data.dead) || (c != owner && c.data.carrier == owner));
                if (hidden.TryGetValue(b, out bool was) && was == hide) continue;
                hidden[b] = hide;
                foreach (Material m in materials[b]) if (m != null) m.SetFloat(VertexGhost, hide ? 1f : 0f);
            }
        }

        static Character OwnerOf(Camera cam)
        {
            MainCamera main = cam.GetComponent<MainCamera>();
            if (main == null) return null;
            int seat = SeatCameras.SeatOf(main);
            // a câmera do jogo é de quem hospeda
            if (seat == Seats.Host) return SeatContext.Active ? null : Character.localCharacter;
            return SeatContext.State(seat).Character;
        }
    }
}
