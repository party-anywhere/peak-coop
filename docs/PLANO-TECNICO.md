# PEAK Co-op Local: plano técnico

Um PEAK só, com até 10 jogadores dentro dele, cada um na sua parte da tela (ou no celular), com o próprio
personagem, inventário, vida/fome/frio, HUD e interações. Segue o caminho do guia do Party Anywhere
(`pa docs coop`): **uma cópia do jogo, N jogadores de verdade, N visões**. Nada de abrir várias cópias.

## O que o jogo é por dentro (versão 3.0.1, `PEAK.exe` sha256 `4cb94d00…e9e233`)

- **Unity 6, URP, Mono** (as DLLs em `PEAK_Data\Managed`): mod por **BepInEx 5** + Harmony, em C#.
- **Rede: Photon PUN 2.** Cada pessoa online é um *ator* Photon (`ActorNumber`). Jogar sozinho usa o
  **modo offline** do PUN (`PhotonNetwork.OfflineMode` + `NetworkingUtilities.JoinDummyRoom`): uma sala de
  mentira com um ator só.
- **Limite de jogadores:** `NetworkingUtilities.MAX_PLAYERS = 4` (só o tamanho da sala online; há até um
  comando de console `SetMaxPlayers`). O limite real para o sofá está nos outros lugares abaixo.

### Tudo é "por ator" ou "o local"

| Peça do jogo | Como é hoje | O que o mod faz |
|---|---|---|
| `Player` (prefab "Player", PhotonView) | Um por ator. Guarda **inventário** (3 slots + mochila + slot temporário). `Player.localPlayer` estático. | Um `Player` por vaga, cada um com o seu ator virtual. |
| `Character` (prefab "Character") | Um por ator. `Character.localCharacter` estático, posto em `Awake` quando `view.IsMine`. Vida, fome, frio, veneno etc. em `CharacterAfflictions`/`CharacterData`. | Um `Character` por vaga, nascido pelo `CharacterSpawner` do jogo. |
| `PlayerHandler` | Dicionários por `ActorNumber`: `Player`, `Character`, sessão (userId). `GetAllPlayerCharacters()` confere contra `PhotonNetwork.CurrentRoom.PlayerCount`. | Os atores virtuais entram na sala, então tudo isso funciona sem reescrever. |
| `PhotonView.IsMine` | `controllerActorNr == LocalPlayer.ActorNumber` (calculado em `RebuildControllerCache`). | Patch: ator virtual também conta como "meu". |
| `CharacterInput.Sample` | Lê as `InputAction` globais (teclado/mouse/controle do Windows), só para `character.IsLocal`. | Prefixo: lê o pad da vaga pelo SDK (`Client.GetPad`). |
| `CharacterMovement.CameraLook` | Sensibilidade do mouse ou do controle, global. | Para quem joga pelo SDK: graus direto do analógico + `TakeLook` do celular. |
| `MainCamera.instance` / `MainCameraMovement` (singleton) | Uma câmera, segue `Character.localCharacter` (também espectador e fantasma). | Uma câmera por vaga (cópia da original), cada uma rodando o `MainCameraMovement` no contexto da vaga, com `Camera.rect` da grade. |
| `GUIManager.instance` | Um HUD (barra de stamina e aflições, inventário, "usar", ping, emotes). | Uma cópia do HUD por vaga (só as telas), dentro da parte dela. |
| Áudio | `AudioListener` na câmera principal; `AssignMixerGroup` tem 4 grupos de voz. | Só a câmera de P1 escuta; voz desligada para atores virtuais. |

Contagem de uso dos singletons no `Assembly-CSharp` (arquivos): `Character.localCharacter` 106,
`MainCamera.instance` 51, `GUIManager.instance` 46, `Player.localPlayer` 11. É por isso que a **troca de
contexto** (abaixo) vale mais que corrigir cada uso.

## A ideia central: atores virtuais + troca de contexto

1. **Partida local = modo offline do PEAK** (o mesmo "Jogar sozinho" do menu). Nada de servidor, nada de
   conta extra, nada de contornar a Steam.
2. **Ator virtual por vaga.** Para P2..P10, o mod cria um `Photon.Realtime.Player` (construtor
   `protected internal`, por reflexão) com `ActorNumber = 100 + vaga` e põe na sala (`Room.Players`). Assim
   `PlayerCount`, `PlayerHandler`, `GetAllPlayerCharacters`, a lista do fim da partida, as estatísticas e o
   reviver já enxergam N escaladores, pelos caminhos do próprio jogo.
3. **Nascer como o jogo nasce.** `PhotonNetwork.Instantiate("Player")` e `("Character")`, e então
   `TransferOwnership` para o ator virtual. Patch em `PhotonView.IsMine` (e `AmController`) para atores
   virtuais. `Player.Awake`/`Character.Awake` rodam no contexto da vaga, então `localPlayer`/`localCharacter`
   apontam para o certo enquanto nascem.
4. **Troca de contexto** (`SeatContext.Enter(vaga)` / `Exit`): troca `Character.localCharacter`,
   `Player.localPlayer`, `MainCamera.instance`, `Singleton<MainCameraMovement>.Instance`,
   `GUIManager.instance` e a "vaga da entrada". Em volta de: `Update`/`LateUpdate`/`FixedUpdate` de todo
   componente do `Character` e do `Player` da vaga, das câmeras e do HUD dela, e dos itens que ela segura
   (`Item` com `holderCharacter` da vaga). Entrar no contexto de quem já está nele não faz nada (pegadinha
   dos finalizers do Harmony, ver guia seção 4).
5. **RPCs** que chegam a views de ator virtual rodam no contexto da vaga (no offline o PUN entrega na hora).

## Tela dividida e HUD

- Grade do padrão Party Anywhere (`src/Local/Grid.cs`): 1; 2 lado a lado; 2+1; 2+2; 3+2; 3+3; 3+2+2;
  3+3+2; 3+3+3; 4+3+3. Ordem P1, P2... e reorganiza no mesmo quadro quando alguém entra ou sai.
- Câmeras: uma por vaga com `Camera.rect`; o pós-processamento do URP é por câmera (HBAO, volume): nas
  visões extras, efeitos temporais desligados (TAA/motion blur misturam as imagens).
- Corpo em primeira pessoa: o PEAK esconde a cabeça do próprio personagem (`HideTheBody`). Cada corpo numa
  camada só dele e cada câmera esconde só a sua.
- HUD: a cópia do `GUIManager` por vaga, com o canvas em `ScreenSpaceCamera` na câmera da vaga e escala pela
  altura da parte. Etiqueta "P2 · nome" na cor da vaga.
- Áreas sem visão pintadas de preto. Preset de vídeo leve só na memória com mais de um jogador.

## O que precisa de atenção no PEAK (lista para o checklist "não perdi nada")

- **Aeroporto (lobby)**, partida, os biomas (praia, tropical, alpino/mesa, caldeira, pico), a fogueira
  entre trechos (ela espera "todos" perto: atores virtuais contam), o **fim de corrida** e o helicóptero.
- **Desmaiar, morrer, fantasma/espectador** (`PlayerGhost`, `MainCameraMovement.specCharacter`), **reviver**
  (estátua, fogueira), carregar outro jogador nas costas (`CharacterCarrying`).
- Corda, estacas, piton, balão, cogumelos, comida cozida, itens lendários, mochila (abrir a de outro
  jogador), emotes, ping (`PointPinger`), passaporte/customização (cada vaga com a cor da vaga por padrão).
- Conquistas e emblemas: só para a conta de quem hospeda (P1). Os atores virtuais não mexem em
  `AchievementManager` (patch: no contexto de vaga extra, ignora).
- Voz (Photon Voice): desligada para atores virtuais.
- Online: se P1 entra na sala de outra pessoa pela internet, o mod fica parado (como no mod do MicroWorks).

## Passar de 4 para 10

Lugares do limite achados até agora: `MAX_PLAYERS` (sala), `PlayerHandler.AssignMixerGroup` (4 grupos de
voz), espaços de nascer no aeroporto e na praia (`SpawnPoint`), a lista de jogadores no fim
(`EndScreen`), o `SteamRichPresence`. Método do guia: subir de 1 em 1 e anotar o primeiro sintoma.

## Ordem de trabalho

O mapa completo do que é "de cada jogador" (online × offline, vida e morte, ajuda, itens, perigos, save) e a
ordem detalhada estão em [ANALISE-ONLINE-OFFLINE.md](ANALISE-ONLINE-OFFLINE.md).

✅ = testado no jogo (celulares de mentira do `pa phone-sim`, prints e log); 🧪 = código pronto, sem teste dirigido
ainda; ⏳ = falta.

1. ✅ Mod carrega (BepInEx), conecta ao Hub e avisa no celular.
2. ✅ Cada escalador lê o controle da própria vaga (P1 também, com o Hub aberto).
3. ✅ Atores virtuais P2..P10 na sala offline; cada um nasce pelo `PhotonNetwork.Instantiate` do jogo com
   `Character` e `Player` próprios. Aeroporto: P1..P4 no elevador que abre, P5..P10 no saguão em frente. Ilha:
   desmaiados na praia, cada um no ponto de nascer dele (como no online).
4. ✅ Troca de contexto (sem alocação) em volta de tudo o que é do escalador; câmera própria por vaga, corpo
   escondido só na câmera do dono; grade sem cantos vazios até 10 (4+3+3).
5. ✅ HUD por jogador (cópia do `GUIManager` no contexto da vaga), em overlay nativo dentro da parte de cada um
   (nítido mesmo com escala de renderização baixa), nomes sobre a cabeça por quem olha.
6. ✅ "Usar" por vaga (`Interaction` própria): abrir malas, pegar itens; inventário individual conferido
   (P5 com binóculo/lanterna/corneta, os outros vazios). RPC "para o host" vindo de um extra executa na hora.
7. ✅ Aparência por vaga (pele na cor da vaga do Party Anywhere, resto sorteado entre o que a conta tem).
8. 🧪 Perigos por quadro (névoa, vento frio, sol, lava, fontes e campos de status, orbe do sono, casca de
   banana, fogueira), toques (armadilhas, vazio, água-viva…), monstros (`IsLocal` = "deste PC"), janelas por
   dono (o B fecha), pausa sem congelar em grupo, loot "de grupo" no modo sozinho, conquistas só do P1, sair do
   lobby tira o escalador.
9. ✅ Placas "P1".."P10" na cor da vaga, acima da cabeça (só a vaga, sem nome de conta).
10. ✅ Tela do jogo no celular (`PhoneViews` do SDK): quem liga sai da divisão da TV, a câmera e o HUD dele vão
    para a imagem do celular (testado: TV com P1 e P2, P3 e P4 no celular).
11. 🧪 Continuar expedição salva com os extras (o modo sozinho passa a aceitar vários registros; cada vaga volta
    com a posição, status, inventário e item na mão guardados, ou morta), tela de fim no HUD de cada um, menus e
    rodas pelo controle do Hub (UIInputHandler por vaga, seleção própria: direcional/analógico, A, B, LB/RB),
    pausa de extra sem "sair"/"convidar"/"configurações", aparência escolhida no passaporte guardada por vaga
    (`[Aparencia]` no config do mod).
12. ✅ Ficha validada, pacote (`pa pack`) e na Biblioteca do Hub (`pa add .`). ⏳ Release público.

13. ✅ (0.1.1) O que é de cada jogador na tela: espectador por jogador (o `specCharacter` estático vira um por
    vaga; quem morre assiste, os outros seguem jogando), efeitos de tela por câmera (os volumes de pós-processamento
    do HUD de cada um e os de desmaio e queda só valem na câmera do dono), HUD recortado na parte de cada um (o
    binóculo e as telas cheias não vazam), nome "P2" no espectador e no fim da expedição, luz do peito para todos,
    luzes do mundo acesas para todas as câmeras, "CO-OP LOCAL" no lugar de "Sozinho" no menu.

## Desempenho medido (Ryzen 7 5700U, vídeo integrado, 1080p, ilha)

| Jogadores | FPS |
|---|---|
| 1 (o jogo sem dividir) | ~20 |
| 4 | ~9 |
| 7 | ~7 |
| 10 | ~6 |

Desligando as câmeras extras com 10 jogadores, o jogo vai a 11–14: metade do custo é desenhar as visões, a outra
metade é simular 10 bonecos com física. Com mais de uma visão, o mod corta sombra e detalhe só na memória
(`SplitGraphics`); a escala de renderização da pessoa é mantida (o HUD é nativo de qualquer jeito, então baixar a
escala no menu de vídeo do jogo é o ajuste que mais ajuda em PC fraco).
