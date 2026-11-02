using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Photon.Pun;
using UnityEngine;
using PhotonPlayer = Photon.Realtime.Player;

namespace PeakCoop.Local
{
    /// <summary>O que é "do jogador local" no PEAK, guardado por vaga extra.</summary>
    class ExtraSeat
    {
        public readonly int Seat;
        public PhotonPlayer Actor;
        public Character Character;
        public Player Player;
        public MainCamera Camera;
        public GameObject InteractionObject;
        public GameObject Hud;
        /// <summary>As instâncias estáticas do jogo que são da vaga (os singletons da câmera, a Interaction, o HUD): campo → valor.</summary>
        public readonly Dictionary<FieldInfo, object> Singletons = new Dictionary<FieldInfo, object>();

        public ExtraSeat(int seat) { Seat = seat; }
    }

    /// <summary>
    /// A troca de contexto: por um instante, o jogo enxerga o escalador de uma vaga como "o jogador local" (o ator do
    /// Photon, Character.localCharacter, Player.localPlayer, a câmera e as instâncias estáticas dela). Entrar no contexto
    /// de quem já está nele não faz nada (devolve false), e só quem trocou devolve: os finalizers do Harmony rodam na
    /// ordem dos prefixes, então cada um devolve só o que ele mesmo trocou.
    ///
    /// Roda milhares de vezes por quadro (cada componente de cada escalador, o FixedUpdate várias vezes quando o fps
    /// cai): sem alocação e sem reflexão no caminho de cada troca.
    /// </summary>
    static class SeatContext
    {
        /// <summary>Os valores "do jogador local" de quem hospeda, guardados enquanto outra vaga está no contexto.</summary>
        static PhotonPlayer hostActor;
        static Character hostCharacter;
        static Player hostPlayer;
        static MainCamera hostCamera;
        static object[] hostStatics = new object[8];

        static readonly int[] stack = new int[64];
        static int depth;
        static readonly ExtraSeat[] states = new ExtraSeat[PartyAnywhere.Client.MaxSeats];

        /// <summary>Os campos estáticos que alguma vaga troca, com leitura e escrita compiladas.</summary>
        static readonly List<FieldInfo> tracked = new List<FieldInfo>();
        static readonly Dictionary<FieldInfo, int> trackedIndex = new Dictionary<FieldInfo, int>();
        static readonly List<Func<object>> getters = new List<Func<object>>();
        static readonly List<Action<object>> setters = new List<Action<object>>();

        public static int Current { get; private set; } = Seats.Host;

        /// <summary>Dentro do contexto de um escalador extra (não de quem hospeda).</summary>
        public static bool Active => Current != Seats.Host;

        public static ExtraSeat State(int seat) => seat == Seats.Host ? null : states[seat] ?? (states[seat] = new ExtraSeat(seat));

        public static IEnumerable<ExtraSeat> Extras
        {
            get { foreach (ExtraSeat s in states) if (s != null) yield return s; }
        }

        public static void TrackSingleton(FieldInfo field)
        {
            if (trackedIndex.ContainsKey(field)) return;
            trackedIndex[field] = tracked.Count;
            tracked.Add(field);
            getters.Add(Expression.Lambda<Func<object>>(Expression.Convert(Expression.Field(null, field), typeof(object))).Compile());
            ParameterExpression v = Expression.Parameter(typeof(object), "v");
            setters.Add(Expression.Lambda<Action<object>>(Expression.Assign(Expression.Field(null, field), Expression.Convert(v, field.FieldType)), v).Compile());
            if (hostStatics.Length < tracked.Count) Array.Resize(ref hostStatics, tracked.Count * 2);
            // quem chama já devolveu o campo ao valor de quem hospeda
            hostStatics[tracked.Count - 1] = field.GetValue(null);
        }

        /// <summary>
        /// Cria, no contexto da vaga, a cópia dela de algo que o jogo guarda num campo estático (Interaction.instance,
        /// GUIManager.instance…): o que <paramref name="create"/> puser no campo passa a ser da vaga, e o campo volta
        /// ao valor de antes para todo o resto.
        /// </summary>
        public static void Adopt(ExtraSeat s, FieldInfo field, Action create)
        {
            Run(s.Seat, () =>
            {
                object before = field.GetValue(null);
                field.SetValue(null, null);
                try { create(); }
                finally
                {
                    s.Singletons[field] = field.GetValue(null);
                    field.SetValue(null, before);
                    TrackSingleton(field);
                }
            });
        }

        /// <returns>true quando trocou: chame <see cref="Exit"/> depois (num finally).</returns>
        public static bool Enter(int seat)
        {
            if (seat == Current || seat < 0) return false;
            if (seat != Seats.Host && (states[seat] == null || !VirtualActors.CanUse)) return false;
            if (depth == stack.Length) return false;
            Save(Current);
            stack[depth++] = Current;
            Load(seat);
            Current = seat;
            return true;
        }

        public static void Exit()
        {
            if (depth == 0) return;
            Save(Current);
            int back = stack[--depth];
            Load(back);
            Current = back;
        }

        /// <summary>Roda em nome da vaga (ou só roda, se já está nela).</summary>
        public static void Run(int seat, Action action)
        {
            bool entered = Enter(seat);
            try { action(); }
            finally { if (entered) Exit(); }
        }

        /// <summary>Guarda os valores vivos "do jogador local" como sendo da vaga que está no contexto.</summary>
        static void Save(int seat)
        {
            if (seat == Seats.Host)
            {
                hostActor = PhotonNetwork.LocalPlayer;
                hostCharacter = Character.localCharacter;
                hostPlayer = Player.localPlayer;
                hostCamera = MainCamera.instance;
                for (int i = 0; i < getters.Count; i++) hostStatics[i] = getters[i]();
                return;
            }
            ExtraSeat s = states[seat];
            // o que a vaga ganhou no contexto (o personagem que nasceu, o Player dela) fica com ela
            s.Character = Character.localCharacter;
            s.Player = Player.localPlayer;
        }

        static void Load(int seat)
        {
            if (seat == Seats.Host)
            {
                VirtualActors.SetLocalPlayer(hostActor);
                Character.localCharacter = hostCharacter;
                Player.localPlayer = hostPlayer;
                MainCamera.instance = hostCamera;
                for (int i = 0; i < setters.Count; i++) setters[i](hostStatics[i]);
                return;
            }
            ExtraSeat s = states[seat];
            VirtualActors.SetLocalPlayer(s.Actor);
            Character.localCharacter = s.Character;
            Player.localPlayer = s.Player;
            // sem câmera própria ainda, a vaga usa a de quem hospeda
            MainCamera.instance = s.Camera != null ? s.Camera : hostCamera;
            // as instâncias que a vaga tem; o que ela não tem (ou a cópia dela que morreu com a cena) fica com o de
            // quem hospeda
            for (int i = 0; i < tracked.Count; i++)
            {
                object v = s.Singletons.TryGetValue(tracked[i], out object mine) && (!(mine is UnityEngine.Object o) || o != null)
                    ? mine : hostStatics[i];
                setters[i](v);
            }
        }

        /// <summary>Volta tudo para quem hospeda (troca de cena, erro no meio de um contexto).</summary>
        public static void Reset()
        {
            if (depth == 0) return;
            Save(Current);
            Load(stack[0]);
            Current = stack[0];
            depth = 0;
        }
    }
}
