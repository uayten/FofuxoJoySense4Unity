# Fofuxo's JoySense4Unity — instruções do repositório

Pacote Unity público, id `com.uayten.fofuxojoysense`, namespace `Fofuxo.JoySense`, assembly
`Uayten.FofuxoJoySense`, Unity `6000.6` ou mais novo. Repositório:
<https://github.com/uayten/FofuxoJoySense4Unity>.

Este projeto segue a convenção project-docs (ROADMAP.md, DECISIONS.md, HANDOFF.md).
Quando o usuário pedir para retomar ("retoma", "vamos continuar o projeto", "vamos continuar o desenvolvimento do roadmap"), antes de agir: ler HANDOFF.md; do ROADMAP.md, só os títulos (`grep "^### R-" ROADMAP.md`) e o item do próximo passo ou do pedido. Confirmar em 3 linhas o estado e o próximo passo e esperar o ok.

## Escopo

Suporte nativo a controles Sony e Nintendo pelo Input System. O pacote é genérico: nada específico
de um jogo entra nele. Toda mudança visível ganha uma entrada em `[Unreleased]` no `CHANGELOG.md`.

## Idioma

- Em inglês: código (identificadores, comentários, logs) e `THIRD-PARTY-NOTICES.md`.
- Em português, porque o usuário lê: este `CLAUDE.md`, `CHANGELOG.md`, `ROADMAP.md`, `DECISIONS.md`
  e `HANDOFF.md` (D-006).
- O README principal é o `README.pt-BR.md`. O `README.md` em inglês está desatualizado de
  propósito e só aponta para ele; quando o principal mudar, não traduzir, e sim garantir o item de
  tradução no fim do `A fazer` do ROADMAP (R-009), sem duplicar.

## Arquitetura

- **C#, não plugin nativo.** As APIs do Windows (SetupAPI, WinUSB) são chamadas por P/Invoke em
  DLLs do sistema. DLL nativa só para um transporte que o C# no Unity não alcança: o Bluetooth LE do
  Switch 2 precisa do GATT do WinRT, que o Mono não tem (D-002).
- **A entrada chega pelo backend HID do próprio Unity.** Cada dispositivo é um layout registrado
  para o vendor/product id dele, cuja struct de estado *é* o relatório HID cru, como no suporte a
  DualShock do Unity. Assim o pacote nunca reescreve nem intercepta eventos, e ruído, `.current`,
  remapeamento e `PlayerInput` se comportam como em qualquer dispositivo nativo (F-004). O que o
  relatório cru não expressa vira tipo de controle: `CalibratedStickAxis` e `CalibratedStickButton`
  leem as contagens de 12 bits dos sticks pela calibração de cada controle, definida no controle em
  tempo de execução (F-005). Exceção: por Bluetooth o Windows não cria dispositivo HID, e o pacote
  adiciona ele mesmo um dispositivo do mesmo layout, alimentado pelos relatórios que a DLL nativa
  recebe (D-007).
- **A DLL nativa `JoySenseBle`** tem o código em `Native~/JoySenseBle` e é compilada por `build.cmd`
  para `Runtime/Plugins/x86_64`. Depois de carregada, o Editor a mantém presa: para recompilá-la é
  preciso fechar o Unity. Ela nunca chama o C#; o C# consulta.
- **Os dispositivos herdam de `Gamepad`** e seguem a convenção posicional do Unity: `buttonSouth` é
  o botão de baixo, qualquer que seja a letra (B na Nintendo), como no `SwitchProControllerHID` do
  Unity. Botões sem equivalente em `Gamepad` (Home, Capture, C, GL, GR) são controles extras (D-003).
- **I/O bloqueante nunca roda na thread principal.** Os comandos WinUSB e as leituras de SPI levam
  centenas de milissegundos no total e vão para uma thread de trabalho; até terminarem, o
  dispositivo lê sem calibração.
- O código só de Windows fica atrás de `UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN`, para o pacote
  compilar em qualquer plataforma.

## Portando do JoyShockLibrary4Unreal

Os protocolos vêm do JoyShockLibrary4Unreal (JSL4U). A camada de hardware dele fica em
`Source/JoyShockLibrary4Unreal/JoyShockLibrary/` daquele plugin. Os fatos de hardware escritos nos
comentários dele (offsets de bytes, sinais de eixos, por que um valor é o que é) vêm junto com o
código, porque foram medidos no hardware e não dá para recuperá-los só pelo código. Quando uma
correção entrar no JSL4U, é aqui que ela vai:

| JSL4U | Aqui |
|---|---|
| `JoyShock_Switch2.cpp` (`sw2_open_winusb`, `init_switch2`) | `Runtime/Switch2/Switch2CommandInterface.cs` |
| `InputHelpers.cpp` (`parse_switch2_report`) | `Switch2ProControllerHIDInputState` e `ProcessMotion` em `Runtime/Switch2/Switch2ProController.cs` |
| `GamepadMotion.hpp` | `Runtime/Motion/GamepadMotion.cs` |
| `JoyShock_Nintendo.cpp` (`CalcAnalogStick2`) | `AxisCalibration.Apply` em `Runtime/Controls/CalibratedStickAxis.cs` |
| `Switch2Bluetooth.cpp` | `Native~/JoySenseBle/JoySenseBle.cpp` |
| `JoyShock_Switch2.cpp` (`init_switch2_bluetooth`, `read_sw2_ble_memory`), `JoyShockEnumeration.cpp` (`discover_bluetooth_switch2`), `InputHelpers.cpp` (`settle_input`) | `Runtime/Switch2/Switch2Bluetooth.cs` |

## Testes no hardware

Fechar o Steam antes: ele pode prender a interface WinUSB do Switch 2, e o controle não pode ser
configurado enquanto isso. Um controle deixado inicializado por uma sessão anterior continua
transmitindo, então testar também a partir de um controle recém-plugado.

- Mudança na DLL nativa: compilar uma cópia fora do pacote e testá-la num processo separado
  (PowerShell com `Add-Type` e P/Invoke), com o scan do Editor pausado por reflexão
  (`Switch2Bluetooth.s_Stopping`, `s_StopSignal` e `JsBle_StopScan`). Só depois fechar o Unity
  (salvar cenas e assets, `EditorApplication.Exit(0)` direto no `eval`; o `delayCall` pode não
  rodar com o Editor sem foco), rodar o `build.cmd` e reabrir com `unity open`.
- Relatórios crus no Editor: um `InputSystem.onEvent` instalado pelo `unity cmd eval_file`, lendo
  com `StateEvent.GetState<Switch2ProControllerHIDInputState>` (o `eval` não aceita `unsafe` nem
  `using`).
