using UnityEngine.UI;
using System.Text;
using PeakCoop.Local;
using UnityEngine;

namespace PeakCoop.Dev
{
    /// <summary>Só para desenvolver: a cada 8 s, onde está cada escalador, a câmera e o HUD dele.</summary>
    class SeatDiag : MonoBehaviour
    {
        float next;

        string dumpedScene;
        bool dumpedElevators;

        void Update()
        {
            if (Time.unscaledTime < next || !VirtualActors.CanUse) return;
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (scene != dumpedScene)
            {
                dumpedScene = scene;
                var dump = new StringBuilder($"Cena {scene}: objetos de nascer/elevador");
                foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    string n = t.name.ToLowerInvariant();
                    if (n.Contains("levator") || n.Contains("spawn") || n.Contains("elev"))
                        dump.Append($"\n  {t.name} pos={t.position:F1} pai={(t.parent != null ? t.parent.name : "-")}");
                }
                Plugin.Log.LogInfo(dump.ToString());
            }
            next = Time.unscaledTime + 4f;
            var sb = new StringBuilder($"Diag: fps={1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f):F0} fixo={Time.fixedDeltaTime:F3} max={Time.maximumDeltaTime:F2} escaladores={Character.AllCharacters.Count} memória={System.GC.GetTotalMemory(false) / 1048576} MB (gerenciada)");
            if (SpawnPoint.allSpawnPoints.Count > 0) sb.Append($"\n  ponto1={SpawnPoint.GetSpawnPoint(1).transform.position:F2} pai={SpawnPoint.GetSpawnPoint(1).transform.parent?.name}");
            foreach (Transform t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name == "Elevator" || t.name.StartsWith("Elevator ("))
                {
                    sb.Append($"\n  {t.name} pos={t.position:F2}");
                    if (!dumpedElevators)
                        foreach (Component c in t.GetComponentsInChildren<Component>())
                            if (!(c is Transform) && !(c is Renderer) && !(c is MeshFilter)) sb.Append($" [{c.gameObject.name}:{c.GetType().Name}]");
                }
            dumpedElevators = true;
            foreach (Player p in FindObjectsByType<Player>(FindObjectsSortMode.None))
                sb.Append($"\n  inventário {p.name} ator={p.photonView.OwnerActorNr} local={p == Player.localPlayer} [{string.Join(",", System.Array.ConvertAll(p.itemSlots, i => i.IsEmpty() ? "-" : i.GetPrefabName()))}] personagem={(p.character != null ? p.character.name : "-")}");
            Character host = Character.localCharacter;
            if (host != null) sb.Append($"\n  P1 {Describe(host)} cam={Cam(MainCamera.instance)}");
            foreach (ExtraSeat s in SeatContext.Extras)
            {
                sb.Append($"\n  P{s.Seat + 1} {(s.Character != null ? Describe(s.Character) : "sem personagem")} cam={Cam(s.Camera)}");
                Interaction it = s.InteractionObject != null ? s.InteractionObject.GetComponent<Interaction>() : null;
                if (it != null) sb.Append($" olhando={it.bestInteractableName}");
                if (s.Player != null) sb.Append($" itens=[{string.Join(",", System.Array.ConvertAll(s.Player.itemSlots, i => i.IsEmpty() ? "-" : i.GetPrefabName()))}]");
                if (s.Hud != null)
                    foreach (Canvas c in s.Hud.GetComponentsInChildren<Canvas>(true))
                        if (c.isRootCanvas)
                            sb.Append($"\n     hud {c.name} ativo={c.gameObject.activeInHierarchy} on={c.enabled} modo={c.renderMode} cam={(c.worldCamera != null ? c.worldCamera.name : "-")} ordem={c.sortingOrder} escala={c.scaleFactor:F2} rect={((RectTransform)c.transform).rect.size} lossy={c.transform.lossyScale.x:F2}" + Frame(c));
            }
            Plugin.Log.LogInfo(sb.ToString());
        }

        static string Frame(Canvas c)
        {
            Transform f = c.transform.Find("PeakCoop frame");
            if (f == null) return " sem moldura";
            var r = (RectTransform)f;
            var sb = new StringBuilder($" moldura pos={r.position:F0} tam={r.rect.size:F0} lossy={r.lossyScale.x:F2} filhos={r.childCount} restantes={c.transform.childCount - 1}");
            foreach (Transform ch in r)
            {
                var cr = ch as RectTransform;
                if (cr == null) continue;
                sb.Append($"\n        filho {ch.name} min={cr.anchorMin} max={cr.anchorMax} delta={cr.sizeDelta:F0} rect={cr.rect.size:F0} escala={cr.localScale.x:F2}");
                foreach (Transform g in ch)
                    if (g is RectTransform gr && (g.name == "BarGroup" || g.name == "Inventory"))
                    {
                        Canvas own = g.GetComponent<Canvas>();
                        var corners = new Vector3[4];
                        gr.GetWorldCorners(corners);
                        sb.Append($"\n          neto {g.name} mundo={corners[0]:F0}..{corners[2]:F0} canvasProprio={(own != null ? own.renderMode + "/" + own.overrideSorting : "-")}");
                        foreach (Graphic gfx in g.GetComponentsInChildren<Graphic>())
                        {
                            gfx.rectTransform.GetWorldCorners(corners);
                            sb.Append($"\n            {gfx.name} {corners[0]:F0}..{corners[2]:F0} canvas={gfx.canvas?.name}");
                            break;
                        }
                    }
            }
            return sb.ToString();
        }

        static string Describe(Character c) =>
            $"pos={c.Center:F1} chão={c.data.isGrounded} desmaiado={c.data.fullyPassedOut} morto={c.data.dead} ragdoll={c.data.currentRagdollControll:F2}";

        static string Cam(MainCamera m)
        {
            if (m == null) return "-";
            Camera c = m.GetComponent<Camera>();
            return $"{m.transform.position:F1} rect={c.rect} mask={c.cullingMask:X}";
        }
    }
}
