# Handoff

Atualizado: 2026-11-01

## Estado
O Switch 2 Pro Controller funciona por USB e por Bluetooth. Giroscópio e acelerômetro foram confirmados pelo usuário nas duas conexões (F-012). O R-011 (dead zone por binding e `gyroLook` para Input Actions, D-010) compila e passou em relatórios sintéticos, mas a action em Play e o hardware não foram testados. O R-010 tem dois testes em aberto.

O código vive no MyDragonShop (D-004). Existe um clone em `C:\UnityProjects\FofuxoJoySense4Unity`, com um commit local do estado do pacote, sem push: o `uayten/FofuxoJoySense4Unity` no GitHub ainda está vazio.

## Próximo passo
Com o usuário, testar o R-011 em Play no MyDragonShop, começando por girar o controle e ver a câmera mover. Aprovado, apagar o item e pôr `gyroLook` e os processors no README, seção Movimento. Se a action não receber o valor, o gyro segue por `ConsumeRotation()`.

## Perguntas em aberto
- Publicar o commit local no GitHub é decisão do usuário: o repositório é público.
- Uma conexão Bluetooth pode ficar viva e muda (F-013). A causa é desconhecida, e a recuperação nova nunca rodou de verdade.
- Parear com o PC pode tirar o pareamento do controle com o console Switch 2; não verificado.
- A tela de remapeamento do jogo sempre abre na aba de controle com o Switch 2, porque ele atualiza o `lastUpdateTime` centenas de vezes por segundo mesmo parado (`GameInput.LastUsedDeviceIsGamepad`, código do jogo).

## Notas para a próxima sessão
- As correções de Bluetooth achadas aqui foram anotadas para o JSL4U, no `ROADMAP.md` dele. O F-013 e a recuperação do silêncio ainda não foram.
- A volta completa na mesa, o `Gamepad.current` e a tela de remapeamento não foram confirmados um a um com o controle real, embora o R-004 tenha saído do roadmap com a aprovação geral do usuário.

## Ler primeiro
O `CLAUDE.md` deste pacote, e D-009, D-010 e F-012 a F-014 do `DECISIONS.md`. Para o movimento, `Runtime/Switch2/Switch2ProController.cs` e `Runtime/Controls/MotionLookControl.cs`; não vale ler `Runtime/Motion/GamepadMotion.cs`, que é porte fiel. No JoyShockLibrary4Unreal (`C:\Unreal Projects\BossRush\Plugins\JoyShockLibrary4Unreal`), pular `Intermediate/` e `Binaries/`.
