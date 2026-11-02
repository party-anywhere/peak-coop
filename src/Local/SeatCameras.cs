using System;
using System.Collections.Generic;
using UnityEngine;

namespace PeakCoop.Local
{
    /// <summary>
    /// Uma câmera por escalador extra: cópia do objeto da câmera do jogo (MainCamera + MainCameraMovement + o resto),
    /// acordada no contexto da vaga (<see cref="SeatClones"/>). Só a câmera de quem hospeda escuta.
    /// </summary>
    static class SeatCameras
    {
        static readonly Dictionary<MainCamera, int> seatOf = new Dictionary<MainCamera, int>();

        public static bool IsSeatCamera(MainCamera cam) => seatOf.ContainsKey(cam);

        /// <summary>A vaga dona da câmera (a câmera do jogo é de quem hospeda).</summary>
        public static int SeatOf(MainCamera cam) => cam != null && seatOf.TryGetValue(cam, out int seat) ? seat : Seats.Host;

        public static void Create(ExtraSeat s)
        {
            MainCamera source = MainCamera.instance;
            if (source == null || s.Character == null) return;
            GameObject copy = SeatClones.Clone(s, source.gameObject, $"MainCamera P{s.Seat + 1}");
            s.Camera = copy.GetComponent<MainCamera>();
            foreach (AudioListener l in copy.GetComponentsInChildren<AudioListener>(true)) l.enabled = false;
            SplitScreen.Register(s.Seat, copy.GetComponent<Camera>());
            seatOf[s.Camera] = s.Seat;
            BodyPerCamera.Install();
            Plugin.Log.LogInfo($"P{s.Seat + 1}: câmera própria ({string.Join(", ", Array.ConvertAll(copy.GetComponents<Component>(), c => c.GetType().Name))})");
        }
    }
}
