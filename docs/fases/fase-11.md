# Fase 11 — Atualizar pelo próprio app

**Status:** análise aprovada pelo usuário em 22/11/2026 (P1 a P4 como recomendado); em implementação
**Implementação:** Sonnet, em duas partes:
- 11a, o app: `medium`;
- 11b, a publicação: `low`.

**Análise e conferência:** Opus. **Documentação:** Haiku.

Repositório: `C:\Projetos\Mixer de Audio`, branch `main`, público em https://github.com/LucasAlexsander2/MixerDeAudio (criado em 22/11/2026). **Agentes não rodam git.**

## 1. Objetivo

O app passa a se atualizar sozinho, como o CursorSuave:
- confere no GitHub se há versão nova;
- avisa;
- baixa com um clique;
- reinicia já atualizado.

Cada versão nova sai de uma tag `vX.Y.Z` enviada ao GitHub, sem passo manual de instalador.

Motivo prático: o app instalado hoje (`%LOCALAPPDATA%\Programs\MixerDeAudio`, 1.0.0, gerado na Fase 7/8) ficou para trás. Ele não tem as Fases 9 e 10.

Fica fora: atualizar pelo celular e um canal beta.

## 2. Pendências do usuário

| # | Pendência | Recomendação |
|---|---|---|
| P1 | **Onde avisar da versão nova.** | Nos três lugares, como no CursorSuave:<br>• um balão do Windows uma vez por versão;<br>• uma faixa no topo da janelinha, "Versão X disponível · Atualizar";<br>• uma seção **Atualização** nas Configurações, com a versão atual, "Procurar agora" e o progresso. |
| P2 | **Novidades depois de atualizar.** | **Sim, simples.** Na primeira abertura depois de atualizar, as Configurações mostram uma vez o cartão "Novidades da versão X", com 2 a 5 itens. A lista fica em `Novidades.json`, e a publicação falha se faltar a entrada da versão. |
| P3 | **A versão instalada antiga (Inno Setup).** | **Migração automática.** Você instala uma vez o `Setup.exe` novo, baixado da página de Releases. Na primeira abertura, o app novo:<br>• fecha o antigo;<br>• desinstala o antigo em silêncio;<br>• refaz o "Iniciar com o Windows" para o caminho novo.<br>Configurações, celulares pareados e atalhos ficam onde estão (`%LOCALAPPDATA%\MixerDeAudio`). |
| P4 | **Número da primeira versão.** | **1.1.0**, com as Fases 9, 10 e 11. Para conferir que a atualização funciona, publica-se em seguida a **1.1.1**, só com a nota de novidades. |

## 3. O que foi levantado (22/11/2026)

**CursorSuave (`C:\Projetos\CursorSuave`)**
- Usa o **Velopack 1.2.161** com o `GithubSource`, sem token, no repositório público.
- `release.yml`: uma tag `v*` dispara testes, `dotnet publish -p:Version`, `vpk download` (para o delta), a conferência do `Novidades.json`, `vpk pack` e `vpk upload github`.
- `Atualizador.cs` envolve o Velopack atrás da interface `IFonteAtualizacao`, para dar para testar.
- `ControleAtualizacao.cs` é a máquina de estados: verificar, nova versão, baixando, pronto, reiniciar ou aplicar ao sair. Também tem a regra "a cada 6 h".
- Os hooks ficam no começo do `Main`: `OnFirstRun`, `OnAfterUpdateFastCallback` e `OnBeforeUninstallFastCallback`.
- **O que reaproveitar:** a estrutura desses arquivos, adaptada. O Mixer usa MVVM (`SettingsViewModel`), não code-behind.

**Mixer de Áudio**
- Publicação self-contained, com trimming e ReadyToRun, e `WindowsAppSDKSelfContained`.
- O `Main` é próprio (`DISABLE_XAML_GENERATED_MAIN`), com instância única por `AppInstance.FindOrRegisterForKey`.
- Instalador Inno em `instalador\MixerDeAudio.iss`, com `{autopf}\MixerDeAudio` e `PrivilegesRequired=lowest`. Ele é gerado por `tools\release\gerar.ps1`.
- A versão vem do `<Version>` do `.csproj`, hoje 1.0.0.
- Os dados ficam em `%LOCALAPPDATA%\MixerDeAudio\` (`AppPaths`): `config.json`, `logs`, `Atalhos`.
- "Iniciar com o Windows" usa o valor `MixerDeAudio` em `HKCU\...\Run` (`StartupRegistration`, `StartupRules`).
- Os roteiros de teste rodam o `publicado\MixerDeAudio.exe`, que não é uma instalação.
- **Risco:** o Velopack instala em `%LOCALAPPDATA%\<packId>\` e, ao desinstalar, **apaga essa pasta inteira**. Com o `packId` `MixerDeAudio`, a desinstalação apagaria o `config.json`. Ver a D2.

## 4. Decisões

| # | Decisão |
|---|---|
| D1 | **Velopack 1.2.161 e GitHub Releases** do repositório público, com `GithubSource(repo, null, false)`, sem token. Nenhum outro pacote. |
| D2 | **`packId` = `MixerDeAudioApp`**, então o app fica em `%LOCALAPPDATA%\MixerDeAudioApp\current\MixerDeAudio.exe`. Os dados continuam em `%LOCALAPPDATA%\MixerDeAudio\`, fora da pasta que o Velopack apaga. |
| D3 | **Hooks na primeira linha do `Main`**, antes do `AppInstance`: `VelopackApp.Build()...Run()`. Os hooks rodam rápido e sem interface:<br>• `OnFirstRun`: migração do Inno (D9) e caminho do "Iniciar com o Windows";<br>• `OnAfterUpdateFastCallback`: caminho do "Iniciar com o Windows";<br>• `OnBeforeUninstallFastCallback`: tira o valor do `Run`. |
| D4 | **`Services\Updater.cs`** envolve o Velopack atrás de `IUpdateSource` (instalado, versão atual, versão nova, verificar, baixar com progresso, aplicar e reiniciar, aplicar ao sair).<br>**`Services\UpdateController.cs`** guarda os estados `Indisponivel`, `Parado`, `Verificando`, `NovaVersao`, `Baixando` (%), `Pronto` e `Erro`, e avisa o ViewModel na thread da tela.<br>**`Services\UpdateRules.cs`** reúne a lógica pura: a regra de 6 h, o balão uma vez por versão e o resumo do erro. |
| D5 | **Quando conferir:** 60 s depois de abrir e, depois, a cada 6 h, se a opção **"Procurar atualizações automaticamente"** estiver ligada. É uma configuração nova, `AtualizarAuto`, ligada por padrão.<br>A conferência só procura: **nunca baixa nem instala sem clique**. O botão "Procurar agora" funciona mesmo com a opção desligada. |
| D6 | **Interface (P1):**<br>• seção **Atualização** nas Configurações: "Versão 1.1.0", o estado, os botões "Procurar agora", "Atualizar" (baixa com % e tempo restante) e "Reiniciar para atualizar", e o interruptor da D5;<br>• faixa no topo da janelinha quando há versão nova ou baixada;<br>• balão da bandeja (`H.NotifyIcon`) uma vez por versão, que o clique abre na seção Atualização. |
| D7 | **Aplicar:**<br>• "Reiniciar" fecha direito e chama `ApplyUpdatesAndRestart`. O servidor do celular solta a porta e a espera da 10b a retoma.<br>• "Sair" com uma versão já baixada aplica ao fechar (`WaitExitThenApplyUpdates`). |
| D8 | **Novidades (P2):** `MixerDeAudio\Novidades.json` vai embutido no app.<br>`AppSettings.UltimaVersaoVista` guarda a última versão vista. Se a versão atual for maior que ela, as Configurações mostram o cartão uma vez e gravam a versão.<br>Instalação nova não mostra o cartão. |
| D9 | **Migração do Inno (P3), no `OnFirstRun`.** Se existir `%LOCALAPPDATA%\Programs\MixerDeAudio\unins000.exe`:<br>1. manda `--sair` ao `MixerDeAudio.exe` antigo e espera até 5 s;<br>2. roda `unins000.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART` e espera até 30 s;<br>3. **depois**, regrava o `Run` para o caminho novo, se ele estava ligado antes. O desinstalador antigo apaga o valor `MixerDeAudio`, então o estado é lido antes do passo 1.<br><br>Falha só vai para o log e o app abre do mesmo jeito. A regra "há instalação antiga e o que fazer" fica pura e testável. |
| D10 | **Cópia não instalada** (`publicado\`, `bin\`): a seção mostra "Atualização indisponível nesta cópia (não instalada)" e nada acessa a rede. Com `--teste-oculto`, a conferência automática nunca roda. Os roteiros continuam como estão. |
| D11 | **Publicação (11b):** `.github\workflows\release.yml` no molde do CursorSuave, com:<br>• `dotnet test` da bateria;<br>• `dotnet publish MixerDeAudio\MixerDeAudio.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained -p:Version=$VERSAO`;<br>• a conferência do `Novidades.json`;<br>• `vpk pack -u MixerDeAudioApp -e MixerDeAudio.exe --packTitle "Mixer de Áudio" -i ícone`;<br>• `vpk upload github`.<br>O `README.md` ganha "Instalar" e "Publicar uma versão". |
| D12 | **O Inno sai:** `instalador\` e `tools\release\gerar.ps1` são removidos. O `docs\ESTADO.md` passa a mandar publicar por tag. O `<Version>` do `.csproj` fica só como versão das cópias locais e vai para 1.1.0. |
| D13 | **Trim e avisos:** a publicação continua sem avisos novos, além dos dois `IL2104` antigos. Se o Velopack gerar avisos de trim, a correção é `TrimmerRootAssembly` só para ele, registrada no documento. |

## 5. Arquivos

- **11a, o app:**
  - novos:
    - `Services\Updater.cs`, `Services\UpdateController.cs`, `Services\UpdateRules.cs`;
    - `Services\InnoMigration.cs`, com a regra pura e a execução;
    - `Novidades.json`;
  - alterados:
    - `Program.cs` (hooks);
    - `AppSettings.cs` (`AtualizarAuto`, `UltimaVersaoVista`, `BalaoVersao`);
    - `StartupRegistration.cs` (regravar o caminho);
    - `SettingsViewModel.cs` e `Flyout\SettingsView.xaml` (seção e cartão);
    - a janelinha (faixa) e a bandeja (balão);
    - `MixerDeAudio.csproj` (Velopack, `Version` 1.1.0, `Novidades.json` como recurso);
  - testes: `UpdateRulesTests.cs`, `UpdateControllerTests.cs` (com uma fonte falsa) e `InnoMigrationTests.cs`.
- **11b, a publicação:**
  - `.github\workflows\release.yml`;
  - `README.md`;
  - remover `instalador\` e `tools\release\`;
  - `docs\ESTADO.md` e `docs\PLANO.md`.

## 6. Testes

### 6.1 Unitários

- Regra das 6 h: nunca verificou, menos de 6 h, 6 h ou mais, opção desligada.
- Estados: verificar sem novidade, nova versão, baixar com progresso, pronto, reiniciar, erro na verificação, erro ao baixar e cópia não instalada.
- Balão uma vez por versão.
- Novidades:
  - lê o JSON;
  - escolhe a versão;
  - mostra uma vez;
  - não mostra em instalação nova.
- Migração: há instalação antiga ou não, o estado do "Iniciar com o Windows" é lido antes e o caminho novo do `Run` está certo.
- Comparar versões (1.1.0 < 1.1.1 < 1.10.0).
- A bateria inteira passa.

### 6.2 Roteiro (`roteiro-f11.ps1`)

Mesmas regras de sempre:
- `--teste-oculto`;
- `BackupConfig` e `RestoreConfig`;
- nunca matar o roteiro no meio;
- reabrir o app do usuário.

| Caso | Por que no roteiro | Resultado esperado |
|---|---|---|
| 11.1 Cópia não instalada | A seção nova depende da janela. | Nas Configurações (UIA), a seção Atualização mostra a versão 1.1.0 e "indisponível nesta cópia". Nenhuma conexão do app com `github.com` em 90 s (`Get-NetTCPConnection`). |
| 11.2 Sem regressão | A partida tem os hooks novos. | O app abre e fecha normalmente, e o log não tem `ERRO`. |

### 6.3 Ponta a ponta (Opus, com o usuário)

1. O Opus confere o workflow, cria a tag `v1.1.0` (com autorização) e acompanha o GitHub Actions até a release sair com o `Setup.exe`.
2. **Usuário:** baixa e roda o `MixerDeAudio-win-Setup.exe` da página de Releases. Confere:
   - o app antigo foi fechado e desinstalado e some de "Apps instalados";
   - o "Iniciar com o Windows" continua como estava;
   - as configurações e os celulares pareados continuam.
3. O Opus publica a `v1.1.1`, só com a nota de novidades.
4. **Usuário:** o balão aparece (ou "Procurar agora"). Ele toca em "Atualizar" e vê a porcentagem, depois toca em "Reiniciar" e o app volta na 1.1.1 com o cartão de Novidades.

## 7. Resultados

### Parte 11a (o app) — Sonnet, 22/11/2026

**Arquivos**
- Novos (`MixerDeAudio\`): `Services\Updater.cs`, `Services\UpdateController.cs` (também traz `IUpdateSource` e os estados), `Services\UpdateRules.cs` (6 h, balão, versões, novidades, textos, faixa, estimativa de tempo), `Services\InnoMigration.cs` (regra pura) e `Services\InnoMigration.Run.cs` (execução), `Services\NotesResource.cs`, `Novidades.json` (entrada 1.1.0, 4 itens; recurso embutido).
- Alterados: `Program.cs` (hooks do Velopack na primeira linha do `Main`), `AppSettings.cs` (`AtualizarAuto`, `UltimaVersaoVista`, `BalaoVersao`), `SettingsStore.cs` (`ExistedOnLoad`), `StartupRules.cs` e `StartupRegistration.cs` (`WasEnabledBefore`, `RepointToCurrent`, `RemoveIfCurrent`), `App.xaml.cs` (controle, timer de 60 s e 6 h, balão, `Shutdown`), `SettingsViewModel.cs`, `SettingsView.xaml(.cs)` (cartão e seção), `FlyoutWindow.xaml(.cs)` (faixa), `MixerDeAudio.csproj` (Velopack 1.2.161, `Version` 1.1.0, `Novidades.json`).
- Testes: `UpdateRulesTests.cs`, `UpdateControllerTests.cs` (fonte falsa), `InnoMigrationTests.cs`; `MixerDeAudio.Tests.csproj` linka os arquivos novos e copia o `Novidades.json`.
- Roteiro: `tools\testes\roteiro-f11.ps1`.

**Build:** Debug sem erros e sem avisos. Publish Release: só os dois `IL2104` antigos; o Velopack não gerou avisos de trim, então a D13 (`TrimmerRootAssembly`) não foi necessária.

**Testes unitários:** 865 aprovados, 0 falhas, 865 no total.

**Roteiro `roteiro-f11.ps1` (99,5 s):**
- 11.1 OK: "Versão 1.1.0" e "Atualização indisponível nesta cópia (não instalada)."; nenhuma conexão externa do app em 90 s; botões Atualizar e Reiniciar escondidos.
- 11.2 OK: a janela fecha, o processo sai, o log tem `iniciado` e `saindo` e nenhuma linha `ERRO`.
- Final: `config.json` e `Run` iguais aos de antes.

**Desvios e observações**
- O clique no balão não abre as Configurações: o H.NotifyIcon 2.4.1 não expõe o clique no balão de forma simples. O balão diz "Clique no ícone para atualizar" e a faixa da janelinha leva ao mesmo fluxo.
- Na primeira execução do roteiro, o `StartApp` e o `Cmd '--abrir-sem-foco'` disputaram a instância única (a conferência de `iniciado` contava linhas antigas do log) e uma janela apareceu fora do modo oculto. Corrigido no roteiro: ele espera uma linha `iniciado` nova. O `config.json` foi restaurado igual.
- Instalação nova (sem `config.json`) grava `UltimaVersaoVista` na partida para o cartão não aparecer (D8); quem vem do Inno (config existe, nada visto) vê o cartão uma vez.
- Nada da migração foi executado de verdade: só a regra pura é testada, com caminhos falsos.
- Ao fim do roteiro, o app instalado do usuário estava sendo reinstalado por um `MixerDeAudio-1.0.0-x64-setup.exe` (de `dist`) iniciado fora desta sessão; o app foi fechado só com `--sair` antes do publish e não foi reaberto por mim (o instalador o recoloca).

### Conferência (Opus, 22/11/2026)

- **Build e testes:** 0 avisos e 0 erros; 865 testes aprovados. O publish Release tem só os dois `IL2104` antigos, e o Velopack não trouxe avisos de trim.
- **Hooks:** `VelopackApp.Build()...Run()` é a primeira linha do `Main`, antes do `AppInstance`.
- **Migração:**
  - Ordem certa: lê o "Iniciar com o Windows", manda `--sair` ao exe antigo, roda o desinstalador e regrava o `Run`.
  - Não há corrida com o desinstalador do Inno. O `RemoveStartupEntries` dele só apaga o `Run` se o valor apontar para a pasta antiga, então o caminho novo nunca é apagado, em qualquer ordem.
  - O desinstalador silencioso nunca apaga `%LOCALAPPDATA%\MixerDeAudio` (só com `/APAGARDADOS`).
  - O exe antigo (1.0.0), aberto sozinho com `--sair`, executa o comando e fecha, sem prender a instância única.
- **Workflow:** a conferência do `Novidades.json` foi movida para antes dos testes e da publicação, para falhar logo.
- **Pendente:** o hook instalado de verdade e o Velopack trimado só são exercitados na ponta a ponta (6.3).
