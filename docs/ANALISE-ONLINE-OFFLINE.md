# PEAK: online × offline × co-op local, jogador por jogador

Análise do código do PEAK 3.0.1 (`Assembly-CSharp`, decompilado só para estudo, fora do repositório) para que
**cada escalador do sofá seja um jogador completo**, igual a quem entra pela internet: próprio corpo, status,
inventário, HUD, câmera, morte, reviver, ajudar os outros, save e estatísticas.

A regra que sai do código: no online, **cada PC roda a "parte de cliente" para o seu próprio personagem**
(`Character.localCharacter`, `IsLocal`, `IsMine`) e o host roda a "parte de servidor" para o mundo. No sofá,
só existe um PC: tudo que no online roda "uma vez em cada PC" precisa rodar **uma vez para cada escalador**, no
contexto dele. Esta lista é o mapa disso.

Legenda de prioridade: 🔴 quebra o jogo se faltar · 🟠 jogabilidade perdida · 🟡 visual/qualidade · ⚪ só P1.

---

## 1. O que muda entre o modo online e o offline (o "Jogar sozinho")

O mod roda a partida no **modo offline do PUN** (sala de mentira). Todos os lugares em que o jogo trata offline
diferente do online (busca por `OfflineMode`, `IsOffline`, `PlayerCount`, `PlayerList`):

| Onde | Offline faz | Online faz | No co-op local | Prio |
|---|---|---|---|---|
| `LootData.IsValidToSpawn` (`banInSolo`) | **Bane itens "de grupo"** do sorteio de loot (offline, ou sala com 1 jogador) | Os itens aparecem | Patch: com 2+ escaladores, valer o online. Os itens banidos só aparecem nos dados (ScriptableObjects): listar em tempo de execução com o mod. | 🔴 |
| `Spawner` / `SingleItemSpawner` (`playersInRoomRequirement`) | Spawners "um por jogador" contam `PhotonNetwork.PlayerList` | Igual | Os atores virtuais estão na sala → **loot escala sozinho**. Precisam estar na sala **antes da ilha carregar** (já é assim: entram no aeroporto). | 🔴 |
| `DestroyBasedOnPlayerCount` / `PSM_AddPlayerCountBasedDespawner` / `PSM_SetSpawnerPlayerCountRequirement` | Props e baús somem com poucos jogadores | Igual | Idem: contam os atores virtuais. | 🔴 |
| `Spawner.OnPlayerEnteredRoom` | — | Quem entra no meio destrava spawners pendentes | Ao criar um ator virtual no meio da partida, chamar os callbacks de "jogador entrou" (`OnPlayerEnteredRoom`) como o Photon faria. | 🟠 |
| `PauseMenuHandler` | **Pausa congela o tempo** (`Time.timeScale = 0`) | Pausa não congela | Com 2+ escaladores, pausar como no online (cada um no seu menu, o jogo segue). | 🟠 |
| `MapHandler.JumpToSegment` (`!IsOffline`) | Não sincroniza névoa pela rede | Sincroniza | Nada a fazer. | ⚪ |
| `Quicksave` (`LobbyType`) | Save marcado "offline" | "online" | O save do sofá é offline; ver seção 4. | 🟠 |
| `AirportInviteFriendsKiosk`, botão Convidar da pausa | Somem | Aparecem | Ok sumir: o convite é o lobby do Party Anywhere. | ⚪ |
| Voz (Photon Voice), ícone de mudo, `AudioLevels` | Desligada | Ligada | Fica desligada: estão todos no mesmo sofá. Os atores virtuais não pegam o microfone (já feito). | ⚪ |
| `SteamRichPresence` | "Jogando sozinho" | "Grupo de N/4" | Opcional: mostrar "N escaladores" para os amigos da Steam. | ⚪ |
| `NetworkingUtilities.MAX_PLAYERS = 4` | Sem efeito | Tamanho da sala | O sofá não usa vagas da sala de verdade. | ⚪ |
| Kick, ban, trocar host, reconectar | — | Existe | Substituído pelo lobby do Party Anywhere (entrar, cair, voltar). | ⚪ |

---

## 2. O que é "de cada jogador" (estado individual)

Tudo abaixo já é **por ator** no jogo. Com um ator virtual por vaga, cada escalador ganha o seu automaticamente.
O trabalho é garantir que a lógica que **atualiza** isso rode no contexto dele.

| Estado | Onde mora | Já é individual? | Falta |
|---|---|---|---|
| Inventário: 3 slots + mochila + slot temporário (`ItemSlot`, `BackpackSlot`, `TemporaryItemSlot`) | `Player` (um por ator) | ✅ (um `Player` por vaga) | HUD dos slots por vaga; trocar item (direcional) já vem do pad. |
| Item na mão (objeto de rede próprio) | `CharacterItems`, `Item` (PhotonView do dono) | ✅ se instanciado no contexto | **Envolver `Item`/`ItemComponent`/`ItemAction` no contexto do `holderCharacter`** (hoje só os componentes do corpo são envolvidos). Ver seção 5. | 
| Stamina, stamina extra, os 15 status (`STATUSTYPE`: Ferimento, Fome, Frio, Veneno, Caranguejo, Maldição, Sono, Peso, Calor, Espinhos, Esporos, Teia, Flecha, Petrificação, Planta carnívora) | `CharacterAfflictions` (45 usos de "local") | ✅ componente do corpo | Já roda no contexto. Conferir efeitos de tela (seção 6). |
| As 26 aflições ativas (`Affliction_*`: InfiniteStamina, Numb, Sunscreen, LowGravity, Invincibility, Chaos, ZombieBite…) | `CharacterAfflictions` | ✅ | `Affliction_MassSuperJump`/`RadiateInfiniteStam` usam `localCharacter` → rodar no contexto de quem tem a aflição. |
| Desmaiado, morto, esqueleto, zumbi, petrificado | `CharacterData` | ✅ | — |
| Fantasma / espectador (`PlayerGhost`, `MainCameraMovement.specCharacter`) | Câmera (singleton) | ✅ com a câmera por vaga | Cada vaga morta escolhe quem assiste com o próprio pad. |
| Aparência (cor, chapéu, roupa, medalha do passaporte) | `CharacterCustomization` (lê do PlayerPrefs de quem hospeda) | ❌ todos nasceriam iguais a P1 | Cada vaga com aparência própria: começar pela cor da vaga do Party Anywhere; depois deixar cada um montar a sua no passaporte (salvo por vaga, no config do mod). |
| Nome sobre a cabeça (`UIPlayerNames`, `characterName` = NickName do ator) | Ator | ✅ ("P2 Fulano") | Desenhar no HUD de cada câmera (seção 6). |
| Diário/estatísticas da expedição (`CharacterStats`: linha do tempo, altura, quedas, comidas…) | Corpo | ✅ | Tela de fim por vaga (seção 7). |
| Valores da corrida para conquistas (`AchievementManager.runBasedValueData`) | Singleton do PC | ❌ um só | Só P1 ganha conquistas/emblemas da Steam (é a conta dele). Os extras: guardar os números deles para a tela de fim, sem tocar na Steam. |
| Id de conta (`character.data.userID`, `UserID` do ator) | Ator | ✅ id falso por vaga (feito) | Precisa ser **estável por vaga** para o save (seção 4). |

---

## 3. Vida, morte e ajuda entre jogadores (o coração do co-op)

Todas essas mecânicas já existem para "outros jogadores". O que faz elas funcionarem no sofá é: o alvo ser um
`Character` de verdade (✅ com os atores virtuais) e quem age estar no contexto certo.

| Mecânica | Código | Situação no sofá | Prio |
|---|---|---|---|
| Desmaiar quando a soma de status enche (`HandleLife` → `RPCA_PassOut`) | `Character` | ✅ roda no contexto do corpo | — |
| Morrer (esqueleto no chão), `RPCA_Die`, timer de morte | `Character` | ✅ | — |
| **Fim de jogo quando todos morrem** (`CheckEndGame`: todos de `AllCharacters` mortos) | `Character` | ✅ conta os extras | — |
| **Vencer** (`CheckWinCondition`: no pico ou na corda do helicóptero) por personagem | `Character.RPCEndGame` | ✅ cada um vence ou perde | — |
| **Carregar um amigo desmaiado nas costas** (`CharacterCarrying`, interação "CARRY"/"DROP") | `CharacterInteractible` | ✅ alvo é personagem; o "usar" precisa ser do contexto de quem carrega | 🔴 |
| **Dar comida/usar item no amigo** (`canUseOnFriend`, `FeedItem`/`GetFedItemRPC`, curativo, remédio) | `CharacterInteractible.IsSecondaryInteractible` (usa `localCharacter.data.currentItem`) | Precisa da interação por vaga (seção 5) | 🔴 |
| **Puxar o amigo pela mão / ajudar a subir** (`CharacterGrabbing`, `isReaching`) | Corpo | ✅ (física entre corpos) | — |
| Descolar quem ficou preso (`IsStuck` + `IsLocal`) | `CharacterGrabbing` | `IsLocal` = "é o meu"; trocar por "é de alguém do sofá" | 🟠 |
| **Gancho de resgate** (`RescueHook`: puxa um amigo até você) | Item | Envolver o item no contexto do dono; `targetPlayer.photonView.IsMine` já vale para extras | 🟠 |
| **Reviver**: estátua de escoteiro (`ScoutEffigy`), baú de reviver (`RespawnChest.RespawnAllPlayersHere`), reviver na fogueira (`ScoutsWereRevivedAtCurrentBaseCamp`), `RPCA_ReviveAtPosition` | Mundo, host | ✅ itera todos os personagens | — |
| Canhão de escoteiro (lança amigos), Bing Bong, sacrifício (`Action_SacrificeFriend`), adaga ritual, `Action_WarpToRandomPlayer` | Itens | Envolver no contexto do dono | 🟠 |
| **Moral perto da fogueira e de amigos** (`MoraleBoost`, `Campfire.moraleBoostRadius`, proteção da fogueira) | `Campfire.Update` usa só `localCharacter` | ❌ só P1 ganha → **rodar a parte "local" para cada vaga** | 🔴 |
| Acender a fogueira e seguir para o próximo trecho (exige todos perto) | `Campfire` (itera todos) | ✅ | — |
| Canibalismo (comer amigo morto, `Interact_CastFinished` + `interactor.IsLocal`) | `CharacterInteractible` | `IsLocal` do interator: contexto | 🟡 |
| Zumbi (`MushroomZombie`) caça jogadores, mordida (`Affliction_ZombieBite`) | Mob | Alvos ✅; dano aplicado se `IsLocal` → seção 5 | 🟠 |
| Escoteiro-chefe (`Scoutmaster`: aparece para quem se afasta do grupo) | Mob | Alvo `IsLocal` | 🟠 |
| Reconectar: quem cai volta com o mesmo estado (`ReconnectHandler`, `ReconnectData`) | Host | Os atores virtuais não "caem" do Photon; a vaga em RECONNECTING fica parada e protegida | 🟠 |

---

## 4. Save, progresso e "cada um com o seu"

| Item | Como é | O que fazer |
|---|---|---|
| **Quicksave da expedição** (`quicksave.peak`, salvo na fogueira) | `RunProgress` + uma lista `PlayerRunData` **por id de conta** (hash SHA-256), vinda dos registros do `ReconnectHandler` | Os extras entram no save **se** o `ReconnectHandler` tiver registro deles (id falso por vaga). Ao carregar, o jogo só aplica o do host (`SavedHostState`); os outros chegam pelo `RPC_ReconnectingPlayerSpawn`, que o jogo **não manda** para atores locais → o spawn do mod precisa ler o registro da vaga (`ReconnectHandler.TryGetReconnectData(id)`) e aplicar com `ApplyAndPushReconnectData`. 🔴 para continuar uma expedição salva. |
| Id estável por vaga | Hoje `9000000000000000 + vaga` | P2 de hoje é o P2 do save. Se a ordem do sofá mudar, o personagem salvo vai para quem estiver na vaga (mesma regra do Party Anywhere: a vaga é a identidade). |
| Conquistas, emblemas, estatísticas da Steam (`AchievementManager`, `Action_IncrementSteamStat`, `BadgeUnlocker`) | Da conta Steam do PC | **Só P1.** No contexto de uma vaga extra, não chamar a Steam (patch: ignorar `ThrowAchievement`, `IncrementSteamStat` e `BadgeUnlocker` fora do P1). Os números dos extras ficam para a tela de fim. |
| Progresso de "ascents" (dificuldades desbloqueadas), cosméticos desbloqueados | Conta de P1 | Valem para todos no sofá (é a partida de P1). |
| Aparência de cada um | PlayerPrefs de P1 | Por vaga, no `BepInEx\config` do mod (seção 2). |
| Configurações (sensibilidade, inverter, FOV) | Do jogo, globais | Sensibilidade por vaga é do mod (o pad já gira em graus). |

---

## 5. Lógica "só do jogador local" que precisa rodar para cada um

São **159 arquivos** que dependem de `Character.localCharacter`, `Player.localPlayer` ou `IsLocal`. Eles
caem em cinco padrões, e cada padrão tem uma solução só:

### 5.1 Componentes do próprio corpo / Player / câmera ✅ (feito)
`Character`, `CharacterMovement`, `CharacterAfflictions`, `CharacterItems`, `CharacterClimbing`,
`CharacterData`, `CharacterStats`, `CharacterAnimations`, `CharacterBalloons`, `CharacterRagdoll`,
`CharacterCustomization`, `HideTheBody`, `AnimatedMouth`, `EyeBlinkController`, `PlayerEyeLook`,
`MainCameraMovement`, `CameraOverride_*`… Solução: `SeatComponents` envolve Update/LateUpdate/FixedUpdate/
Start/OnEnable/OnDisable no contexto da vaga.

### 5.2 Itens na mão 🔴
`Item`, `ItemComponent` (36 tipos: Balão, Colmeia, Bing Bong, Cacto, Vela, Piton, Construível, Dinamite,
Sinalizador, Cozinhar, Jetpack, Nocaute, Lanterna, Feijão mágico, Corneta mágica, Mandrágora, Amuletos,
Paraquedas, Adaga ritual, Honra de escoteiro, Teleporte, Gancho de resgate, Mochila-foguete, Lança-corda,
Rolo de corda, Amarrar corda, Bola de neve, Lança-cipó, Planador…) e `ItemAction` (52 ações: comer, curar,
aflições, super pulo, binóculo, passaporte, guia, invocar escoteiro-chefe, teleportes…). Usam
`holderCharacter.IsLocal` e `Character.localCharacter`. Solução: no prefixo de contexto, para
`Item`/`ItemComponent`/`ItemAction`, a vaga é a do `holderCharacter` (e de quem lançou, para itens no ar).

### 5.3 Perigos e zonas do mundo que aplicam efeito "em mim" por quadro 🔴
Rodam em `Update`/`FixedUpdate` e olham só `Character.localCharacter`:
`Fog` (frio na névoa), `FogSphere`, `Peak/AntiSphere`, `OrbThatMakesYouSleepy`, `WindChillZone` (vento frio),
`HotSun` (sol queimando), `Lava`, `LavaRising`, `StatusEmitter` (fontes de status), `Peak/StatusFieldBase`
(e `StatusFieldGloom`), `InjurySphere`, `BananaPeel`, `FallPost`/`PassOutPost` (efeito de tela), `Campfire`
(moral e proteção), `MountainProgressHandler` (títulos de bioma ao chegar), `Peak/GloomSafeZone`, `Mirror`.
Solução: **rodar só a parte "aplicar em mim" uma vez por vaga extra**, no contexto dela, sem repetir a parte do
mundo (mover a névoa, timers compartilhados). Um patch por tipo (Postfix que, para cada vaga, entra no contexto e
chama o método de efeito: `Fog.MakePlayerCold`, `Lava.Heat`, `WindChillZone.ApplyStatus`…). Onde o estado é por
jogador (ex.: `BananaPeel.counter`, `WindChillZone.localCharacterInsideBounds`), guardar uma cópia por vaga.

### 5.4 Gatilhos e colisões com `character.IsLocal` 🔴
`Peak/KillPlane` (cair no vazio), `Peak/SpikeTrap`, `Peak/SpikeRoller`, `Peak/MovingSawBlade`, `SwingingAxe`,
`SlipperyJellyfish` (água-viva), `TumbleWeed`, `ShakyRock`, `ShakyIcicleIce`, `StickyCactus`, `StatusTrigger`,
`TickTrigger`, `TombTrigger`, `SpiderTrigger`, `SpecialDayZone`, `WaterfallPusher`, `PlayerMoveZone`,
`BreakableBridge`, `AOE` (explosões), `ThornOnMe`/`ArrowOnMe`, `CactusBall`, `Peak/EnviroHelpingHand`,
`ScoutCannonAchievementZone`. Solução genérica: envolver `OnTrigger*`/`OnCollision*` desses tipos no contexto
do personagem que encostou (`CharacterRagdoll.TryGetCharacterFromCollider`).

### 5.5 Monstros que só "machucam" o alvo local 🟠
`Spider`, `Antlion`, `Tornado`, `Scorpion`, `FrogTongue`, `VenusFlyTrap`, `Mob`, `MushroomZombie`,
`Condor`, `Beehive`/`BeeSwarm`, `Scoutmaster`. Escolhem alvo entre todos (✅), mas o efeito roda em
`if (target.IsLocal)`. Solução: quando o alvo é de uma vaga do sofá, aplicar no contexto dela
(`IsLocal` = "este personagem é controlado neste PC", que é o significado no online).

### 5.6 Interface e entrada "do jogador local" 🔴
`Interaction` (singleton: raio da câmera para "o que estou olhando" e o "usar", 23 usos), `GUIManager`
(singleton, 53 usos), `BackpackWheel`/`EmoteWheel` (rodas), `UI_UseItemProgress`, `UI_Stamina`,
`PointPinger` (ping), `PauseMenuHandler`, `PassportManager`, `BoardingPass`, `EndScreen`.
Solução: uma cópia de `Interaction` e do HUD do `GUIManager` por vaga, com o singleton trocado no contexto
(o mesmo que já é feito com a câmera). Menus de tela cheia (pausa, passaporte) abrem na parte de quem apertou.

---

## 6. Visual, câmera e som por jogador

| Item | Prio |
|---|---|
| Câmera por vaga com a grade sem cantos vazios (feito: cópia da `MainCamera` + `MainCameraMovement`) | 🔴 |
| Efeitos de status na tela (`GUIManager.*FX`: frio, calor, veneno, sono, ferimento, esporos, espinhos, petrificar, açúcar, energético, protetor solar, dormente) e o pós-processamento (`PassOutPost`, `FallPost`, `IllegalScreenEffect`): hoje só na câmera de P1 → cada câmera com o seu volume | 🟠 |
| Esconder o próprio corpo/cabeça só na própria câmera (`HideTheBody`, camada por vaga) | 🟠 |
| Binóculo, câmera de canhão, "God cam" (`CameraOverride`) por vaga | 🟡 |
| Névoa visual e partículas que seguem "o jogador" (`Fog.ApplyVisuals` põe a névoa na posição de P1) | 🟡 |
| Nomes e pings sobre as cabeças desenhados no HUD de quem vê | 🟡 |
| Som: só a câmera de P1 escuta (feito); sons "2D" do jogador local (respirar, coração, frio) só de P1 | 🟡 |
| Desempenho: preset de vídeo leve só na memória com 2+ telas; HBAO e efeitos temporais desligados nas extras | 🟠 |

---

## 7. Fases, eventos e fluxo da partida

| Etapa | O que olhar | Prio |
|---|---|---|
| **Aeroporto** (lobby): check-in (`AirportCheckInKiosk`), cartão de embarque (`BoardingPass`), escolher a "ascent" (dificuldade), entrar no avião | Só P1 escolhe e confirma (ele é o anfitrião); os extras andam pelo aeroporto. Atores virtuais criados aqui (antes da ilha: loot por jogador). | 🔴 |
| **Ilha**: praia (todos começam desmaiados na areia), Tropical/Raízes, Alpino/Mesa, Caldeira/Forno, Pico, e o Vazio/Nadir | Extras nascem como no online: desmaiados na praia (`StartPassedOutOnTheBeach`) perto do ponto de nascer de cada ator (`SpawnPoint.GetSpawnPoint(actor)`) | 🟠 |
| Biomas de cada partida variam (`Biome.BiomeType`: Shore, Tropics, Alpine, Volcano, Peak, Mesa, Roots, Swamp, Temple, Grasslands, Ocean, Skyblock, Hell) | Perigos de cada bioma estão nas listas 5.3–5.5 | — |
| **Névoa / lava / "gloom" subindo** (`Fog`, `LavaRising`, `OrbFogHandler`, avisos "The fog rises") | Mundo: host. Efeito por vaga (5.3). Avisos aparecem para todos. | 🔴 |
| **Fogueira** entre trechos: acender exige todos perto, moral, save, reviver | 5.3 + seção 4 | 🔴 |
| Dia e noite (`DayNightManager`), clima, eventos de "dia especial" (`SpecialDayZone`) | Mundo, compartilhado | — |
| "Mini run" e corridas customizadas (`RunSettings`) | Mesmas regras | 🟡 |
| **Fim**: helicóptero/pico (`PeakSequence`, `PeakHandler.EndCutscene`), tela de fim (`EndScreen`) com diário de cada escoteiro | Tela de fim por vaga ou uma só com todos os diários | 🟠 |
| Cena de corte do fim: tela inteira | 🟡 |

---

## 8. Ordem de implementação e estado

1. ✅ Atores virtuais, contexto, câmera e HUD por vaga, nascimento como no online (aeroporto e praia).
2. ✅ Itens no contexto do dono e `Interaction` por vaga: abrir mala e pegar item testados; inventário individual.
   (Achado: o "pegar" é um RPC para o host, que o Photon descartava no contexto do extra; todo ator do sofá conta
   como host agora.)
3. 🧪 Gatilhos/colisões (5.4), perigos por quadro (5.3) e monstros (5.5): código pronto, sem teste dirigido.
4. 🧪 `banInSolo`, pausa sem congelar em grupo, conquistas só do P1, janelas por dono.
5. ✅ HUD nítido por vaga (overlay nativo na parte de cada um) e nomes sobre a cabeça por quem olha.
6. ✅ Aparência por vaga (cor da vaga). ⏳ Escolhida pela pessoa no passaporte.
7. ⏳ Save/continuar com os extras, tela de fim por vaga, menus navegáveis pelo controle do Hub, tela no celular.

Cada etapa testada com 2+ jogadores pelo checklist "não perdi nada" do `pa docs coop` antes de publicar.
