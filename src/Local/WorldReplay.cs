using System;
using System.Collections.Generic;
using HarmonyLib;
using Peak;
using UnityEngine;

namespace PeakCoop.Local
{
    /// <summary>
    /// Perigos e zonas do mundo que, a cada quadro, aplicam o efeito só "em mim" (Character.localCharacter): no
    /// online cada PC roda isso para o seu escalador. Aqui o jogo roda uma vez (para quem hospeda) e o mod repete só a
    /// parte "aplicar em mim" para cada escalador do sofá, no contexto dele, sem repetir a parte do mundo (a névoa
    /// subindo, o vento trocando, o fogo queimando). O estado que é por jogador (quem está dentro, há quanto tempo)
    /// fica guardado por vaga.
    /// </summary>
    static class WorldReplay
    {
        /// <summary>Cada vaga extra com escalador vivo no mundo, já no contexto dela.</summary>
        static void ForEachSeat(Action<Character> action)
        {
            if (SeatContext.Active) return;
            foreach (ExtraSeat s in SeatContext.Extras)
            {
                if (s.Character == null) continue;
                Character c = s.Character;
                SeatContext.Run(s.Seat, () =>
                {
                    try { action(c); }
                    catch (Exception e) { Once($"{action.Method.DeclaringType?.Name}: {e.Message}"); }
                });
            }
        }

        static readonly HashSet<string> logged = new HashSet<string>();

        static void Once(string message)
        {
            if (logged.Add(message)) Plugin.Log.LogWarning($"Perigo por jogador: {message}");
        }

        /// <summary>Estado por (objeto, vaga): campos privados do jogo que dizem "eu" e precisam de uma cópia por vaga.</summary>
        static readonly Dictionary<(int, int), object[]> saved = new Dictionary<(int, int), object[]>();

        /// <summary>Roda <paramref name="body"/> com os campos trocados pela cópia da vaga (e guarda a cópia de volta).</summary>
        static void WithSeatFields(UnityEngine.Object owner, int seat, System.Reflection.FieldInfo[] fields, Action body)
        {
            var key = (owner.GetInstanceID(), seat);
            object[] host = new object[fields.Length];
            for (int i = 0; i < fields.Length; i++) host[i] = fields[i].GetValue(owner);
            if (saved.TryGetValue(key, out object[] mine))
                for (int i = 0; i < fields.Length; i++) fields[i].SetValue(owner, mine[i]);
            try { body(); }
            finally
            {
                mine = new object[fields.Length];
                for (int i = 0; i < fields.Length; i++) { mine[i] = fields[i].GetValue(owner); fields[i].SetValue(owner, host[i]); }
                saved[key] = mine;
            }
        }

        static System.Reflection.FieldInfo[] Fields(Type t, params string[] names) =>
            Array.ConvertAll(names, n => AccessTools.Field(t, n));

        // ---- sol forte (Mesa/deserto) ----
        [HarmonyPatch(typeof(HotSun), "Update")]
        static class HotSunPatch
        {
            static void Postfix(HotSun __instance) => ForEachSeat(_ => __instance.Update());
        }

        // ---- vento frio (Alpino): o status e o empurrão; o tempo do vento é do mundo ----
        [HarmonyPatch(typeof(WindChillZone), "Update")]
        static class WindChillStatus
        {
            static void Postfix(WindChillZone __instance) => ForEachSeat(c =>
            {
                if (__instance.windActive && __instance.windZoneBounds.Contains(c.Center)) __instance.ApplyStatus(c);
            });
        }

        [HarmonyPatch(typeof(WindChillZone), "FixedUpdate")]
        static class WindChillForce
        {
            static void Postfix(WindChillZone __instance) => ForEachSeat(c =>
            {
                if (__instance.windActive && __instance.timeSpentWaiting >= __instance.delayBeforeForce && __instance.windZoneBounds.Contains(c.Center))
                    __instance.AddWindForceToCharacter(c, __instance.currentForceMult);
            });
        }

        // ---- a névoa que sobe: frio (ou ferimento no esqueleto) para quem ficou abaixo dela ----
        [HarmonyPatch(typeof(Fog), "Update")]
        static class FogPatch
        {
            static void Postfix(Fog __instance) => ForEachSeat(c =>
            {
                if (__instance.stops == null) return;
                __instance.MakePlayerCold();
                c.data.isInFog = c.Center.y < __instance.transform.position.y;
            });
        }

        // ---- lava: calor por perto e o pulo para fora ao encostar ----
        [HarmonyPatch(typeof(Lava), "Update")]
        static class LavaHeat
        {
            static void Postfix(Lava __instance) => ForEachSeat(_ => __instance.Heat());
        }

        [HarmonyPatch(typeof(Lava), "FixedUpdate")]
        static class LavaEffects
        {
            static void Postfix(Lava __instance) => ForEachSeat(_ => __instance.DoEffects());
        }

        // ---- fontes de status (esporos, gás, calor, frio de pontos do mapa) ----
        [HarmonyPatch(typeof(StatusEmitter), nameof(StatusEmitter.Update))]
        static class StatusEmitterPatch
        {
            static readonly System.Reflection.FieldInfo[] perSeat = Fields(typeof(StatusEmitter), "inZone", "timeSinceLastTick", "isOverlapPriority");
            static bool replaying;

            static void Postfix(StatusEmitter __instance)
            {
                if (replaying) return;
                ForEachSeat(c => WithSeatFields(__instance, SeatContext.Current, perSeat, () =>
                {
                    replaying = true;
                    try { __instance.Update(); } finally { replaying = false; }
                }));
            }
        }

        // ---- campos de status (Gloom, zonas de veneno/frio dos biomas novos) ----
        [HarmonyPatch(typeof(StatusFieldBase), "Update")]
        static class StatusFieldPatch
        {
            static readonly System.Reflection.FieldInfo[] perSeat = Fields(typeof(StatusFieldBase), "lastEnteredTime", "inflicting", "characterInField");
            static bool replaying;

            static void Postfix(StatusFieldBase __instance)
            {
                if (replaying) return;
                ForEachSeat(c => WithSeatFields(__instance, SeatContext.Current, perSeat, () =>
                {
                    replaying = true;
                    try { __instance.Update(); } finally { replaying = false; }
                }));
            }
        }

        // ---- esfera de ferimento ----
        [HarmonyPatch(typeof(InjurySphere), "Update")]
        static class InjurySpherePatch
        {
            static void Postfix(InjurySphere __instance) => ForEachSeat(_ => __instance.Update());
        }

        // ---- orbe que dá sono a quem olha para ele (com a câmera de quem olha) ----
        [HarmonyPatch(typeof(OrbThatMakesYouSleepy), nameof(OrbThatMakesYouSleepy.UpdateHypnosis))]
        static class SleepyOrbPatch
        {
            static bool replaying;

            static void Postfix(OrbThatMakesYouSleepy __instance, bool debug)
            {
                if (replaying) return;
                ForEachSeat(_ =>
                {
                    replaying = true;
                    try { __instance.UpdateHypnosis(debug); } finally { replaying = false; }
                });
            }
        }

        // ---- casca de banana no chão: escorrega quem passar correndo por cima ----
        [HarmonyPatch(typeof(BananaPeel), "Update")]
        static class BananaPatch
        {
            static void Postfix(BananaPeel __instance) => ForEachSeat(c =>
            {
                if (__instance.item.itemState != ItemState.Ground || __instance.counter < 3f) return;
                if (Vector3.Distance(c.Center, __instance.transform.position) > 1f || !c.data.isGrounded || c.data.avarageVelocity.magnitude < 1.5f) return;
                __instance.counter = 0f;
                __instance.GetComponent<Photon.Pun.PhotonView>().RPC("RPCA_TriggerBanana", Photon.Pun.RpcTarget.All, c.refs.view.ViewID);
            });
        }

        // ---- fogueira: proteção (sem fome/frio por perto) e o alívio de quando ela acende ----
        [HarmonyPatch(typeof(Campfire), "Update")]
        static class CampfirePatch
        {
            static void Prefix(Campfire __instance, out bool __state) => __state = __instance.fireHasStarted;

            static void Postfix(Campfire __instance, bool __state)
            {
                if (!__instance.Lit) return;
                bool justLit = !__state && __instance.fireHasStarted;
                ForEachSeat(c =>
                {
                    __instance.ApplyCampfireProtection();
                    if (justLit && Vector3.Distance(__instance.transform.position, c.Center) <= __instance.moraleBoostRadius)
                    {
                        c.refs.afflictions.AdjustStatus(CharacterAfflictions.STATUSTYPE.Injury, -0.2f);
                        c.refs.afflictions.AdjustStatus(CharacterAfflictions.STATUSTYPE.Petrify, -0.2f);
                    }
                });
            }
        }
    }
}
