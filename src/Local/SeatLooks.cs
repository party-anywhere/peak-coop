using Photon.Pun;
using UnityEngine;
using Zorro.Core;
using PhotonPlayer = Photon.Realtime.Player;

namespace PeakCoop.Local
{
    /// <summary>
    /// A aparência de cada escalador do sofá. No online cada pessoa traz a dela (da conta); aqui todo mundo usaria a de
    /// quem hospeda. Cada vaga ganha a pele da cor mais perto da cor dela no Party Anywhere e o resto sorteado entre o
    /// que a conta já desbloqueou, sempre igual para a mesma vaga (o sorteio usa a vaga como semente).
    /// </summary>
    static class SeatLooks
    {
        public static void Apply(int seat, PhotonPlayer actor)
        {
            Customization c = Singleton<Customization>.Instance;
            if (c == null) return;
            var data = new PersistentPlayerData();
            CharacterCustomizationData look = data.customizationData;
            // o que a pessoa da vaga escolheu no passaporte da última vez
            if (TryLoad(seat, look))
            {
                GameHandler.GetService<PersistentPlayerDataService>().SetPlayerData(actor, data);
                return;
            }
            Random.State before = Random.state;
            Random.InitState(7919 * (seat + 1));
            try
            {
                look.currentSkin = ClosestSkin(c, Seats.Color(seat));
                look.currentAccessory = c.GetRandomUnlockedIndex(Customization.Type.Accessory);
                look.currentEyes = c.GetRandomUnlockedIndex(Customization.Type.Eyes);
                look.currentMouth = c.GetRandomUnlockedIndex(Customization.Type.Mouth);
                look.currentOutfit = c.GetRandomUnlockedIndex(Customization.Type.Fit);
                look.currentHat = c.GetRandomUnlockedIndex(Customization.Type.Hat);
            }
            finally { Random.state = before; }
            GameHandler.GetService<PersistentPlayerDataService>().SetPlayerData(actor, data);
        }

        static BepInEx.Configuration.ConfigEntry<string> Entry(int seat) =>
            Plugin.Settings.Bind("Aparencia", $"P{seat + 1}", "", "Aparência escolhida no passaporte (pele, olhos, boca, acessório, roupa, chapéu, faixa, medalha). Vazio = a cor da vaga.");

        static bool TryLoad(int seat, CharacterCustomizationData look)
        {
            string[] v = Entry(seat).Value.Split(',');
            if (v.Length != 8) return false;
            var n = new int[8];
            for (int i = 0; i < 8; i++) if (!int.TryParse(v[i], out n[i])) return false;
            look.currentSkin = n[0]; look.currentEyes = n[1]; look.currentMouth = n[2]; look.currentAccessory = n[3];
            look.currentOutfit = n[4]; look.currentHat = n[5]; look.currentSash = n[6]; look.currentMedal = n[7];
            return true;
        }

        /// <summary>A pessoa mudou a aparência no passaporte: guarda para a vaga dela.</summary>
        [HarmonyLib.HarmonyPatch(typeof(PersistentPlayerDataService), nameof(PersistentPlayerDataService.SetPlayerData))]
        static class Remember
        {
            static void Postfix(PhotonPlayer player, PersistentPlayerData playerData)
            {
                if (!SeatContext.Active || player == null || !VirtualActors.TryGetSeat(player.ActorNumber, out int seat) || seat != SeatContext.Current) return;
                CharacterCustomizationData l = playerData.customizationData;
                Entry(seat).Value = $"{l.currentSkin},{l.currentEyes},{l.currentMouth},{l.currentAccessory},{l.currentOutfit},{l.currentHat},{l.currentSash},{l.currentMedal}";
            }
        }

        static int ClosestSkin(Customization c, Color want)
        {
            int best = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < c.skins.Length; i++)
            {
                if (c.skins[i] == null || c.skins[i].IsLocked) continue;
                Color.RGBToHSV(c.skins[i].color, out float h, out float s, out float v);
                Color.RGBToHSV(want, out float wh, out float ws, out float wv);
                float dh = Mathf.Abs(h - wh);
                dh = Mathf.Min(dh, 1f - dh);
                float d = dh * 4f + Mathf.Abs(s - ws) + Mathf.Abs(v - wv) * 0.5f;
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }
    }
}
