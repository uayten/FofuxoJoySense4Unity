# Fofuxo's JoySense4Unity

**Português** · [English](README.md) (em desenvolvimento)

O equivalente para Unity do [JoyShockLibrary4Unreal](https://github.com/AgentOttsel/JoyShockLibrary4Unreal):
suporte nativo a controles Sony e Nintendo no Input System do Unity, começando pelo
**Switch 2 Pro Controller da Nintendo, por USB e por Bluetooth**.

Um controle suportado aparece como um `Gamepad` comum, então bindings `<Gamepad>` e telas de
remapeamento que já existem funcionam com ele sem mudança: sem Steam Input, sem drivers, nada para
configurar.

> **Situação:** em desenvolvimento, ainda sem versão lançada.

## Sumário

- [Controles suportados](#controles-suportados)
- [Movimento](#movimento)
- [Conectar por Bluetooth](#conectar-por-bluetooth)
- [Requisitos](#requisitos)
- [Instalação](#instalação)
- [Conflitos com outros aplicativos](#conflitos-com-outros-aplicativos)
- [Diagnóstico](#diagnóstico)
- [Créditos](#créditos)
- [Licença](#licença)

## Controles suportados

| Controle | Conexão | O que funciona |
|---|---|---|
| Switch 2 Pro Controller | USB e Bluetooth | Todos os botões, os dois sticks com a calibração do próprio controle, giroscópio e acelerômetro |

O Switch 2 Pro Controller é um `Switch2ProController`, que é um `Gamepad`. Os botões são mapeados por
posição, como o Unity mapeia os outros controles Nintendo:

| Controle do `Gamepad` | Botão do Switch 2 |
|---|---|
| `buttonSouth` / `buttonEast` / `buttonNorth` / `buttonWest` | B / A / X / Y |
| `leftShoulder` / `rightShoulder` | L / R |
| `leftTrigger` / `rightTrigger` | ZL / ZR (digitais: 0 ou 1) |
| `select` / `start` | − / + |

Home, Capture, C (GameChat) e os botões traseiros GL e GR não têm equivalente em `Gamepad`; são
`homeButton`, `captureButton`, `chatButton`, `leftGripButton` e `rightGripButton`, usados nos
bindings como `<Switch2ProController>/home` e assim por diante.

Com vários Switch 2 Pro Controllers conectados, todos funcionam, mas os sticks usam uma faixa fixa em
vez da calibração de cada controle.

## Movimento

O drift do giroscópio é calibrado sozinho enquanto o controle fica parado ou se move sem solavancos.
Tudo vem nos eixos do Unity: com o controle deitado e a face para cima, x para a direita, y para cima
e z para longe do jogador.

| Membro do `Switch2ProController` | O que é |
|---|---|
| `gyro` | Velocidade angular em graus por segundo; aceita binding, como `<Switch2ProController>/gyro` |
| `accelerometer` | Aceleração em g; aceita binding |
| `orientation` | A orientação do controle, que pode ir direto para um `Transform`; o yaw é relativo e deriva devagar |
| `ConsumeRotation()` | Quanto o controle girou desde a última chamada, em graus; é o que usar para mirar |
| `gyroSpace` | O espaço de `ConsumeRotation()`: `Controller`, `World` ou `Player` |
| `gyroDeadZone` | Giros mais lentos que este valor, em graus por segundo, não contam em `ConsumeRotation()` |
| `gravity` e `linearAcceleration` | A gravidade e a aceleração sem ela, em g |

## Conectar por Bluetooth

Na primeira vez, segure o botão SYNC do controle até as luzes correrem: o pacote conecta e pareia o
controle com o PC. Daí em diante, basta apertar um botão qualquer para ele reconectar. O botão que
acorda o controle não chega ao jogo.

O pacote procura controles desde que o Editor abre, então dá para conectar fora do Play Mode, e
recompilar scripts não derruba a conexão. Plugar o cabo com o controle conectado por Bluetooth
passa a conexão para o USB em uns dois segundos; tirar o cabo desliga o controle, e um botão o
reconecta por Bluetooth.

O controle chega como o mesmo `Switch2ProController` do cabo; `isBluetooth` diz por qual conexão
ele veio.

## Requisitos

- Unity 6000.6 ou mais novo
- Input System 1.20.0
- Windows (Editor e builds standalone para Windows); para Bluetooth, Windows 64 bits e um
  adaptador Bluetooth LE

## Instalação

No Package Manager, escolha **Install package from git URL** e digite:

```
https://github.com/uayten/FofuxoJoySense4Unity.git
```

## Conflitos com outros aplicativos

O Switch 2 Pro Controller recebe os comandos de configuração por uma interface USB que só um processo
pode segurar por vez. O Steam pode ficar com ela enquanto está aberto, assim como qualquer outra
ferramenta de controle que suporte esse controle. Se isso acontecer, o pacote não consegue
configurar o controle e avisa no console; feche o outro aplicativo e plugue o controle de novo.

Por Bluetooth vale o mesmo: um aplicativo que esteja conectado ao controle impede o pacote de
conectar, e o console avisa.

## Diagnóstico

`Tools > Fofuxo JoySense > Stick Overlay` desenha os dois sticks do gamepad atual sobre a tela do jogo
no Play Mode: onde cada stick está, o valor que o jogo lê e quais direções contam como apertadas.
Serve para conferir, por exemplo, se para cima no stick lê para cima no jogo. Num controle com
sensores, mostra também a orientação e as leituras do movimento.

## Créditos

- Renan ([AgentOttsel](https://github.com/AgentOttsel)) e os colaboradores do
  [JoyShockLibrary4Unreal](https://github.com/AgentOttsel/JoyShockLibrary4Unreal), cujos protocolos de
  controle este pacote porta.
- Julian "Jibb" Smart, pelo [JoyShockLibrary](https://github.com/JibbSmart/JoyShockLibrary), sobre o
  qual o JoyShockLibrary4Unreal é construído.

## Licença

MIT; veja [LICENSE.md](LICENSE.md). Os avisos de terceiros estão em
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
