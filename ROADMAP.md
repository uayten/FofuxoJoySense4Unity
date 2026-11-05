# Roadmap

<!-- project-docs: só pendências; aprovado é apagado. Ordem = prioridade. Próximo ID: R-014 -->

## Em teste

### R-012 [ajuste] Luz de player 1 também pelo cabo
Ajusta: Runtime/Switch2/Switch2CommandInterface.cs
Esperado: a luz de player 1 acende pelo cabo como pelo Bluetooth, igual ao console, venha o controle desligado ou do Bluetooth.
Testes (Steam fechado):
- [x] Plugar o cabo com o controle desligado acende a luz de player 1
- [ ] Plugar o cabo com o controle no Bluetooth mantém a luz de player 1, e os botões e o gyro continuam certos
- [ ] Tirar o cabo e reconectar por botão no Bluetooth mantém a luz de player 1

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

### R-013 [pesquisa] Conexão Bluetooth que fica muda
Esperado: saber por que o controle às vezes conecta e não manda entrada, comparando as linhas `silent` e `streaming` do `bluetooth.log` quando o erro voltar. Conta como resposta uma causa reproduzível ou a confirmação de que vem do driver ou do hardware.
Em aberto: se o erro não voltar depois do D-013, o item pode sair.

### R-009 [ajuste] Atualizar README.md (inglês)
Ajusta: README.md
Esperado: README.md com o mesmo conteúdo do README.pt-BR.md atual, em inglês, sem o aviso de "em desenvolvimento".
