using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using PartyAnywhere;
using PeakCoop.Input;
using PeakCoop.Local;
using UnityEngine;
using EventType = PartyAnywhere.EventType;

namespace PeakCoop
{
    /// <summary>
    /// Um PEAK só, com todo mundo dentro dele: P1 joga a partida do próprio jogo e conecta ao Hub; cada pessoa do
    /// lobby do Party Anywhere vira um escalador de verdade, com a sua parte da tela, o seu HUD e o seu
    /// inventário. Ver docs/PLANO-TECNICO.md.
    /// </summary>
    [BepInPlugin(Id, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Id = "com.joaocaioo.peak-coop";
        public const string Name = "PEAK Co-op Local";
        public const string Version = "0.1.3";

        const float ConnectRetrySeconds = 2f;

        internal static ManualLogSource Log;

        /// <summary>O arquivo de configuração do mod (BepInEx\config\com.joaocaioo.peak-coop.cfg).</summary>
        internal static BepInEx.Configuration.ConfigFile Settings;

        public static Client Hub { get; private set; }

        const float NoUpdateWarnSeconds = 15f;

        static float nextConnectTry;
        static bool announced;
        static volatile bool ticked;
        static System.Threading.Timer watchdog;
        static readonly HashSet<string> reported = new HashSet<string>();

        /// <summary>
        /// Onde o mod roda: um objeto só dele, que nenhuma troca de cena destrói. Não depende do objeto do BepInEx
        /// (em alguns PCs ele é desligado ou destruído e nada do mod rodava, sem erro nenhum no log).
        /// </summary>
        internal static GameObject Runner { get; private set; }

        void Awake()
        {
            Log = Logger;
            Settings = Config;
            Log.LogInfo($"{Name} {Version}: um PEAK só, com os jogadores do Party Anywhere dentro dele");
            // cada passo à parte: um que falhe aparece no log com o nome, e os outros seguem
            Step("rodar com a janela sem foco", () => Application.runInBackground = true); // o Hub ou o navegador por cima
            var harmony = new Harmony(Id);
            Step("ganchos no jogo", () => harmony.PatchAll(typeof(Plugin).Assembly));
            Step("componentes por jogador", () => SeatComponents.Install(harmony));
            // quem a câmera está assistindo (espectador depois de morrer) é de cada jogador, não do PC
            Step("espectador por jogador", () => SeatContext.TrackPerSeat(AccessTools.Field(typeof(MainCameraMovement), "<specCharacter>k__BackingField")));
            Step("efeitos de tela por jogador", PersonalVolumes.Install);
            Step("toques do mundo por jogador", () => WorldTouch.Install(harmony));

            Runner = new GameObject("PeakCoop.Runner") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(Runner);
            Runner.AddComponent<Tick>();
            // os escaladores do sofá entram nesta partida, com a tela dividida aqui
            Step("escaladores do sofá", () => Runner.AddComponent<LocalSeats>());
            Step("tela dividida", () => Runner.AddComponent<SplitScreen>());
            Step("tela no celular", () => Runner.AddComponent<PhoneViews>());

            // se nada do mod rodar em 15 s, o log diz (em vez de ficar calado)
            watchdog = new System.Threading.Timer(_ =>
            {
                if (!ticked) Log.LogError($"O mod não rodou em {NoUpdateWarnSeconds:0} s depois de carregar (o jogo travou ou o Unity não está chamando o Update). Mande este arquivo (BepInEx, LogOutput.log) ao autor do mod.");
            }, null, (int)(NoUpdateWarnSeconds * 1000), System.Threading.Timeout.Infinite);

            // para desenvolver (BepInEx\config\com.joaocaioo.peak-coop.cfg); nada é gravado nas configurações do jogo
            Dev.DevAuto.AutoSolo = Config.Bind("Desenvolvimento", "EntrarSozinho", false, "Entra no Jogar sozinho assim que o menu abre.").Value;
            Dev.DevAuto.AutoIsland = Config.Bind("Desenvolvimento", "IrParaIlha", false, "Do aeroporto, vai para a ilha depois de 25 s.").Value;
            LocalSeats.MaxPlayers = Config.Bind("Desenvolvimento", "LimiteJogadores", 0, "Só as primeiras N vagas entram na partida (0 = todas), para medir desempenho.").Value;
            Dev.DevAuto.CameraTest = Config.Bind("Desenvolvimento", "TesteCameras", false, "Na ilha, desliga as câmeras extras por 15 s para medir o custo delas.").Value;
            Dev.DevAuto.PrintEvery = Config.Bind("Desenvolvimento", "PrintACada", 0f, "Segundos entre prints da tela em BepInEx\\prints (0 = desligado).").Value;
            if (Dev.DevAuto.AutoSolo || Dev.DevAuto.AutoIsland || Dev.DevAuto.PrintEvery > 0f) Runner.AddComponent<Dev.DevAuto>();
            Dev.DevScenario.Group = Config.Bind("Desenvolvimento", "CenarioGrupo", false, "No lugar da mala: junta todos na frente do P1 (teste das placas de nome).").Value;
            Dev.DevScenario.Effects = Config.Bind("Desenvolvimento", "CenarioEfeitos", false, "Na ilha: P2 envenenado e P3 morto (teste dos efeitos e do espectador por jogador).").Value;
            if (Dev.DevScenario.Group || Dev.DevScenario.Effects || Config.Bind("Desenvolvimento", "CenarioMala", false, "Leva o P2 até uma mala de tempos em tempos (teste de interação).").Value) Runner.AddComponent<Dev.DevScenario>();
            if (Config.Bind("Desenvolvimento", "Diagnostico", false, "Anota no log onde está cada escalador, a câmera e o HUD dele.").Value) Runner.AddComponent<Dev.SeatDiag>();
        }

        static void Step(string what, Action action)
        {
            try { action(); }
            catch (Exception e) { Log.LogError($"Falhou ao preparar \"{what}\": {e}"); }
        }

        /// <summary>Um erro por quadro viraria milhares de linhas: cada erro diferente vai ao log uma vez.</summary>
        internal static void ReportOnce(string where, Exception e)
        {
            string key = where + e.GetType().Name + e.Message;
            if (reported.Count < 50 && reported.Add(key)) Log.LogError($"Erro em {where}: {e}");
        }

        /// <summary>O quadro do mod, chamado pelo Runner.</summary>
        sealed class Tick : MonoBehaviour
        {
            void Update()
            {
                if (!ticked)
                {
                    ticked = true;
                    Log.LogInfo("Mod rodando");
                }
                try { Frame(); }
                catch (Exception e) { ReportOnce("Plugin.Update", e); }
            }

            void OnApplicationQuit()
            {
                Hub?.Dispose();
                Hub = null;
            }
        }

        static void Frame()
        {
            // aqui é sempre o contexto de quem hospeda; um contexto que ficou aberto (erro no meio) volta agora
            if (SeatContext.Current != Seats.Host)
            {
                Log.LogWarning($"Contexto do P{SeatContext.Current + 1} ficou aberto; voltando para o P1");
                SeatContext.Reset();
            }
            if (Hub == null)
            {
                if (Time.unscaledTime >= nextConnectTry) TryConnect();
            }
            else
            {
                Hub.Poll();
                DrainEvents();
            }
        }

        static void TryConnect()
        {
            nextConnectTry = Time.unscaledTime + ConnectRetrySeconds;
            Result r = Client.Connect(out Client c);
            if (r != Result.Ok)
            {
                if (r != Result.HubNotRunning) Log.LogWarning($"Party Anywhere: {Describe(r)}");
                return;
            }
            Hub = c;
            PhoneViews.Hub = c;
            Hub.Poll();
            Seats.Refresh(Hub);
            Log.LogInfo($"Party Anywhere: conectado; {Hub.ActiveSeatCount} jogador(es) no lobby");
            if (!announced)
            {
                announced = true;
                Hub.Toast(Client.AllSeats, "PEAK Co-op conectado");
                PhoneLabels.Send(Hub, Client.AllSeats);
            }
        }

        static void DrainEvents()
        {
            while (Hub.NextEvent(out PartyAnywhere.Event ev))
            {
                if (ev.Type == EventType.HubLost)
                {
                    Log.LogWarning("Party Anywhere: o Hub fechou; os controles ficam parados até ele voltar");
                    continue;
                }
                Hub.GetSeat(ev.Seat, out SeatInfo info);
                Seats.Refresh(Hub);
                Log.LogInfo($"P{ev.Seat + 1} {Describe(ev.Type)}: {info.Name} ({info.Device})");
                if (ev.Type == EventType.SeatJoined)
                {
                    Hub.Toast(ev.Seat, $"PEAK Co-op: você é o P{ev.Seat + 1}");
                    PhoneLabels.Send(Hub, ev.Seat);
                }
            }
        }

        static string Describe(Result r)
        {
            switch (r)
            {
                case Result.Version: return "o Hub fala outra versão do SDK; atualize o Party Anywhere ou o mod";
                case Result.Busy: return "outro jogo já está conectado ao Hub";
                default: return r.ToString();
            }
        }

        static string Describe(EventType t)
        {
            switch (t)
            {
                case EventType.SeatJoined: return "entrou";
                case EventType.SeatReconnecting: return "caiu (vaga guardada)";
                case EventType.SeatResumed: return "voltou";
                case EventType.SeatLeft: return "saiu";
                case EventType.SeatRenamed: return "trocou de nome";
                default: return t.ToString();
            }
        }
    }
}
