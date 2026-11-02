using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PeakCoop.Local
{
    /// <summary>
    /// Troca de contexto em volta de todo o código de um escalador extra: os métodos de ciclo de vida da Unity
    /// (Update, LateUpdate, FixedUpdate, Start, OnEnable, OnDisable) de cada componente dele, do Player dele e da câmera
    /// dele rodam com a vaga como "o jogador local". Os tipos são corrigidos (Harmony) na primeira vez que aparecem num
    /// escalador; o custo para os outros objetos do mesmo tipo é uma consulta num dicionário.
    /// </summary>
    static class SeatComponents
    {
        static readonly string[] lifecycle = { "Update", "LateUpdate", "FixedUpdate", "Start", "OnEnable", "OnDisable" };
        const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        static readonly Dictionary<int, int> seatByComponent = new Dictionary<int, int>();
        static readonly HashSet<MethodBase> patched = new HashSet<MethodBase>();
        static Harmony harmony;

        public static void Install(Harmony h)
        {
            harmony = h;
            // itens trocam de dono a toda hora: o contexto sai de quem segura o item na hora (ou de quem o jogou)
            foreach (Type t in typeof(Item).Assembly.GetTypes())
                if (!t.IsAbstract || t == typeof(ItemComponent))
                    if (typeof(Item).IsAssignableFrom(t) || typeof(ItemComponent).IsAssignableFrom(t) || typeof(ItemActionBase).IsAssignableFrom(t))
                        PatchType(t);
        }

        /// <summary>A vaga dona de um item: quem segura, ou quem jogou há pouco (o item no ar ainda é dele).</summary>
        static bool TryGetItemSeat(object o, out int seat)
        {
            seat = -1;
            Item item = o as Item ?? (o as ItemComponent)?.item ?? (o as ItemActionBase)?.item;
            if (item == null) return false;
            Character owner = item.holderCharacter;
            if (owner == null && item.lastThrownCharacter != null && item.photonView != null && item.photonView.IsMine)
                owner = item.lastThrownCharacter;
            return owner != null && Seats.TryGetSeat(owner, out seat);
        }

        public static void Register(GameObject root, int seat)
        {
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                seatByComponent[mb.GetInstanceID()] = seat;
                PatchType(mb.GetType());
            }
        }

        public static bool TryGetSeat(UnityEngine.Object o, out int seat) => seatByComponent.TryGetValue(o.GetInstanceID(), out seat);

        /// <summary>
        /// A vaga dona do objeto, se ele é de um escalador: componente registrado, item na mão, ou parte do corpo de
        /// alguém (o de quem hospeda dá <see cref="Seats.Host"/>). false: é do mundo.
        /// </summary>
        public static bool TryGetOwnerSeat(UnityEngine.Object o, out int seat)
        {
            if (seatByComponent.TryGetValue(o.GetInstanceID(), out seat) || TryGetItemSeat(o, out seat)) return true;
            Character c = o is Component comp ? comp.GetComponentInParent<Character>() : null;
            if (c == null) return false;
            if (!Seats.TryGetSeat(c, out seat)) seat = Seats.Host;
            return true;
        }

        static void PatchType(Type type)
        {
            for (Type t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                // o próprio mod e a Unity ficam de fora
                if (t.Assembly == typeof(SeatComponents).Assembly || t.Namespace?.StartsWith("UnityEngine") == true) continue;
                foreach (string name in lifecycle)
                {
                    MethodInfo m = t.GetMethod(name, Declared, null, Type.EmptyTypes, null);
                    if (m == null || m.IsAbstract || m.ContainsGenericParameters || !patched.Add(m)) continue;
                    try
                    {
                        harmony.Patch(m, prefix: new HarmonyMethod(typeof(SeatComponents), nameof(Prefix)),
                            finalizer: new HarmonyMethod(typeof(SeatComponents), nameof(Finalizer)));
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning($"Contexto: não deu para envolver {t.FullName}.{name}: {e.Message}");
                    }
                }
            }
        }

        static void Prefix(object __instance, out bool __state)
        {
            int seat = -1;
            __state = __instance is UnityEngine.Object o
                && (seatByComponent.TryGetValue(o.GetInstanceID(), out seat) || TryGetItemSeat(o, out seat))
                && SeatContext.Enter(seat);
        }

        static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state) SeatContext.Exit();
            return __exception;
        }
    }
}
