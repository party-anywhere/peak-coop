using System;
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
        public const string Version = "0.1.1";

        const float ConnectRetrySeconds = 2f;

        internal static ManualLogSource Log;

        /// <summary>O arquivo de configuração do mod (BepInEx\config\com.joaocaioo.peak-coop.cfg).</summary>
        internal static BepInEx.Configuration.ConfigFile Settings;

        public static Client Hub { get; private set; }

        float nextConnectTry;
        bool announced;

        void Awake()
        {
            Log = Logger;
            Settings = Config;
            Log.LogInfo($"{Name} {Version}: um PEAK só, com os jogadores do Party Anywhere dentro dele");
            // o jogo segue lendo os controles do Hub sem ser a janela em foco (o Hub ou o navegador por cima)
            Application.runInBackground = true;
            var harmony = new Harmony(Id);
            harmony.PatchAll(typeof(Plugin).Assembly);
            SeatComponents.Install(harmony);
            // quem a câmera está assistindo (espectador depois de morrer) é de cada jogador, não do PC
            SeatContext.TrackPerSeat(AccessTools.Field(typeof(MainCameraMovement), "<specCharacter>k__BackingField"));
            PersonalVolumes.Install();
            WorldTouch.Install(harmony);
            // os escaladores do sofá entram nesta partida, com a tela dividida aqui
            gameObject.AddComponent<LocalSeats>();
            gameObject.AddComponent<SplitScreen>();
            gameObject.AddComponent<PhoneViews>();

            // para desenvolver (BepInEx\config\com.joaocaioo.peak-coop.cfg); nada é gravado nas configurações do jogo
            Dev.DevAuto.AutoSolo = Config.Bind("Desenvolvimento", "EntrarSozinho", false, "Entra no Jogar sozinho assim que o menu abre.").Value;
            Dev.DevAuto.AutoIsland = Config.Bind("Desenvolvimento", "IrParaIlha", false, "Do aeroporto, vai para a ilha depois de 25 s.").Value;
            LocalSeats.MaxPlayers = Config.Bind("Desenvolvimento", "LimiteJogadores", 0, "Só as primeiras N vagas entram na partida (0 = todas), para medir desempenho.").Value;
            Dev.DevAuto.CameraTest = Config.Bind("Desenvolvimento", "TesteCameras", false, "Na ilha, desliga as câmeras extras por 15 s para medir o custo delas.").Value;
            Dev.DevAuto.PrintEvery = Config.Bind("Desenvolvimento", "PrintACada", 0f, "Segundos entre prints da tela em BepInEx\\prints (0 = desligado).").Value;
            if (Dev.DevAuto.AutoSolo || Dev.DevAuto.AutoIsland || Dev.DevAuto.PrintEvery > 0f) gameObject.AddComponent<Dev.DevAuto>();
            Dev.DevScenario.Group = Config.Bind("Desenvolvimento", "CenarioGrupo", false, "No lugar da mala: junta todos na frente do P1 (teste das placas de nome).").Value;
            Dev.DevScenario.Effects = Config.Bind("Desenvolvimento", "CenarioEfeitos", false, "Na ilha: P2 envenenado e P3 morto (teste dos efeitos e do espectador por jogador).").Value;
            if (Dev.DevScenario.Group || Dev.DevScenario.Effects || Config.Bind("Desenvolvimento", "CenarioMala", false, "Leva o P2 até uma mala de tempos em tempos (teste de interação).").Value) gameObject.AddComponent<Dev.DevScenario>();
            if (Config.Bind("Desenvolvimento", "Diagnostico", false, "Anota no log onde está cada escalador, a câmera e o HUD dele.").Value) gameObject.AddComponent<Dev.SeatDiag>();
        }

        void Update()
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

        void OnApplicationQuit()
        {
            Hub?.Dispose();
            Hub = null;
        }

        void TryConnect()
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

        void DrainEvents()
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
