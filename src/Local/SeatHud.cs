using System.Collections.Generic;
using UnityEngine;

namespace PeakCoop.Local
{
    /// <summary>
    /// O HUD de cada escalador: uma cópia do GUIManager do jogo (stamina e status, inventário, mira, "usar", corda,
    /// arremesso, roda de emotes e da mochila, efeitos de tela de frio/calor/veneno…) rodando no contexto da vaga. Com a
    /// tela dividida, cada HUD (o do jogo e as cópias) é desenhado pela câmera do dono, dentro da parte dele.
    /// </summary>
    static class SeatHud
    {
        static readonly List<Canvas> hostCanvases = new List<Canvas>();
        static readonly Dictionary<Canvas, RenderMode> originalMode = new Dictionary<Canvas, RenderMode>();

        public static void Create(ExtraSeat s)
        {
            GUIManager source = GUIManager.instance;
            if (source == null || s.Camera == null) return;
            GameObject copy = SeatClones.Clone(s, source.gameObject, $"GUIManager P{s.Seat + 1}");
            s.Hud = copy;
            Plugin.Log.LogInfo($"P{s.Seat + 1}: HUD próprio (cópia de {Path(source.transform)}, {copy.GetComponentsInChildren<Canvas>(true).Length} canvas)");
        }

        static readonly System.Reflection.FieldInfo guiField = HarmonyLib.AccessTools.Field(typeof(GUIManager), nameof(GUIManager.instance));

        /// <summary>O GUIManager (HUD) da vaga: o do jogo para quem hospeda, a cópia para os outros.</summary>
        public static GUIManager Gui(int seat)
        {
            if (seat == Seats.Host) return SeatContext.Active ? null : GUIManager.instance;
            ExtraSeat s = SeatContext.State(seat);
            return s.Singletons.TryGetValue(guiField, out object g) ? g as GUIManager : null;
        }

        static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;

        /// <summary>Cada HUD na câmera do dono (dividida) ou como o jogo fez (um jogador só).</summary>
        public static void Layout(bool split)
        {
            GUIManager host = HostGui();
            Camera hostCam = MainCamera.instance != null ? MainCamera.instance.GetComponent<Camera>() : null;
            if (host != null)
            {
                if (SplitScreen.OnPhone(Seats.Host) && hostCam != null) PlaceOnPhone(host.gameObject, hostCam);
                else Place(host.gameObject, split ? hostCam : null);
            }
            foreach (ExtraSeat s in SeatContext.Extras)
            {
                if (s.Hud == null) continue;
                Camera cam = s.Camera != null ? s.Camera.GetComponent<Camera>() : null;
                if (SplitScreen.OnPhone(s.Seat) && cam != null) PlaceOnPhone(s.Hud, cam);
                else Place(s.Hud, cam);
            }
        }

        /// <summary>Quem joga pelo celular: o HUD dele é desenhado pela câmera dele, dentro da imagem que vai ao celular.</summary>
        static void PlaceOnPhone(GameObject hud, Camera cam)
        {
            foreach (Canvas c in hud.GetComponentsInChildren<Canvas>(true))
            {
                if (!c.isRootCanvas) continue;
                if (!originalMode.ContainsKey(c)) { originalMode[c] = c.renderMode; originalCamera[c] = c.worldCamera; }
                Camera own = originalCamera[c];
                if (originalMode[c] == RenderMode.WorldSpace || (own != null && own.GetComponent<MainCamera>() == null)) continue;
                if (c.renderMode != RenderMode.ScreenSpaceCamera) c.renderMode = RenderMode.ScreenSpaceCamera;
                if (c.worldCamera != cam) c.worldCamera = cam;
                c.planeDistance = cam.nearClipPlane + 0.05f;
                RectTransform frame = Frame(c);
                Fit(frame, c, new Rect(0f, 0f, 1f, 1f), 1f);
                laidOut.Remove(frame); // ao voltar para a TV, a moldura se recalcula
            }
        }

        static GUIManager HostGui() => SeatContext.Active ? null : GUIManager.instance;

        static readonly Dictionary<Canvas, RectTransform> frames = new Dictionary<Canvas, RectTransform>();
        static readonly Dictionary<Canvas, Camera> originalCamera = new Dictionary<Canvas, Camera>();

        /// <summary>
        /// Com a tela dividida, cada canvas do HUD vira overlay (desenhado na resolução da tela, por cima de tudo, e não
        /// dentro da imagem da câmera, que o jogo pode renderizar menor e esticar: era o que borrava o HUD) e tudo o
        /// que ele tem vai para uma moldura do tamanho da parte do dono. A moldura monta o HUD num tamanho virtual
        /// maior e encolhe no fim, sem encolher tanto quanto a parte: com 10 telas o texto continua legível.
        /// </summary>
        static void Place(GameObject hud, Camera cam)
        {
            foreach (Canvas c in hud.GetComponentsInChildren<Canvas>(true))
            {
                if (!c.isRootCanvas) continue;
                if (!originalMode.ContainsKey(c)) { originalMode[c] = c.renderMode; originalCamera[c] = c.worldCamera; }
                // canvas de outra câmera (a do herói, das cenas): fica como o jogo fez
                Camera own = originalCamera[c];
                if (originalMode[c] == RenderMode.WorldSpace || (own != null && own.GetComponent<MainCamera>() == null)) continue;
                RectTransform frame = Frame(c);
                if (cam == null)
                {
                    if (c.renderMode != originalMode[c]) c.renderMode = originalMode[c];
                    if (originalMode[c] == RenderMode.ScreenSpaceCamera && MainCamera.instance != null)
                        c.worldCamera = MainCamera.instance.GetComponent<Camera>();
                    Fit(frame, c, new Rect(0f, 0f, 1f, 1f), 1f);
                    continue;
                }
                if (c.renderMode != RenderMode.ScreenSpaceOverlay) c.renderMode = RenderMode.ScreenSpaceOverlay;
                Rect part = cam.rect;
                float r = Mathf.Min(part.width, part.height);
                // o HUD do PEAK precisa de ~1250 de largura (barra de stamina à esquerda, inventário à direita) para as
                // duas partes não se encostarem: nas partes estreitas (7+ jogadores), encolhe o quanto for preciso
                float fitsWidth = Screen.width * part.width / Mathf.Max(c.scaleFactor, 0.0001f) / MinHudWidth;
                Fit(frame, c, part, Mathf.Clamp(Mathf.Min(Mathf.Sqrt(r) * 1.1f, fitsWidth), 0.38f, 1f));
            }
        }

        /// <summary>A moldura do canvas: um filho que recebe tudo o que o canvas tinha.</summary>
        static RectTransform Frame(Canvas c)
        {
            if (frames.TryGetValue(c, out RectTransform f) && f != null) return f;
            // a cópia do HUD de uma vaga já traz a moldura do HUD de onde foi copiada
            if (c.transform.Find("PeakCoop frame") is RectTransform copied) return frames[c] = copied;
            var go = new GameObject("PeakCoop frame", typeof(RectTransform));
            f = (RectTransform)go.transform;
            f.SetParent(c.transform, false);
            var children = new List<Transform>();
            foreach (Transform child in c.transform) if (child != f) children.Add(child);
            foreach (Transform child in children) child.SetParent(f, false);
            f.pivot = new Vector2(0.5f, 0.5f);
            frames[c] = f;
            return f;
        }

        /// <summary>
        /// A moldura ocupa <paramref name="part"/> da tela; o HUD dentro dela é montado <c>1/scale</c> maior e encolhido.
        /// Em overlay, o mundo do canvas é a tela em pixels: a moldura vai direto para lá (o retângulo do canvas de uma
        /// cópia pode ficar com o tamanho de quando ela era desenhada pela câmera da parte).
        /// </summary>
        static void Fit(RectTransform f, Canvas c, Rect part, float scale)
        {
            f.pivot = new Vector2(0.5f, 0.5f);
            f.localScale = new Vector3(scale, scale, 1f);
            // nada do HUD de um sai da parte dele (a máscara do binóculo é uma imagem 10x maior que a tela, as telas
            // de morte e de desmaio cobrem tudo)
            var clip = f.GetComponent<UnityEngine.UI.RectMask2D>() ?? f.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            bool whole = part.width >= 0.999f && part.height >= 0.999f;
            if (clip.enabled == whole) clip.enabled = !whole;
            if (c.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                f.anchorMin = Vector2.zero;
                f.anchorMax = Vector2.one;
                f.anchoredPosition = Vector2.zero;
                f.sizeDelta = Vector2.zero;
                return;
            }
            Vector2 px = new Vector2(Screen.width * part.width, Screen.height * part.height);
            float unit = Mathf.Max(c.scaleFactor, 0.0001f) * scale;
            Vector2 size = px / unit;
            f.anchorMin = f.anchorMax = new Vector2(0.5f, 0.5f);
            f.position = new Vector3(Screen.width * part.center.x, Screen.height * part.center.y, f.position.z);
            if ((f.sizeDelta - size).sqrMagnitude > 0.25f || !laidOut.Contains(f))
            {
                f.sizeDelta = size;
                // na cópia do HUD, os filhos não se reposicionam sozinhos com a moldura nova: desligar e religar
                // a moldura recalcula todos (uma vez, quando a divisão muda)
                if (f.gameObject.activeSelf)
                {
                    f.gameObject.SetActive(false);
                    f.gameObject.SetActive(true);
                }
                laidOut.Add(f);
            }
        }

        static readonly HashSet<RectTransform> laidOut = new HashSet<RectTransform>();

        const float MinHudWidth = 1250f;
    }
}
