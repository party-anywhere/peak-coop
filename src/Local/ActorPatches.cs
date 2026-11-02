using System;
using HarmonyLib;
using Photon.Pun;
using PhotonPlayer = Photon.Realtime.Player;

namespace PeakCoop.Local
{
    /// <summary>
    /// O Photon e o PEAK tratando os atores virtuais como "deste PC", fora do contexto também: o que é de um escalador
    /// do sofá é deste PC, como o que é de quem hospeda.
    /// </summary>
    static class ActorPatches
    {
        /// <summary>O ator de verdade deste PC (o LocalPlayer fora de qualquer contexto).</summary>
        static int RealActor => SeatContext.Active ? -1 : PhotonNetwork.LocalPlayer?.ActorNumber ?? -1;

        /// <summary>IsMine é calculado contra o LocalPlayer da hora: vale para os atores virtuais e para o de verdade.</summary>
        [HarmonyPatch(typeof(PhotonView), nameof(PhotonView.RebuildControllerCache))]
        static class IsMinePatch
        {
            static void Postfix(PhotonView __instance)
            {
                if (__instance.IsMine || !VirtualActors.CanUse) return;
                int controller = __instance.ControllerActorNr;
                if (VirtualActors.IsVirtual(controller) || controller == PhotonNetwork.MasterClient?.ActorNumber)
                    __instance.IsMine = true;
            }
        }

        /// <summary>
        /// RPC com destino a um ator virtual: roda aqui mesmo, no contexto da vaga (com o LocalPlayer trocado, o PUN
        /// entende que o destino é este PC e executa na hora).
        /// </summary>
        [HarmonyPatch(typeof(PhotonNetwork), nameof(PhotonNetwork.RPC),
            new[] { typeof(PhotonView), typeof(string), typeof(RpcTarget), typeof(PhotonPlayer), typeof(bool), typeof(object[]) })]
        static class TargetedRpc
        {
            static void Prefix(PhotonPlayer player, out bool __state)
            {
                __state = player != null && VirtualActors.TryGetSeat(player.ActorNumber, out int seat) && SeatContext.Enter(seat);
            }

            static Exception Finalizer(Exception __exception, bool __state)
            {
                if (__state) SeatContext.Exit();
                return __exception;
            }
        }

        /// <summary>
        /// Tudo roda neste PC, que é o host: um ator do sofá também é o "master". Sem isto, o que um escalador extra pede
        /// ao host (pegar um item, abrir a mochila, acender a fogueira: RPC para MasterClient) sairia pela rede no modo
        /// offline e se perderia.
        /// </summary>
        [HarmonyPatch(typeof(PhotonPlayer), nameof(PhotonPlayer.IsMasterClient), MethodType.Getter)]
        static class CouchIsMaster
        {
            static void Postfix(PhotonPlayer __instance, ref bool __result)
            {
                if (!__result && VirtualActors.IsVirtual(__instance.ActorNumber) && VirtualActors.CanUse) __result = true;
            }
        }

        /// <summary>
        /// "IsLocal" no online quer dizer "este personagem é simulado neste PC": monstros (aranha, formiga-leão,
        /// tornado, escorpião, sapo, planta carnívora, zumbi, condor) só machucam, agarram e soltam o alvo quando ele é
        /// local. Todo escalador do sofá é deste PC.
        /// </summary>
        [HarmonyPatch(typeof(Character), nameof(Character.IsLocal), MethodType.Getter)]
        static class CouchIsLocal
        {
            static void Postfix(Character __instance, ref bool __result)
            {
                if (!__result && VirtualActors.CanUse && Seats.TryGetSeat(__instance, out _)) __result = true;
            }
        }

        /// <summary>A conta da Steam é de quem hospeda: o escalador do sofá fica com o id falso da vaga.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.AssignUserID))]
        static class UserId
        {
            static bool Prefix(Player __instance)
            {
                if (!SeatContext.Active) return true;
                Character c = __instance.character;
                if (c != null && c.data != null) c.data.userID = VirtualActors.UserIdOf(SeatContext.Current);
                return false;
            }
        }

        /// <summary>A voz (Photon Voice) é do microfone de quem hospeda: o escalador do sofá não toma o microfone.</summary>
        [HarmonyPatch(typeof(VoiceClientHandler), nameof(VoiceClientHandler.LocalPlayerAssigned))]
        static class Voice
        {
            static bool Prefix() => !SeatContext.Active;
        }
    }
}
