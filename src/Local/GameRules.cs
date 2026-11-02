using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace PeakCoop.Local
{
    /// <summary>
    /// Onde o "Jogar sozinho" do PEAK é diferente do online, com mais de um escalador no sofá vale o online: os itens de
    /// grupo aparecem, a pausa não congela os outros. E a conta da Steam (conquistas, emblemas, estatísticas) é só de
    /// quem hospeda: o que um escalador extra faz não conta para ela.
    /// </summary>
    static class GameRules
    {
        /// <summary>Mais de um escalador na partida do sofá.</summary>
        static bool Group => VirtualActors.CanUse && PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.PlayerCount > 1;

        /// <summary>Itens "de grupo" (banInSolo) saem do sorteio no modo sozinho; em grupo, aparecem como no online.</summary>
        [HarmonyPatch(typeof(LootData), nameof(LootData.IsValidToSpawn))]
        static class GroupLoot
        {
            static bool Prefix(LootData __instance, ref bool __result)
            {
                if (!__instance.banInSolo || !Group) return true;
                __result = !(__instance.excludeBasedOnCustomRunSetting && RunSettings.IsCustomRun
                    && RunSettings.GetValue(__instance.excludeIfSettingDisabled) == 0);
                return false;
            }
        }

        /// <summary>No online a pausa é só um menu; sozinho ela congela o tempo. Em grupo, como no online.</summary>
        [HarmonyPatch(typeof(PauseMenuHandler), "OnEnable")]
        static class PauseDoesNotFreeze
        {
            static void Postfix()
            {
                if (Group) Time.timeScale = GameUtils.GetTimeScale();
            }
        }

        // ---- a conta da Steam é de quem hospeda ----

        [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.ThrowAchievement))]
        static class NoAchievement { static bool Prefix() => !SeatContext.Active; }

        [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.IncrementSteamStat))]
        static class NoSteamStat
        {
            static bool Prefix(ref int __result)
            {
                if (!SeatContext.Active) return true;
                __result = 0;
                return false;
            }
        }

        [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.SetSteamStat))]
        static class NoSetSteamStat { static bool Prefix() => !SeatContext.Active; }

        [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.AddToRunBasedInt))]
        static class NoRunInt { static bool Prefix() => !SeatContext.Active; }

        [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.AddToRunBasedFloat))]
        static class NoRunFloat { static bool Prefix() => !SeatContext.Active; }

        [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.SetRunBasedInt))]
        static class NoSetRunInt { static bool Prefix() => !SeatContext.Active; }

        [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.SetRunBasedFloat))]
        static class NoSetRunFloat { static bool Prefix() => !SeatContext.Active; }

        [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.RecordMaxHeight))]
        static class NoHeight { static bool Prefix() => !SeatContext.Active; }

        [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.LocalCharacterHitByTrap))]
        static class NoTrapStat { static bool Prefix() => !SeatContext.Active; }

        [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.AddStatusBlockedByMilk))]
        static class NoMilkStat { static bool Prefix() => !SeatContext.Active; }
    }
}
