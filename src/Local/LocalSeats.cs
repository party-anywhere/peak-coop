using System.Linq;
using System;
using Photon.Pun;
using UnityEngine;

namespace PeakCoop.Local
{
    /// <summary>
    /// Os escaladores do sofá: para cada vaga ocupada do lobby (fora a de quem hospeda), um ator virtual na sala, um
    /// Character e um Player nascidos pelo PhotonNetwork.Instantiate do jogo no contexto da vaga, e uma câmera própria
    /// (cópia da câmera do jogo). Nascem perto de quem hospeda, no aeroporto ou na ilha, e nascem de novo a cada cena.
    /// </summary>
    class LocalSeats : MonoBehaviour
    {
        const float SpawnSpacing = 1.2f;

        /// <summary>Para desenvolver (medir desempenho): só as primeiras vagas entram (0 = todas).</summary>
        public static int MaxPlayers;

        string waiting;

        void Update()
        {
            // nasce depois de quem hospeda: a cena já está pronta e há onde pôr o escalador
            Character host = Character.localCharacter;
            string why = !VirtualActors.CanUse ? $"fora da sala offline (offline={PhotonNetwork.OfflineMode}, sala={PhotonNetwork.InRoom})"
                : host == null ? "sem o personagem de quem hospeda"
                : LoadingScreenHandler.loading || !host.started ? "a cena está carregando"
                : null;
            if (why != waiting)
            {
                waiting = why;
                if (why != null) Plugin.Log.LogInfo($"Escaladores do sofá esperando: {why}");
            }
            if (why != null) return;
            for (int seat = 0; seat < PartyAnywhere.Client.MaxSeats; seat++)
            {
                if (seat == Seats.Host) continue;
                if (!Seats.Occupied(seat))
                {
                    // saiu do lobby (não só caiu: quem cai fica RECONNECTING e o escalador espera por ele)
                    ExtraSeat gone = SeatContext.Extras.FirstOrDefault(x => x.Seat == seat);
                    if (gone != null && (gone.Character != null || gone.Actor != null)) Leave(gone);
                    continue;
                }
                if (MaxPlayers > 0 && seat >= MaxPlayers) continue;
                ExtraSeat s = SeatContext.State(seat);
                try
                {
                    if (s.Actor == null || PhotonNetwork.CurrentRoom.GetPlayer(s.Actor.ActorNumber) == null)
                    {
                        s.Actor = VirtualActors.Ensure(seat, $"P{seat + 1}");
                        SeatLooks.Apply(seat, s.Actor);
                    }
                    if (s.Character == null) Spawn(s, host);
                    else if (s.Camera == null) SeatCameras.Create(s);
                    else if (s.InteractionObject == null) SeatInteraction.Create(s);
                    else if (s.Hud == null) { SeatHud.Create(s); SeatMenus.Create(s); }
                }
                catch (Exception e)
                {
                    SeatContext.Reset();
                    Plugin.Log.LogError($"P{seat + 1}: não consegui pôr o escalador na partida: {e}");
                    enabled = false; // um erro por quadro encheria o log; o diagnóstico já está acima
                }
            }
        }

        /// <summary>
        /// Como quem sai de uma partida online: o escalador some do mundo (o que ele carregava fica no chão, como o
        /// jogo faz ao destruir o personagem), a câmera e o HUD dele saem da tela e o ator sai da sala.
        /// </summary>
        static void Leave(ExtraSeat s)
        {
            Plugin.Log.LogInfo($"P{s.Seat + 1} saiu da expedição");
            if (s.Character != null) Seats.Unbind(s.Character);
            SeatContext.Run(s.Seat, () =>
            {
                if (s.Character != null) PhotonNetwork.Destroy(s.Character.photonView);
                if (s.Player != null) PhotonNetwork.Destroy(s.Player.photonView);
            });
            SplitScreen.Unregister(s.Seat);
            if (s.Camera != null) Destroy(s.Camera.gameObject);
            if (s.Hud != null) Destroy(s.Hud);
            if (s.InteractionObject != null) Destroy(s.InteractionObject);
            s.Character = null;
            s.Player = null;
            s.Camera = null;
            s.Hud = null;
            s.InteractionObject = null;
            s.Singletons.Clear();
            VirtualActors.Remove(s.Seat);
            s.Actor = null;
        }

        /// <summary>
        /// Um lugar com chão embaixo, sem parede nem outro escalador ocupando, perto do desejado: começa nele e vai
        /// abrindo em anéis em volta do ponto de nascer. Nunca abaixo do ponto (onde o chão é outro andar ou o vazio).
        /// </summary>
        static Vector3 SafeSpot(Vector3 desired, Vector3 anchor)
        {
            int solid = HelperFunctions.AllPhysical;
            bool Fits(Vector3 at, out Vector3 feet)
            {
                feet = at;
                if (!Physics.Raycast(at + Vector3.up * 1.2f, Vector3.down, out RaycastHit hit, 3f, solid, QueryTriggerInteraction.Ignore)) return false;
                if (hit.point.y < anchor.y - 0.7f) return false;
                feet = hit.point;
                return !Physics.CheckCapsule(feet + Vector3.up * 0.45f, feet + Vector3.up * 1.6f, 0.35f, solid, QueryTriggerInteraction.Ignore);
            }
            if (Fits(desired, out Vector3 spot)) return spot + Vector3.up * 0.15f;
            for (float r = 0.8f; r <= 4f; r += 0.6f)
                for (int a = 0; a < 12; a++)
                {
                    Vector3 at = anchor + Quaternion.Euler(0f, a * 30f, 0f) * Vector3.forward * r;
                    if (Fits(at, out spot)) return spot + Vector3.up * 0.15f;
                }
            Plugin.Log.LogWarning($"Nascer: sem lugar livre perto de {anchor:F1}; usando o ponto");
            return anchor + Vector3.up * 0.5f;
        }

        /// <summary>
        /// Os 4 elevadores do aeroporto ("Elevator", "Elevator (1)"…), começando pelo do ponto de nascer de quem
        /// hospeda; null fora do aeroporto.
        /// </summary>
        static Transform[] AirportElevators()
        {
            var list = new System.Collections.Generic.List<Transform>();
            foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (t.name == "Elevator" || t.name.StartsWith("Elevator (")) list.Add(t);
            if (list.Count == 0 || SpawnPoint.allSpawnPoints.Count == 0) return null;
            Vector3 p1 = SpawnPoint.GetSpawnPoint(1).transform.position;
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            // o elevador de quem hospeda é o mais perto do ponto de nascer: ele vai primeiro
            int first = 0;
            for (int i = 1; i < list.Count; i++)
                if (Vector3.Distance(list[i].position, p1) < Vector3.Distance(list[first].position, p1)) first = i;
            Transform hostElevator = list[first];
            list.RemoveAt(first);
            list.Insert(0, hostElevator);
            return list.ToArray();
        }

        /// <summary>O chão logo abaixo (ou um pouco acima) do ponto, sem descer mais que 3 m do nível de referência.</summary>
        static Vector3 FloorAt(Vector3 at, float referenceY)
        {
            Vector3 from = new Vector3(at.x, referenceY + 2f, at.z);
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 5f, HelperFunctions.terrainMapMask | HelperFunctions.DefaultMask, QueryTriggerInteraction.Ignore))
                return hit.point;
            return new Vector3(at.x, referenceY, at.z);
        }

        static void Spawn(ExtraSeat s, Character host)
        {
            // como no online: cada jogador no seu ponto de nascer (P2 no 2, P3 no 3…). A cena tem poucos pontos (o
            // online é de 4): da volta em diante, o escalador fica ao lado do ponto, sem um nascer dentro do outro
            bool freshStart = !GameHandler.IsOnIsland || !GameHandler.PlayersHaveLeftShore;
            Vector3 pos;
            Quaternion rot;
            Transform spawnAt;
            bool passedOut = false;
            if (!GameHandler.IsOnIsland && SpawnPoint.allSpawnPoints.Count > 0)
            {
                // aeroporto: só o elevador do ponto de nascer abre (os outros três são cenário fechado). Como no online,
                // até 4 saem dele (P2 à esquerda, P3 à direita, P4 no fundo); do P5 em diante, numa fila no saguão,
                // em frente aos elevadores
                SpawnPoint point = SpawnPoint.GetSpawnPoint(1);
                Transform t = point.transform;
                Vector3 side = Vector3.ProjectOnPlane(t.right, Vector3.up).normalized;
                // a porta: do meio do elevador para o ponto de nascer (que fica perto da porta)
                Transform[] elevators = AirportElevators();
                Vector3 front = elevators != null
                    ? Vector3.ProjectOnPlane(t.position - elevators[0].position, Vector3.up).normalized
                    : Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
                if (front.sqrMagnitude < 0.01f) front = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
                side = Vector3.Cross(Vector3.up, front).normalized;
                if (s.Seat <= 3)
                    pos = t.position + (s.Seat == 1 ? -side * 0.6f : s.Seat == 2 ? side * 0.6f : -front * 0.55f);
                else
                {
                    int i = s.Seat - 4; // P5..P10 → 0..5, alternando dos dois lados do P1
                    float x = (i / 2 + 1) * 1.1f * (i % 2 == 0 ? -1f : 1f);
                    pos = FloorAt(t.position + front * 4f + side * x, t.position.y);
                }
                rot = t.rotation;
                spawnAt = t;
            }
            else if (freshStart && SpawnPoint.allSpawnPoints.Count > 0)
            {
                int count = SpawnPoint.allSpawnPoints.Count;
                SpawnPoint point = SpawnPoint.GetSpawnPoint(s.Seat + 1);
                int lap = s.Seat / count;
                pos = point.transform.position + point.transform.right * (SpawnSpacing * lap) + Vector3.up * 0.3f * lap;
                rot = point.transform.rotation;
                spawnAt = point.transform;
                passedOut = GameHandler.IsOnIsland && point.startPassedOut;
            }
            else
            {
                // chegou com a expedição andando: ao lado de quem hospeda, um pouco para trás
                Vector3 flat = Vector3.ProjectOnPlane(host.data.lookDirection, Vector3.up).normalized;
                if (flat.sqrMagnitude < 0.01f) flat = host.transform.forward;
                pos = host.Center + Quaternion.Euler(0f, 40f * s.Seat, 0f) * (-flat) * SpawnSpacing + Vector3.up * 0.5f;
                rot = Quaternion.LookRotation(flat);
                spawnAt = host.data.spawnPoint;
            }
            // na ilha, quem já tem registro (expedição salva que continua, ou quem voltou depois de cair) volta como no
            // online: na posição guardada (ou morto, na zona dos mortos), com status, inventário e item na mão
            ReconnectData saved = default;
            bool restore = GameHandler.IsOnIsland
                && ReconnectHandler.TryGetReconnectData(VirtualActors.UserIdOf(s.Seat), out saved, out _)
                && saved.isValid;
            if (restore)
            {
                pos = saved.dead ? Character.DeathPos() : saved.position + Vector3.up * 0.3f;
                passedOut = false;
            }
            else if (GameHandler.IsOnIsland) pos = SafeSpot(pos, pos); // procura chão livre em volta do lugar escolhido
            else pos += Vector3.up * 0.15f;
            SeatContext.Run(s.Seat, () =>
            {
                // o mesmo caminho do CharacterSpawner.SpawnMyPlayerCharacter, com a vaga como "este PC"
                Character c = PhotonNetwork.Instantiate("Character", pos, rot, 0).GetComponent<Character>();
                c.data.spawnPoint = spawnAt;
                // na praia todo mundo acorda desmaiado na areia, como no online
                if (passedOut) c.StartPassedOutOnTheBeach();
                if (Player.localPlayer == null) PhotonNetwork.Instantiate("Player", Vector3.zero, Quaternion.identity, 0);
                c.data.userID = VirtualActors.UserIdOf(s.Seat);
                if (restore)
                {
                    if (saved.isSkeleton) c.data.SetSkeleton(active: true);
                    CharacterSpawner.ApplyAndPushReconnectData(c, saved);
                    Plugin.Log.LogInfo($"P{s.Seat + 1} voltou com o estado guardado ({(saved.dead ? "morto" : "vivo")})");
                }
            });
            Seats.Bind(s.Character, s.Seat);
            SeatComponents.Register(s.Character.gameObject, s.Seat);
            if (s.Player != null) SeatComponents.Register(s.Player.gameObject, s.Seat);
            Plugin.Log.LogInfo($"P{s.Seat + 1} nasceu: {s.Character.name} em {pos:F1} (pontos de nascer: {SpawnPoint.allSpawnPoints.Count}, desmaiado: {passedOut})");
            SeatCameras.Create(s);
        }
    }
}
