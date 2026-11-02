using HarmonyLib;
using Peak;
using Peak.Network;
using Portningsbolaget.Platforms;
using Portningsbolaget.Utilities;
using Zorro.Core;

namespace PeakCoop.Local
{
    /// <summary>
    /// Continuar uma expedição salva com todo o sofá. O quicksave guarda cada escalador pelo id da conta (o dos extras é
    /// o id fixo da vaga), mas no modo sozinho o jogo carrega só "o primeiro registro" e diz que mais de um é
    /// impossível. Com mais de um, o mod carrega como o online: o de quem hospeda vai para ele, os outros viram registros
    /// de reconexão, e cada vaga nasce com o estado dela (<see cref="LocalSeats"/>).
    /// </summary>
    [HarmonyPatch(typeof(Quicksave), nameof(Quicksave.PopulateMapAndPlayerStates))]
    static class SaveSupport
    {
        static bool Prefix()
        {
            var data = Quicksave.LoadedData;
            if (data.run.LobbyType != Quicksave.LobbyMode.Offline || data.playerSaves == null || data.playerSaves.Count <= 1) return true;
            string host = PlatformManager.Platform.UserID.ToString();
            if (host.Length != 64) host = Utilities.GetSha256Hash(host);
            bool hostFound = false;
            foreach (var save in data.playerSaves)
            {
                if (save.HashedUserId == host && !hostFound)
                {
                    hostFound = true;
                    Quicksave.PopulateHostData(save);
                }
                else Singleton<ReconnectHandler>.Instance.PopulateReconnectRecord(save.HashedUserId, save.GetReconnectData(), save.GetAchievementProgress());
            }
            if (!hostFound) Quicksave.PopulateHostData(data.playerSaves[0]);
            Plugin.Log.LogInfo($"Save: expedição com {data.playerSaves.Count} escaladores carregada");
            return false;
        }
    }
}
