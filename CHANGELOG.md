# Changelog

## [Unreleased]

### Switch 2 Pro Controller por USB

- **O Switch 2 Pro Controller é um `Gamepad`.** Plugado no cabo, ele é configurado pela interface
  WinUSB e depois transmite a entrada pelo backend HID do próprio Unity, então bindings
  `<Gamepad>`, remapeamento e `PlayerInput` funcionam com ele sem mudança. Os botões seguem a
  convenção posicional do Unity (`buttonSouth` é o B); Home, Capture, C, GL e GR são controles
  extras do `Switch2ProController`. Os cliques dos sticks se chamam "Left Stick Press" e "Right
  Stick Press", como no `Gamepad` do Unity, para não se confundirem com os próprios sticks. Confirmar e voltar seguem o esquema da Nintendo, como no
  controle Switch Pro do Unity: o A é `Submit` e o B é `Cancel`, então as ações de UI ligadas a
  `{Submit}` e `{Cancel}` confirmam no A e voltam no B.
- **Os sticks usam a calibração do próprio controle:** a correção de stick feita pelo jogador, se
  houver uma salva, e a de fábrica caso contrário. Até ela ser lida, ou se não puder ser, os sticks
  usam uma faixa fixa.
- **Limitações conhecidas.** Só Windows. Com vários Switch 2 Pro Controllers conectados, os sticks
  usam a faixa fixa, porque o pacote ainda não sabe qual interface USB pertence a qual dispositivo.
  O controle não pode ser configurado com o Steam (ou outra ferramenta que o controle) aberto; o
  pacote tenta por dez segundos e depois registra um aviso no console.

### Switch 2 Pro Controller por Bluetooth

- **O Switch 2 Pro Controller também funciona sem fio.** Segure o botão SYNC na primeira vez: o
  pacote conecta, pareia o controle com o PC e daí em diante um botão qualquer o reconecta. Ele chega
  como o mesmo `Switch2ProController` do cabo, com os sticks calibrados do mesmo jeito;
  `isBluetooth` diz por qual conexão ele veio. O botão que acorda o controle não chega ao jogo.
- **Só controles em modo de pareamento ou já pareados com este PC.** Um controle pareado com outro
  aparelho, como um console, é ignorado até você segurar SYNC nele.
- **Recompilar scripts não desconecta o controle:** a conexão continua na DLL nativa, e o Editor a
  retoma depois do domain reload.
- **Trocar de conexão com o jogo rodando.** Plugar o cabo com o controle conectado por Bluetooth
  passa para o USB em uns dois segundos, sem botões fantasmas: o controle continua no formato de
  relatório do Bluetooth, que o pacote descarta enquanto o configura de novo para o USB. Tirar o
  cabo desliga o controle, e um botão o reconecta por Bluetooth.
- **Conexões que falham se recuperam.** Uma conexão que não completa fecha tudo o que abriu, para
  não travar o controle para a próxima tentativa (inclusive de outro aplicativo); um controle que
  conecta mas não manda entrada é configurado de novo e, se continuar mudo, desconectado, para um
  botão reconectá-lo. A leitura da calibração é repetida antes de o pacote desistir dela.
- **Uma DLL nativa (`JoySenseBle.dll`)**, só para Windows x64, faz a parte Bluetooth, que o Mono não
  alcança. O código dela vem junto, em `Native~/JoySenseBle`.
- **Limitações conhecidas.** Só o Pro Controller; os Joy-Cons 2 que aparecem são ignorados.

### Giroscópio e acelerômetro

- **O Switch 2 Pro Controller entrega movimento, pelo cabo e por Bluetooth.** `gyro` (graus por
  segundo) e `accelerometer` (g) são controles, usados nos bindings como
  `<Switch2ProController>/gyro`; `gravity`, `linearAcceleration`, `orientation` e `rawOrientation` são
  propriedades. Tudo nos eixos do Unity, então `orientation` pode ir direto para um `Transform`.
- **`ConsumeRotation()` para mirar:** devolve quanto o controle girou desde a última chamada, somando
  todas as amostras, no espaço escolhido em `gyroSpace` (do controle, do mundo ou do jogador).
  `gyroDeadZone` ignora giros mais lentos que o valor dado, em graus por segundo, sem salto ao passar dele.
  Depois de um tempo sem chamadas, como num menu, o giro acumulado é descartado.
- **O gyro nas Input Actions.** `<Switch2ProController>/gyroLook` traz os graus girados no frame, como o delta
  de um mouse (x para a direita, y para cima). O processor `JoySenseGyroDeadzone` dá a dead zone do binding, e
  `gyroSpace` agora começa em `Player`.
- **`IMotionController`:** a interface comum dos controles com sensores, para um jogo não depender do modelo.

### Dead zone por função

- **`JoySenseStickDeadzone`:** um processor com mínimo e máximo para cada binding de stick, em que 0 significa
  0, e não "use o valor global" como no Stick Deadzone do Input System.
- **`JoySenseSupport.RemoveGlobalStickDeadzone()`** tira a dead zone global de todos os gamepads, para o valor
  chegar limpo aos bindings. O jogo decide se chama.
- **O drift do gyro é calibrado sozinho** enquanto o controle fica parado ou se move sem solavancos.
  `gyroCalibrationMode` troca para manual (`StartGyroCalibration`, `StopGyroCalibration`), e
  `gyroCalibrationOffset` permite salvar e restaurar o resultado.

### Diagnóstico

- **Stick Overlay.** `Tools > Fofuxo JoySense > Stick Overlay` desenha os sticks do gamepad atual
  sobre a tela do jogo no Play Mode: onde cada stick está, o valor que o jogo lê, o valor calibrado
  antes da zona morta e quais direções do stick contam como apertadas. Num controle com sensores,
  mostra também a orientação e as leituras do movimento.
