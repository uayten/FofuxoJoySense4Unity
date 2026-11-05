# Decisões

<!-- project-docs: só o que vale agora. Próximo ID: D-014 · Próximo fato: F-015 -->

## Decisões

### D-001 Nomes
Decisão: exibido como "Fofuxo's JoySense4Unity"; repositório `uayten/FofuxoJoySense4Unity` (público); pacote `com.uayten.fofuxojoysense`; namespace `Fofuxo.JoySense`; assembly `Uayten.FofuxoJoySense`. O README abre dizendo que é o equivalente para Unity do JoyShockLibrary4Unreal.
Por quê: os nomes JoyShockLibrary e JSL4U pertencem ao Julian Smart e ao Renan, não ao autor deste pacote. "Fofuxo's" é a marca do autor, como no Fofuxo's Gameplay Ability System; JoySense = Joy-Con + DualSense.
Origem: usuário

### D-002 C#, com DLL nativa só onde o C# não alcança
Decisão: o pacote é em C#. As APIs do Windows são chamadas por P/Invoke em DLLs do sistema, e a entrada chega pelo backend HID do próprio Unity, com um layout por controle. DLL nativa só para um transporte que o Mono não alcança, como o GATT Bluetooth LE do Switch 2 (WinRT).
Por quê: uma DLL nativa fica travada pelo Editor aberto (cada recompilação exige reiniciar o Unity), precisa de toolchain C++ e de binários commitados, e divide a depuração; os protocolos são trabalho de bytes que o C# faz igualmente bem.
Origem: sugestão da IA aceita
Não escolhido: DLL nativa com API em C a partir do núcleo C++ do JSL4U - esse núcleo usa tipos do Unreal em toda parte, então seria uma reescrita de qualquer jeito.
Afeta: CLAUDE.md (Arquitetura)

### D-003 Os controles são dispositivos `Gamepad`
Decisão: cada controle é um dispositivo que herda de `Gamepad`, com os botões mapeados por posição, como o Unity mapeia os controles Nintendo dele (`buttonSouth` = B). Os usages seguem o esquema da Nintendo, também como no Unity: o A tem `Submit`/`PrimaryAction` e o B tem `Cancel`/`Back`, então confirmar e voltar ficam no A e no B. Botões sem equivalente em `Gamepad` são controles extras.
Por quê: os bindings `<Gamepad>` existentes e a tela de remapeamento do MyDragonShop precisam funcionar sem mudança.
Origem: usuário (herdar de `Gamepad`); sugestão da IA aceita (mapeamento posicional); usuário (A confirma e B volta, como na Nintendo)
Afeta: README.md (Supported controllers)

### D-004 Desenvolvido dentro do MyDragonShop, levado ao repositório à mão
Decisão: o pacote vive como pasta comum no `Packages/` do MyDragonShop, sem `.git` próprio, e só é levado para `uayten/FofuxoJoySense4Unity` à mão, quando o usuário pedir.
Por quê: o sócio recebe as mudanças do pacote junto com o jogo, como no Fofuxo GAS.
Origem: usuário
Não escolhido: submódulo git ou clone aninhado em `Packages/`.

### D-005 Licença e avisos
Decisão: MIT, copyright "uayten". O THIRD-PARTY-NOTICES.md credita o JoyShockLibrary4Unreal (Renan e colaboradores), com o aviso MIT do JoyShockLibrary, o GamepadMotionHelpers (Jibb Smart) e o hidapi. Cada aviso entra quando o pacote passa a conter aquele trabalho.
Por quê: os protocolos são portados de código MIT, cujo aviso precisa acompanhá-lo.
Origem: usuário

### D-006 Idioma dos arquivos
Decisão: código (identificadores, comentários, logs) e THIRD-PARTY-NOTICES.md em inglês; CLAUDE.md, CHANGELOG.md, ROADMAP.md, DECISIONS.md e HANDOFF.md em português. O README completo é o `README.pt-BR.md`; o `README.md`, que o GitHub mostra por padrão, fica em inglês e por enquanto só aponta para ele num aviso no topo, dizendo que a versão em inglês está em desenvolvimento.
Por quê: o usuário lê esses arquivos; o código segue o padrão de pacote público, e a tradução do README para o inglês fica para depois.
Origem: usuário
Afeta: CLAUDE.md (Idioma), CLAUDE.md do MyDragonShop (Pacote de controles), R-009

### D-007 Controles por Bluetooth entram como dispositivo do próprio pacote
Decisão: por Bluetooth o Windows não cria dispositivo HID para o Switch 2, então o pacote adiciona ele mesmo um dispositivo do mesmo layout (`Switch2ProController`, interface `JoySenseBLE`, serial = endereço Bluetooth) e entrega cada relatório como evento de estado, com o report id recolocado na frente, como o USB entrega. A DLL nativa `JoySenseBle` só faz o transporte (scan, conexão, comandos, relatórios), com uma API em C sem callbacks: o C# consulta as descobertas, os relatórios e as linhas de log. As conexões ficam na DLL e sobrevivem ao domain reload; o domínio novo as adota pelos handles e reencontra os dispositivos que o Input System restaurou.
Por quê: um callback do nativo para um domínio já descarregado derruba o Editor; recompilar scripts não deve desconectar o controle; um layout só serve aos dois transportes.
Origem: sugestão da IA aceita (Bluetooth aprovado em 2026-10-13)
Afeta: CLAUDE.md (Arquitetura)

### D-008 Por Bluetooth, só controles em modo de pareamento ou pareados com este PC
Decisão: o pacote só conecta um Switch 2 que anuncie sem host (SYNC segurado, e então o pareia com o PC) ou com o endereço deste PC como host. Um pareado com outro aparelho é ignorado, com uma nota no console por controle, e volta a ser olhado a cada 2 s, para que segurar SYNC nele seja percebido.
Por quê: um botão apertado num controle pareado com um console na mesma sala, para usar o console, tiraria o controle dele. Diverge do JSL4U, que conecta qualquer Switch 2 que anuncie.
Origem: usuário
Afeta: README.pt-BR.md (Conectar por Bluetooth)

### D-009 Movimento: controles sobre o relatório cru, fusão no `onEvent`, API nos eixos do Unity
Decisão: `gyro` e `accelerometer` são controles `noisy` que leem os bytes do relatório, como os sticks (`MotionSensorAxis`, com escala e drift definidos em tempo de execução). A fusão (`GamepadMotion`, porte do JSL4U) roda em `FilterReport`, uma vez por relatório, no espaço do JSL4U; só a fronteira pública converte para os eixos do Unity. Gravidade e orientação são propriedades, porque não estão no relatório. `ConsumeRotation` soma o giro de todos os relatórios desde a última chamada. Por Bluetooth, cada relatório leva a hora em que chegou como hora do evento.
Por quê: o relatório continua sendo o estado, sem reescrever eventos (F-004), e `noisy` impede que o sensor roube o `.current` ou vire binding. A fusão precisa de todas as amostras, e `onEvent` é o único lugar que as vê; um jogo a 60 FPS lê só 1 em cada 8. O Bluetooth enfileira os relatórios juntos a cada update, o que zeraria o intervalo entre eles. Os sinais do JSL4U foram medidos no hardware e por isso não são refeitos para o Unity.
Origem: sugestão da IA aceita (gyro aprovado em 2026-11-01, por USB e Bluetooth)

### D-010 Dead zone por função, e o gyro como controle que o jogo liga a uma action
Decisão: a dead zone de cada binding de stick é o Stick Deadzone do Input System, com `min` e `max` explícitos; o pacote não tem processor próprio, porque um processor do pacote num binding `<Gamepad>` faria esse binding, que vale para qualquer controle, dar erro e parar de funcionar quando o pacote fosse removido (o Input System descarta o binding cujo processor não acha). Todo controle com movimento dá ao `gyroLook` a usage `GyroLook`, e o binding é `*/{GyroLook}`, sem nomear modelo. `JoySenseSupport.RemoveGlobalStickDeadzone()` zera o filtro global pelas configurações do Input System; é uma escolha do jogo, porque vale para todos os gamepads. `gyroLook` é um controle com os graus girados na input update atual, cujo valor fica no controle e não no estado, e sua posição sobre os bytes do gyro é o que avisa as actions a cada relatório. O gyro só é lido por actions, sem dead zone: o `ConsumeRotation` e o `gyroDeadZone` do dispositivo saíram.
Por quê: o usuário quer a configuração por função na janela de Input Actions, como no Enhanced Input da Unreal, sem o jogo guardar lógica de gyro. Zerar pelas configurações em vez de um override de layout evita recriar os dispositivos, o que perderia a ligação com o Bluetooth e a calibração. O gyro não pode ser um delta como o do mouse, que depende de callback de estado (F-004).
Origem: usuário (a meta) e sugestão da IA aceita (o desenho); validado em Play, pelo cabo e pelo Bluetooth (R-011)
Não escolhido: o processor `JoySenseStickDeadzone`, que existiu até o pacote poder ser removido sem quebrar os bindings genéricos; a única diferença para o do Input System era `min=0` valer 0 em vez de "o valor global", o que dá no mesmo com a global zerada. Também não escolhido: uma dead zone para o gyro. Houve um processor `JoySenseGyroDeadzone` em forma de portão (nada passava com o controle parado na mão; um giro de mais de 10° em 0,25 s liberava tudo por 3 s, e outro giro desses renovava), no commit 1c95c64 do MyDragonShop. Saiu sem ser testado no hardware: depois do D-012 o usuário achou o gyro bom sem dead zone pelo cabo e pelo Bluetooth, os jogos de gyro bem feitos oferecem sensibilidade e não dead zone, e para o tremor a indicação do GyroWiki é a suavização em camadas, não dead zone.

### D-012 O `gyroLook` gira pelo tempo, não pela chegada dos relatórios
Decisão: a velocidade de cada amostra vale do momento em que foi tirada até a próxima, e cada input update recebe o trecho disso que ela cobre: as amostras que chegaram nela e, no `onAfterUpdate`, a última velocidade até o `InputState.currentTime`. A velocidade guardada vale no máximo 50 ms sem amostra nova. Vale para o cabo e para o Bluetooth. O relógio do Input System volta para trás ao entrar ou sair do Play sem domain reload, e a contagem só andava para a frente: o gyro parou até ela recomeçar quando a hora fica mais de 0,1 s para trás.
Por quê: por Bluetooth chegam uns 70 relatórios por segundo, e com o jogo a mais fps que isso metade dos frames recebia giro zero e a outra metade um degrau dobrado, então a câmera andava em escada. Pelo cabo, com 500 por segundo, isso não aparecia. O total girado é o mesmo; muda só como ele se distribui pelos frames, sem atraso a mais.
Origem: pedido do usuário (Bluetooth suave) e sugestão da IA aceita (segurar a velocidade); a melhora no Bluetooth ainda a validar (R-011)
Não escolhido: interpolar entre amostras — suaviza mais, mas atrasa a câmera em um intervalo inteiro (~14 ms no Bluetooth).
Afeta: R-011, `Switch2ProController.CreditHeldTurn`

### D-013 Sem Bluetooth em batch mode, o que inclui os processos de importação do Editor
Decisão: `Switch2Bluetooth.Start` só roda fora de batch mode.
Por quê: `InitializeOnLoad` também roda nos `AssetImportWorker`, que são Unity em batch mode, e cada um procurava e conectava o controle por conta própria. O registro em arquivo mostrou dois processos conectados ao mesmo controle. A desconexão do Editor não derrubava o link enquanto um processo desses ainda o segurasse.
Origem: sugestão da IA aceita
Não escolhido: testar só `AssetDatabase.IsAssetImportWorkerProcess()` - um batch mode de CI também não precisa de controle.
Afeta: `JoySenseSupport.Initialize`, F-013, R-013

## Fatos verificados

### F-014 O Input System só deixa testar actions em Play
Fato: fora do Play, relatórios enviados com `InputSystem.Update()` chegam ao `onEvent` e aos controles, mas a action fica em `Waiting`, e uma update `Dynamic` forçada por reflexão não processa os eventos. Controles e processors dá para conferir no Editor parado; actions, não.
Fonte: simulação no Editor com relatórios sintéticos
Verificado: 2026-11-01
Afeta: R-011

### F-012 Pelo cabo, cada relatório chega ao Unity com a própria hora
Fato: pelo USB, o backend HID entrega 500 eventos por segundo, e em 3402 intervalos seguidos todos ficaram entre 1 e 3 ms, sem nenhum zero. A hora do evento serve direto como intervalo entre amostras do movimento. Por Bluetooth, com a hora de chegada que o pacote marca, foram 70 por segundo, a maioria entre 6 e 20 ms: 28 de 567 intervalos passaram de 20 ms, nenhum saiu fora de ordem.
Fonte: `InputSystem.onEvent` medindo `eventPtr.time` por 6,8 s no cabo e 8,1 s no Bluetooth, com o Editor sem foco
Verificado: 2026-11-01
Afeta: D-009

### F-013 Uma conexão Bluetooth pode ficar viva e muda
Fato: numa reconexão por botão, o controle mandou no máximo poucos relatórios e parou. A conexão seguia aberta e os 12 comandos de setup, reenviados, terminaram em 388 ms sem nenhuma falha, mas nenhum relatório chegou depois disso, nem à DLL. A calibração também não foi lida nessa conexão. Aconteceu de novo ao tirar o cabo e acordar o controle por botão, também com a calibração falhando. Ali a recuperação rodou: setup de novo, ainda mudo, link derrubado. Mas, com a DLL sem nenhuma conexão, o Windows seguiu com o controle conectado (`IsConnected` do nó `BTHLE\DEV_`), e o controle sem anunciar não reconectava por botão; desligar e ligar o Bluetooth do Windows o soltou. Quem segurava o link não se sabe. Repetida a mesma sequência logo depois, conectou e funcionou. A causa do silêncio é desconhecida. Medido com o caso na mão, de outro processo, sem Steam e sem nenhuma conexão na DLL do Unity: os 12 comandos de setup respondem em cerca de 30 ms e a leitura de memória devolve dados, mas nenhuma das 6 características com notificação manda nada, nem a `...fd2` de entrada, com a CCCD dela lendo `01-00`. Não religaram o fluxo: repetir o setup, alternar o modo de relatório (0x05, 0x0a, 0x09), reenviar a seleção de features nem pedir intervalos de 15, 60 e 180 ms. Escrever `00-00` na CCCD da `...fd2` responde sucesso e ela segue em `01-00`, mas o mesmo acontece numa conexão sadia com a DLL assinando, então isso não é o defeito. Fechar o Unity com o caso na mão soltou o link em poucos segundos, depois de uns 15 minutos preso, então quem segura é o processo do Unity. Fora do Unity, numa conexão sadia, `JsBle_Disconnect` solta o link em uns 10 s com o processo vivo, e uma conexão recém-aberta transmite uns 200 a 280 relatórios por segundo assim que assina a `...fd2`, sem nenhum comando de setup. O que o Unity retém além do fechamento da DLL não se sabe. Desta vez o caso veio pouco mais de um minuto depois de uma recompilação, com o controle desconectado nela; a relação não está provada. Um cliente preso com `MaintainConnection` ficou como hipótese: o teste não valeu, porque o controle não conectou durante ele. O usuário relata travamentos parecidos em outros softwares e com outros controles, inclusive num jogo do Steam, e suspeita do hardware; isso não foi medido. A investigação parou aqui por ser difícil de reproduzir. Depois, `bluetooth.log` mostrou o Editor e um `AssetImportWorker` conectados juntos ao controle (D-013), o que explica quem segurava o link: sem os processos auxiliares, `JsBle_Disconnect` no Editor soltou o link em até 5 s. Dois processos mandando setup e lendo memória ao mesmo tempo podem explicar também a calibração que falha e o silêncio, mas isso não foi provado. Os travamentos em outros softwares não têm relação com o pacote.
Fonte: teste do usuário e inspeção no Editor (`s_PendingReports` parado em 0 por 1,5 s com a thread principal presa; `JsBle_GetConnections` em 0 e `Get-PnpDeviceProperty` com o controle conectado)
Verificado: 2026-11-01
Afeta: `Switch2Bluetooth.ReadLoop`

### F-001 O backend HID do Unity expõe o Switch 2 Pro Controller e entrega os relatórios dele
Fato: sem este pacote, o Unity cria um Joystick genérico `HID::Nintendo If_Hid` para 057E:2069 (que lê errado o relatório 0x05). O descritor HID declara os relatórios de entrada 0x05 e 0x09 e o de saída 0x02. Depois do init, o Unity recebe uns 500 eventos por segundo, cada um um relatório de 64 bytes começando pelo report id.
Fonte: lista de dispositivos e eventos do Input System inspecionados no Editor, com o controle plugado
Verificado: 2026-10-08
Afeta: D-002

### F-002 O init por WinUSB e as leituras de SPI funcionam em C#
Fato: a sequência de init e as leituras de SPI do `init_switch2` do JSL4U, enviadas do C# por P/Invoke no `winusb.dll`, recebem todas resposta. A unidade testada não tinha calibração de stick do usuário (apagada, tudo 0xFF) e tinha uma de fábrica legível. O controle continua transmitindo depois que o handle WinUSB é liberado.
Fonte: teste no Editor com o controle plugado e o Steam fechado
Verificado: 2026-10-08

### F-003 O Unity não dá como distinguir dois Switch 2 Pro Controllers
Fato: a descrição do dispositivo no Input System tem serial "00" e nenhum caminho de dispositivo, então não dá para casá-la diretamente com a interface WinUSB do controle.
Fonte: `InputDeviceDescription` inspecionada no Editor
Verificado: 2026-10-08
Afeta: R-008

### F-004 Dispositivos HID próprios não conseguem filtrar o "current" por callback de estado
Fato: no Input System 1.20, `IEventPreProcessor` e `InputManager.DontMakeCurrentlyUpdatingDeviceCurrent` são internos, e um dispositivo que implementa `IInputStateCallbackReceiver` vira o atual a cada evento. Um dispositivo cuja struct de estado é o relatório cru recebe o tratamento normal do Unity: estado inalterado não o torna atual.
Fonte: código do Input System 1.20.0 (`InputManager.cs`, `IEventPreProcessor.cs`)
Verificado: 2026-10-08
Afeta: CLAUDE.md (Arquitetura)

### F-005 A normalização do Unity não expressa a calibração de um stick
Fato: `NormalizeProcessor.Normalize` é linear em [min, max]; `zero` só marca a faixa como bipolar. A calibração de um stick Nintendo é por trechos em torno de um centro deslocado, e por isso o pacote tem tipos de controle próprios para os eixos dos sticks.
Fonte: código do Input System 1.20.0 (`NormalizeProcessor.cs`, `AxisControl.cs`)
Verificado: 2026-10-08

### F-006 Os dois sticks do Switch 2 Pro Controller têm o Y com o mesmo sinal
Fato: no relatório cru por USB, os dois sticks crescem para cima, e nenhum eixo precisa ser invertido. O JSL4U inverte o Y direito em `parse_switch2_report` (`InputHelpers.cpp`) e de novo em `FJoyShockInterface` (`JoyShockInterface.cpp`, `-State.stickRY` para controles que não são Sony). As duas inversões se anulam, e o comentário da primeira, que diz que o eixo vem invertido, não vale para o relatório cru.
Fonte: teste no hardware com o Stick Overlay (o direito saiu invertido enquanto só a primeira inversão estava portada); código do JSL4U
Verificado: 2026-10-08

### F-007 Ejetar o controle pelo Windows com o jogo rodando falha, e é o esperado
Fato: com o jogo rodando, "Ejetar hardware com segurança" recusa o "Controlador de jogo compatível com HID", porque o backend HID do Unity mantém o dispositivo aberto. O pacote não segura nada: a interface WinUSB é liberada logo depois da configuração. Desplugar o cabo direto funciona, e o controle volta sozinho ao ser plugado de novo.
Fonte: teste do usuário no hardware; `Switch2CommandInterface.cs` (bloco `finally` com `WinUsb_Free` e `CloseHandle`)
Verificado: 2026-10-08

### F-008 Um serviço GATT esquecido aberto bloqueia o controle para todos os processos
Fato: com o serviço da Nintendo deixado aberto por uma conexão que falhou (no Editor), qualquer outra tentativa, no mesmo processo ou em outro, recebe "sharing violation" ao abrir o serviço e "acesso negado" (status 3) em toda busca de característica, mesmo com o Windows dizendo que o controle está pareado e que o acesso é permitido. Fechado o processo que o segurava, a conexão funcionou: init aceito e uns 250 relatórios por segundo. Por isso toda falha de conexão fecha o serviço, a sessão e o dispositivo.
Fonte: teste no hardware com a DLL de diagnóstico, antes e depois de fechar o Unity
Verificado: 2026-10-12
Afeta: D-007

### F-009 Esperas bloqueantes do WinRT travam numa thread STA
Fato: chamada de uma thread STA (a do PowerShell), a conexão ficou parada mais de dois minutos dentro de um `.get()`. A DLL agora roda o trabalho bloqueante numa thread MTA própria quando quem chama não está no MTA, e toda espera tem limite (10 s; 5 s para comandos).
Fonte: teste no hardware com a DLL de diagnóstico
Verificado: 2026-10-12
Afeta: D-007

### F-010 O controle mantém o modo de relatório ao trocar o Bluetooth pelo cabo
Fato: depois da configuração por Bluetooth (modo 0x09), plugar o cabo derruba o Bluetooth e o USB passa a entregar relatórios 0x09, cujo arranjo de bytes por USB não é o do 0x05: o stick esquerdo cai nos bytes dos botões. A configuração por USB que roda ao plugar terminou sem erro e mesmo assim o modo continuou 0x09; repetida à mão segundos depois, trocou para 0x05 na hora. Plugado sem Bluetooth antes, o USB entrega só 0x05.
Fonte: relatórios capturados no Editor com `InputSystem.onEvent` durante o teste do Bluetooth
Verificado: 2026-10-12
Afeta: `Switch2ProController.FilterReport`

### F-011 Depois do pareamento, o anúncio do controle traz o endereço deste PC
Fato: em modo de pareamento (SYNC) o controle anuncia o host 000000000000; depois dos comandos de pareamento, ao ser acordado por um botão, anuncia o endereço do adaptador Bluetooth do PC (o mesmo que `JsBle_GetHostAddress` devolve).
Fonte: console do Editor no teste do Bluetooth
Verificado: 2026-10-12
