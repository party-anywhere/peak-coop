using System.IO;
using BepInEx;
using UnityEngine;

namespace PeakCoop.Dev
{
    /// <summary>
    /// Só para desenvolver (desligado na ficha entregue): entra sozinho no "Jogar sozinho", vai do aeroporto para a
    /// ilha depois de um tempo e tira um print da tela de tempos em tempos em BepInEx\prints. Assim o teste roda sem
    /// ninguém mexer no jogo, com os jogadores vindo do `pa phone-sim`.
    /// </summary>
    class DevAuto : MonoBehaviour
    {
        public static bool AutoSolo, AutoIsland;
        public static float PrintEvery;
        public static float IslandAfter = 25f;
        public static bool CameraTest;

        float menuSeen = -1f, airportSeen = -1f, islandSeen = -1f, nextPrint;
        bool soloStarted, islandStarted;
        int printIndex;
        string printDir;

        void Start()
        {
            printDir = Path.Combine(Paths.BepInExRootPath, "prints");
            if (PrintEvery > 0f) Directory.CreateDirectory(printDir);
        }

        void Update()
        {
            // o PEAK desliga o log da Unity na versão de loja; para desenvolver, as exceções do jogo vão para o log
            if (!Debug.unityLogger.logEnabled) Debug.unityLogger.logEnabled = true;
            float now = Time.unscaledTime;
            if (AutoSolo && !soloStarted)
            {
                MainMenu menu = FindAnyObjectByType<MainMenu>();
                if (menu == null) menuSeen = -1f;
                else if (menuSeen < 0f) menuSeen = now;
                else if (now - menuSeen > 4f)
                {
                    soloStarted = true;
                    Plugin.Log.LogInfo("Dev: entrando no Jogar sozinho");
                    menu.PlaySoloClicked();
                }
            }
            if (AutoIsland && !islandStarted && Character.localCharacter != null && Character.localCharacter.inAirport)
            {
                if (airportSeen < 0f) airportSeen = now;
                else if (now - airportSeen > IslandAfter)
                {
                    AirportCheckInKiosk kiosk = FindAnyObjectByType<AirportCheckInKiosk>();
                    if (kiosk != null)
                    {
                        islandStarted = true;
                        Plugin.Log.LogInfo("Dev: indo para a ilha");
                        kiosk.StartGame(0);
                    }
                }
            }
            // experimento de desempenho: na ilha, desliga as câmeras extras por 15 s (o log mostra o fps sem elas)
            if (CameraTest && GameHandler.IsOnIsland)
            {
                if (islandSeen < 0f) islandSeen = now;
                bool off = now - islandSeen > 45f && now - islandSeen < 60f;
                foreach (Local.ExtraSeat s in Local.SeatContext.Extras)
                {
                    Camera cam = s.Camera != null ? s.Camera.GetComponent<Camera>() : null;
                    if (cam != null && cam.enabled == off)
                    {
                        cam.enabled = !off;
                        if (s.Seat == 1) Plugin.Log.LogInfo(off ? "Dev: câmeras extras desligadas" : "Dev: câmeras extras ligadas");
                    }
                }
            }
            if (PrintEvery > 0f && now >= nextPrint)
            {
                nextPrint = now + PrintEvery;
                string file = Path.Combine(printDir, $"shot_{printIndex++ % 100:00}.png");
                ScreenCapture.CaptureScreenshot(file);
                File.WriteAllText(Path.Combine(printDir, "latest.txt"), Path.GetFileName(file));
            }
        }
    }
}
