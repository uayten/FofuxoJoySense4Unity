# Roadmap

<!-- project-docs: só pendências; aprovado é apagado. Ordem = prioridade. Próximo ID: R-012 -->

## Em teste

### R-011 [feature] Dead zone por função e gyro nas Input Actions
Ajusta: Runtime/JoySenseSupport.cs, Runtime/Controls/JoySenseProcessors.cs, Runtime/Controls/MotionLookControl.cs, Runtime/Motion/IMotionController.cs, Runtime/Switch2/Switch2ProController.cs; ao aprovar, README.pt-BR.md > Movimento
Esperado: cada binding de stick tem a própria dead zone, sem o filtro global antes dela, e o gyro pode ser ligado a uma action como `<Switch2ProController>/gyroLook`, com dead zone e escala por processors (D-010).
Testes (Steam fechado, no MyDragonShop; o controle e os processors já foram conferidos com relatórios sintéticos, a action em Play e o hardware não):
- [ ] Em Play, girar o controle move a câmera pela action `Jogador/OlharGiroscopio`, pelo cabo e por Bluetooth
- [ ] Virar o controle 90° gira a câmera uns 90° com intensidade em 100%
- [ ] Com o controle parado, a câmera não anda; depois de uma parada, não sobra movimento
- [ ] Mudar `min` do processor em `Jogador/Mover` e em `Jogador/Olhar` muda cada um sem afetar o outro
- [ ] A navegação dos menus com os sticks continua igual, sem andar sozinha

### R-010 [ajuste] Bluetooth só para controles em pareamento ou deste PC; nomes dos cliques dos sticks
Ajusta: Runtime/Switch2/Switch2Bluetooth.cs, Native~/JoySenseBle (JsBle_SuppressFor), Runtime/Switch2/Switch2ProController.cs; ao aprovar, README.pt-BR.md > Conectar por Bluetooth (um controle pareado com outro aparelho é ignorado até segurarem SYNC nele)
Esperado: por Bluetooth, o pacote só conecta um controle em modo SYNC ou já pareado com este PC; um pareado com outro aparelho é ignorado até segurarem SYNC nele (D-008). Na tela de remapeamento, os cliques dos sticks aparecem como "Left Stick Press" e "Right Stick Press".
Testes:
- [x] Controle já pareado com o PC: um botão o reconecta, como antes
- [ ] Na aba de controle da tela de remapeamento, os cliques dos sticks aparecem como "Left Stick Press" e "Right Stick Press"
- [ ] (Opcional, se o controle for pareado de volta com o console) Pareado com o console, um botão não o conecta ao PC e o console mostra "is paired with another device"; segurar SYNC o conecta e pareia com o PC

## Em andamento

## A fazer

### R-005 [feature] Vibração
Esperado: os controles vibram pela API de vibração do Input System.

### R-006 [feature] Joy-Cons
Esperado: os Joy-Cons funcionam como controles, como no JoyShockLibrary4Unreal.

### R-007 [feature] DualShock 4
Esperado: um DualShock 4 funciona por este pacote, como no JoyShockLibrary4Unreal.

### R-008 [feature] Calibração própria de cada Switch 2 Pro Controller com vários conectados
Esperado: com dois ou mais Switch 2 Pro Controllers plugados, os sticks de cada um usam a calibração dele em vez da faixa fixa.
Notas: o Unity não informa o caminho do dispositivo e dá o mesmo serial para todas as unidades (F-003), então o dispositivo do Input System precisa ser casado com a interface WinUSB dele de outro jeito. Por exemplo: ler um relatório de cada caminho HID e ver qual dispositivo recebeu os mesmos bytes.

### R-009 [ajuste] Atualizar README.md (inglês)
Ajusta: README.md
Esperado: README.md com o mesmo conteúdo do README.pt-BR.md atual, em inglês, sem o aviso de "em desenvolvimento".
