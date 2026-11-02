using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PeakCoop.Local
{
    /// <summary>
    /// Gatilhos e colisões do mundo (cair no vazio, espinhos, serras, machados, água-viva, rochas que tremem, zonas de
    /// status…) testam "quem encostou é o meu personagem?" (IsLocal) e aplicam em Character.localCharacter. Aqui, cada
    /// toque roda no contexto do escalador que encostou. Para o que é de um escalador (o corpo dele, o item na mão
    /// dele), vale o contexto do dono.
    /// </summary>
    static class WorldTouch
    {
        static readonly string[] triggers = { "OnTriggerEnter", "OnTriggerStay", "OnTriggerExit" };
        static readonly string[] collisions = { "OnCollisionEnter", "OnCollisionStay", "OnCollisionExit" };
        const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        public static void Install(Harmony harmony)
        {
            var onTrigger = new HarmonyMethod(typeof(WorldTouch), nameof(TriggerPrefix));
            var onCollision = new HarmonyMethod(typeof(WorldTouch), nameof(CollisionPrefix));
            var finalizer = new HarmonyMethod(typeof(WorldTouch), nameof(Finalizer));
            int count = 0;
            foreach (Type t in typeof(Character).Assembly.GetTypes())
            {
                if (!typeof(MonoBehaviour).IsAssignableFrom(t) || t.ContainsGenericParameters) continue;
                foreach (string name in triggers)
                {
                    MethodInfo m = t.GetMethod(name, Declared, null, new[] { typeof(Collider) }, null);
                    if (m != null && TryPatch(harmony, m, onTrigger, finalizer)) count++;
                }
                foreach (string name in collisions)
                {
                    MethodInfo m = t.GetMethod(name, Declared, null, new[] { typeof(Collision) }, null);
                    if (m != null && TryPatch(harmony, m, onCollision, finalizer)) count++;
                }
            }
            Plugin.Log.LogInfo($"Toques do mundo por jogador: {count} métodos");
        }

        static bool TryPatch(Harmony harmony, MethodInfo m, HarmonyMethod prefix, HarmonyMethod finalizer)
        {
            try { harmony.Patch(m, prefix: prefix, finalizer: finalizer); return true; }
            catch (Exception e) { Plugin.Log.LogWarning($"Toque: não deu para envolver {m.DeclaringType}.{m.Name}: {e.Message}"); return false; }
        }

        static void TriggerPrefix(object __instance, Collider __0, out bool __state) => __state = Enter(__instance, __0);

        static void CollisionPrefix(object __instance, Collision __0, out bool __state) => __state = Enter(__instance, __0?.collider);

        static bool Enter(object instance, Collider other)
        {
            if (!VirtualActors.CanUse) return false;
            if (instance is UnityEngine.Object o && SeatComponents.TryGetOwnerSeat(o, out int owner)) return SeatContext.Enter(owner);
            if (other == null) return false;
            Character c = other.GetComponentInParent<Character>();
            return c != null && Seats.TryGetSeat(c, out int seat) && SeatContext.Enter(seat);
        }

        static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state) SeatContext.Exit();
            return __exception;
        }
    }
}
