# Handoff

Atualizado: 2026-11-01

## Estado
O Switch 2 Pro Controller funciona por USB e por Bluetooth. Giroscópio e acelerômetro foram confirmados pelo usuário nas duas conexões (F-012). O gyro é lido só pela action `*/{GyroLook}`, sem dead zone, e gira pelo tempo (D-010, D-012); o usuário aprovou em Play pelo cabo e pelo Bluetooth, e o R-011 saiu do roadmap. A luz de player 1 acende também pelo cabo (R-012, dois testes de troca de conexão em aberto). O R-010 tem dois testes em aberto.

O Bluetooth não inicia mais em batch mode (D-013), e o usuário conectou o controle pelo Bluetooth depois disso, com uma única conexão registrada. O `bluetooth.log` grava cada conexão que transmite e cada conexão muda; a linha `silent` só foi conferida com uma entrada forçada, porque o erro real não voltou.

O código vive no MyDragonShop (D-004). Existe um clone em `C:\UnityProjects\FofuxoJoySense4Unity`, com um commit local do estado do pacote, sem push: o `uayten/FofuxoJoySense4Unity` no GitHub ainda está vazio.

## Próximo passo
Com o usuário, os testes em aberto do R-012 (Bluetooth para o cabo e de volta, com a luz mantida) e do R-010. Depois, o primeiro item de `A fazer` (R-005, vibração), ou entender por que o Bluetooth passou de uns 70 para uns 225 a 240 relatórios por segundo (F-012 está desatualizado nisso).

## Perguntas em aberto
- Publicar o commit local no GitHub é decisão do usuário: o repositório é público.
- O Unity CLI foi para a 1.0.0-beta.15 e pede ao Editor comandos que o `com.unity.pipeline` instalado não tem (`editor status`); atualizar o pacote é decisão do usuário.
- Uma conexão Bluetooth pode ficar viva e muda (F-013, R-013). Não está provado que o D-013 resolveu o silêncio, e o usuário vê travamentos parecidos fora do Unity. Se voltar a acontecer, ver `JoySense/bluetooth.log` em `Application.persistentDataPath`.
- Parear com o PC pode tirar o pareamento do controle com o console Switch 2; não verificado.
- A tela de remapeamento do jogo sempre abre na aba de controle com o Switch 2, porque ele atualiza o `lastUpdateTime` centenas de vezes por segundo mesmo parado (`GameInput.LastUsedDeviceIsGamepad`, código do jogo).

## Notas para a próxima sessão
- As correções de Bluetooth achadas aqui foram anotadas para o JSL4U, no `ROADMAP.md` dele. As de 2026-11-01 (link mudo e o Windows que não solta, taxa de relatórios e giro liso, dead zone do gyro em portão, luz de jogador no cabo) são o R-036 a R-039 de lá; o portão acabou saindo daqui.
- A volta completa na mesa, o `Gamepad.current` e a tela de remapeamento não foram confirmados um a um com o controle real, embora o R-004 tenha saído do roadmap com a aprovação geral do usuário.

## Ler primeiro
O `CLAUDE.md` deste pacote, e D-009, D-010, D-012 e F-012 a F-014 do `DECISIONS.md`. Para o movimento, `Runtime/Switch2/Switch2ProController.cs` e `Runtime/Controls/MotionLookControl.cs`; não vale ler `Runtime/Motion/GamepadMotion.cs`, que é porte fiel. No JoyShockLibrary4Unreal (`C:\Unreal Projects\BossRush\Plugins\JoyShockLibrary4Unreal`), pular `Intermediate/` e `Binaries/`.
