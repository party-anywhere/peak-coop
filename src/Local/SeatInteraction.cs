using HarmonyLib;
using UnityEngine;

namespace PeakCoop.Local
{
    /// <summary>
    /// O "usar" de cada escalador: o jogo tem uma <see cref="Interaction"/> só (o raio da câmera que acha o que você
    /// está olhando, o segurar para interagir, carregar um amigo, dar comida a ele, acender a fogueira). Cada vaga
    /// ganha a sua, que roda no contexto dela: a câmera, o personagem e o botão dela.
    /// </summary>
    static class SeatInteraction
    {
        static readonly System.Reflection.FieldInfo instanceField = AccessTools.Field(typeof(Interaction), nameof(Interaction.instance));

        public static void Create(ExtraSeat s)
        {
            Interaction source = Interaction.instance;
            if (source == null) return;
            GameObject go = null;
            SeatContext.Adopt(s, instanceField, () =>
            {
                go = new GameObject($"Interaction P{s.Seat + 1}");
                Interaction mine = go.AddComponent<Interaction>();
                mine.distance = source.distance;
                mine.area = source.area;
                mine.maxCharacterInteractAngle = source.maxCharacterInteractAngle;
            });
            s.InteractionObject = go;
            SeatComponents.Register(go, s.Seat);
        }
    }
}
