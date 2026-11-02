using System.Collections.Generic;
using PartyAnywhere;
using UnityEngine;

namespace PeakCoop.Local
{
    /// <summary>
    /// As vagas do lobby (P1..P10) e quem é o escalador de cada uma. P1 é quem hospeda: o personagem do próprio
    /// jogo (<see cref="Character.localCharacter"/> fora de qualquer contexto de vaga). Os escaladores extras se
    /// registram com <see cref="Bind"/> quando nascem.
    /// </summary>
    static class Seats
    {
        public const int Host = 0;

        static readonly SeatInfo[] info = new SeatInfo[Client.MaxSeats];
        static readonly Dictionary<Character, int> byCharacter = new Dictionary<Character, int>();
        static Character hostCharacter;

        public static void Refresh(Client hub)
        {
            for (int s = 0; s < Client.MaxSeats; s++)
                if (!hub.GetSeat(s, out info[s])) info[s] = new SeatInfo { Index = s, State = SeatState.Empty };
        }

        /// <summary>A vaga tem alguém (ativo ou reconectando: o personagem fica guardado para quem volta).</summary>
        public static bool Occupied(int seat) => info[seat].State != SeatState.Empty;

        public static SeatInfo Info(int seat) => info[seat];

        public static Color Color(int seat) => new Color32(info[seat].R, info[seat].G, info[seat].B, 255);

        public static void Bind(Character character, int seat) => byCharacter[character] = seat;

        public static void Unbind(Character character) => byCharacter.Remove(character);

        /// <summary>A vaga que controla o personagem, ou false se ninguém do sofá controla (bots, outros PCs).</summary>
        public static bool TryGetSeat(Character character, out int seat)
        {
            if (character == null) { seat = -1; return false; }
            if (byCharacter.TryGetValue(character, out seat)) return true;
            // fora de contexto, o "personagem local" do jogo é o de quem hospeda
            if (!SeatContext.Active && character == Character.localCharacter) hostCharacter = character;
            seat = Host;
            return character == hostCharacter;
        }
    }
}
