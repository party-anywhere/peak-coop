using PeakCoop.Local;
using UnityEngine;

namespace PeakCoop.Dev
{
    /// <summary>
    /// Só para desenvolver: leva o P2 até a mala fechada mais perto, olhando para ela, a cada 20 s na ilha (ou até um item no chão, para testar pegar e o inventário). Com o
    /// `pa phone-sim` apertando "Usar" (X), confere no log se o P2 enxerga a mala, abre e pega o que saiu dela.
    /// </summary>
    class DevScenario : MonoBehaviour
    {
        float next = -1f;

        /// <summary>Em vez da mala: junta todos em fila na frente do P1, uns olhando para os outros (placas de nome).</summary>
        public static bool Group;

        void Update()
        {
            if (!VirtualActors.CanUse || !GameHandler.IsOnIsland) { next = -1f; return; }
            if (next < 0f) next = Time.unscaledTime + 25f;
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 20f;
            if (Group) { Gather(); return; }
            // o primeiro escalador extra com controle de verdade (vaga ativa no Hub)
            ExtraSeat p2 = null;
            foreach (ExtraSeat s in SeatContext.Extras)
                if (s.Character != null && Plugin.Hub != null && Plugin.Hub.GetSeatState(s.Seat) == PartyAnywhere.SeatState.Active) { p2 = s; break; }
            Character c = p2?.Character;
            if (c == null || c.data.fullyPassedOut) return;
            // primeiro um item no chão perto (testa pegar e o inventário); senão, a mala fechada mais perto
            Component best = null;
            Vector3 target = default;
            float bestDist = 25f;
            foreach (Item it in Item.ALL_ACTIVE_ITEMS)
            {
                if (it == null || it.itemState != ItemState.Ground || it is Backpack || it.name.StartsWith("BingBong")) continue;
                float d = Vector3.Distance(it.transform.position, c.Center);
                if (d < bestDist) { bestDist = d; best = it; target = it.Center(); }
            }
            if (best == null)
            {
                bestDist = float.MaxValue;
                foreach (Luggage l in FindObjectsByType<Luggage>(FindObjectsSortMode.None))
                {
                    float d = Vector3.Distance(l.transform.position, c.Center);
                    if (!l.IsOpen && d < bestDist) { bestDist = d; best = l; target = l.Center(); }
                }
            }
            if (best == null) return;
            Vector3 flat = Vector3.ProjectOnPlane(target - c.Center, Vector3.up).normalized;
            Vector3 stand = target - flat * (best is Item ? 1.0f : 1.3f) + Vector3.up * 0.8f;
            SeatContext.Run(p2.Seat, () =>
            {
                c.WarpPlayer(stand, false);
                Vector3 dir = (target - (stand + Vector3.up * 0.6f)).normalized;
                c.data.lookValues = new Vector2(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, Mathf.Asin(dir.y) * Mathf.Rad2Deg);
                c.RecalculateLookDirections();
            });
            if (best is Item item) StartCoroutine(PickUp(p2, item));
            Plugin.Log.LogInfo($"Cenário: P{p2.Seat + 1} levado até {best.name} em {target:F1}; itens antes: [{string.Join(",", System.Array.ConvertAll(p2.Player.itemSlots, i => i.IsEmpty() ? "-" : i.GetPrefabName()))}]");
        }

        static void Gather()
        {
            Character host = Character.localCharacter;
            if (host == null) return;
            Vector3 fwd = Vector3.ProjectOnPlane(host.data.lookDirection, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            int i = 0;
            foreach (ExtraSeat s in SeatContext.Extras)
            {
                Character c = s.Character;
                if (c == null) continue;
                Vector3 at = host.Center + fwd * 4f + right * ((i++ - 1.5f) * 1.4f) + Vector3.up * 0.5f;
                SeatContext.Run(s.Seat, () =>
                {
                    c.WarpPlayer(at, false);
                    Vector3 dir = (host.Center - at).normalized;
                    c.data.lookValues = new Vector2(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 0f);
                    c.RecalculateLookDirections();
                });
            }
            Plugin.Log.LogInfo("Cenário: todos na frente do P1");
        }

        /// <summary>Um instante depois de chegar, o "Usar" da vaga no item que ela está olhando; depois, o inventário dela.</summary>
        System.Collections.IEnumerator PickUp(ExtraSeat s, Item item)
        {
            yield return new WaitForSeconds(0.6f);
            Interaction it = s.InteractionObject != null ? s.InteractionObject.GetComponent<Interaction>() : null;
            string seen = it?.bestInteractable == null ? "nada" : it.bestInteractable.GetTransform().name;
            if (item != null && s.Character != null)
                SeatContext.Run(s.Seat, () => item.Interact(s.Character));
            yield return new WaitForSeconds(1.2f);
            Plugin.Log.LogInfo($"Cenário: P{s.Seat + 1} olhava {seen}, usou {(item != null ? item.name : "?")}; itens agora: [{string.Join(",", System.Array.ConvertAll(s.Player.itemSlots, i => i.IsEmpty() ? "-" : i.GetPrefabName()))}] mochila={(s.Player.backpackSlot.IsEmpty() ? "-" : "sim")}");
        }
    }
}
