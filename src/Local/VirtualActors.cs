using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using PhotonPlayer = Photon.Realtime.Player;

namespace PeakCoop.Local
{
    /// <summary>
    /// Um ator Photon de mentira por vaga extra, guardado na sala do modo offline do PEAK. Tudo no jogo é "por ator"
    /// (inventário, personagem, sessão, lista do fim da expedição): com o ator na sala, cada escalador do sofá passa
    /// pelos mesmos caminhos de quem entra pela internet. O ator é "local" (IsLocal): o jogo nunca tenta mandar nada
    /// para ele pela rede.
    /// </summary>
    static class VirtualActors
    {
        /// <summary>P2 vira o ator 102, P10 o ator 110: longe dos atores de verdade (1..MaxPlayers).</summary>
        public const int FirstActor = 100;

        static readonly ConstructorInfo playerCtor =
            AccessTools.Constructor(typeof(PhotonPlayer), new[] { typeof(string), typeof(int), typeof(bool) });
        static readonly MethodInfo setUserId = AccessTools.PropertySetter(typeof(PhotonPlayer), nameof(PhotonPlayer.UserId));

        public static int ActorOf(int seat) => FirstActor + seat;

        public static bool IsVirtual(int actor) => actor > FirstActor && actor <= FirstActor + PartyAnywhere.Client.MaxSeats;

        public static bool TryGetSeat(int actor, out int seat)
        {
            seat = actor - FirstActor;
            return IsVirtual(actor);
        }

        /// <summary>Id de conta falso, que nunca colide com um SteamID de verdade (os da Steam começam em 7656…).</summary>
        public static string UserIdOf(int seat) => (9000000000000000UL + (ulong)seat).ToString();

        /// <summary>Só na sala do modo offline: numa sala da internet os outros PCs não enxergariam os atores.</summary>
        public static bool CanUse => PhotonNetwork.OfflineMode && PhotonNetwork.InRoom;

        /// <summary>O ator da vaga, criado e posto na sala se ainda não está lá (a sala muda a cada partida).</summary>
        public static PhotonPlayer Ensure(int seat, string name)
        {
            Room room = PhotonNetwork.CurrentRoom;
            int actor = ActorOf(seat);
            PhotonPlayer p = room.GetPlayer(actor);
            if (p != null) return p;
            p = (PhotonPlayer)playerCtor.Invoke(new object[] { name, actor, true });
            string userId = UserIdOf(seat);
            setUserId.Invoke(p, new object[] { userId });
            p.CustomProperties["UserID"] = userId;
            room.StorePlayer(p);
            Plugin.Log.LogInfo($"P{seat + 1} entrou na expedição como o ator {actor}");
            return p;
        }

        public static void Remove(int seat)
        {
            Room room = PhotonNetwork.CurrentRoom;
            room?.Players.Remove(ActorOf(seat));
        }

        /// <summary>Troca quem o Photon acha que é "este PC" (a troca de contexto usa e devolve).</summary>
        public static void SetLocalPlayer(PhotonPlayer p) => PhotonNetwork.NetworkingClient.LocalPlayer = p;
    }
}
