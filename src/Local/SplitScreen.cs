using System.Collections.Generic;
using PartyAnywhere;
using UnityEngine;

namespace PeakCoop.Local
{
    /// <summary>
    /// Uma visão por escalador, cada uma na sua parte da tela (<see cref="Grid"/>), na ordem P1, P2... A câmera de
    /// quem hospeda é a do próprio jogo (<see cref="MainCamera"/>); as dos escaladores extras se registram com
    /// <see cref="Register"/> quando nascem. Quem ligou a tela do jogo no celular sai da TV: a câmera dele desenha na
    /// imagem do celular (<see cref="PhoneViews"/>). Reorganiza no mesmo quadro quando alguém entra, sai ou troca.
    /// </summary>
    class SplitScreen : MonoBehaviour
    {
        static readonly SortedDictionary<int, Camera> extras = new SortedDictionary<int, Camera>();
        static readonly List<Camera> order = new List<Camera>();
        static readonly HashSet<int> onPhone = new HashSet<int>();

        /// <summary>A parte da tela de quem hospeda (o HUD do jogo e os avisos gerais ficam nela).</summary>
        public static Rect HostPart { get; private set; } = new Rect(0f, 0f, 1f, 1f);

        /// <summary>A vaga está jogando pelo celular (a visão dela não está na TV).</summary>
        public static bool OnPhone(int seat) => onPhone.Contains(seat);

        public static void Register(int seat, Camera cam) => extras[seat] = cam;

        public static void Unregister(int seat)
        {
            extras.Remove(seat);
            PhoneViews.Release(seat);
            onPhone.Remove(seat);
        }

        void LateUpdate()
        {
            order.Clear();
            Camera host = MainCamera.instance != null ? MainCamera.instance.GetComponent<Camera>() : null;
            if (host != null) Place(Seats.Host, host);
            foreach (var pair in extras)
                if (pair.Value != null && pair.Value.isActiveAndEnabled) Place(pair.Key, pair.Value);

            int n = order.Count;
            for (int i = 0; i < n; i++) order[i].rect = Grid.Part(n, i);
            HostPart = host != null && !onPhone.Contains(Seats.Host) ? host.rect : new Rect(0f, 0f, 1f, 1f);
            // mais de uma visão desenhada (na TV ou no celular): HUD em molduras e vídeo mais leve
            int views = n + onPhone.Count;
            SeatHud.Layout(views > 1);
            SplitGraphics.Apply(views);
        }

        static void Place(int seat, Camera cam)
        {
            if (PhoneViews.Wants(seat))
            {
                if (onPhone.Add(seat)) Plugin.Log.LogInfo($"P{seat + 1} passou a jogar pela tela do celular");
                cam.targetTexture = PhoneViews.TargetFor(seat);
                cam.rect = new Rect(0f, 0f, 1f, 1f);
                return;
            }
            if (onPhone.Remove(seat))
            {
                Plugin.Log.LogInfo($"P{seat + 1} voltou para a TV");
                PhoneViews.Release(seat);
                cam.targetTexture = null;
            }
            order.Add(cam);
        }
    }
}
