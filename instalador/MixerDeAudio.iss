; Instalador do Mixer de Áudio (Inno Setup 6). Gerado pelo script tools\release\gerar.ps1, que passa:
;   /DAppVersion=1.0.0   versão (vem do <Version> do MixerDeAudio.csproj, a única fonte)
;   /DFonte=<pasta>      pasta do publish (MixerDeAudio.exe e o resto)
;   /DSaida=<pasta>      pasta onde o setup é gravado
; Decisões D13 a D19 em docs\fases\fase-8.md. Nenhuma regra de firewall é criada (P1).

#ifndef AppVersion
  #error Passe /DAppVersion=x.y.z (use tools\release\gerar.ps1)
#endif
#ifndef Fonte
  #error Passe /DFonte=<pasta do publish>
#endif
#ifndef Saida
  #define Saida "..\dist"
#endif

#define AppIdGuid "{9D3F7A52-6B1E-4C80-A7D4-2E5B8C1F0A63}"

[Setup]
; AppId fixo para sempre: é ele que faz a atualização cair por cima. Nunca mudar.
AppId={{9D3F7A52-6B1E-4C80-A7D4-2E5B8C1F0A63}
AppName=MixerDeAudio
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisher=Lucas Alexsander
UninstallDisplayName=Mixer de Áudio
UninstallDisplayIcon={app}\MixerDeAudio.exe

; Por usuário, sem administrador, pasta fixa. A pasta fixa é o que torna seguro o [InstallDelete] e o
; [UninstallDelete] de {app} inteira: nunca remover DisableDirPage.
PrivilegesRequired=lowest
DefaultDirName={autopf}\MixerDeAudio
DisableDirPage=yes
UsePreviousAppDir=no
DisableProgramGroupPage=yes
DefaultGroupName=MixerDeAudio

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

WizardStyle=modern
SetupIconFile=..\MixerDeAudio\Assets\MixerDeAudio.ico
MinVersion=10.0.22000
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir={#Saida}
OutputBaseFilename=MixerDeAudio-{#AppVersion}-x64-setup

; App aberto ao instalar ou desinstalar (D16): o --sair roda no [Code]; isto é a reserva.
CloseApplications=force
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Iniciar com o Windows"; GroupDescription: "Inicialização:"

[Files]
Source: "{#Fonte}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Mixer de Áudio"; Filename: "{app}\MixerDeAudio.exe"
Name: "{autodesktop}\Mixer de Áudio"; Filename: "{app}\MixerDeAudio.exe"; Tasks: desktopicon

[Registry]
; "Iniciar com o Windows" como o SetEnabled do app: grava o caminho entre aspas. Sem a tarefa, o registro não é tocado.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MixerDeAudio"; ValueData: """{app}\MixerDeAudio.exe"""; Tasks: startup
; Tira o "desligado" do Gerenciador de Tarefas, se houver.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"; ValueName: "MixerDeAudio"; Flags: deletevalue; Tasks: startup

[InstallDelete]
; Atualizar por cima: apaga os arquivos da versão anterior antes de copiar. Só {app} (pasta fixa);
; os dados em {localappdata}\MixerDeAudio nunca entram aqui.
Type: filesandordirs; Name: "{app}\*"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Run]
Filename: "{app}\MixerDeAudio.exe"; Description: "Abrir o Mixer de Áudio"; Flags: postinstall nowait skipifsilent

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppIdGuid}_is1';
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  ApprovedKey = 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run';

// Compara versões "a.b.c" numericamente: -1, 0 ou 1. Partes que faltam valem 0.
function CompareVersions(A, B: String): Integer;
var
  VA, VB, P: Integer;
  SA, SB: String;
begin
  Result := 0;
  SA := A;
  SB := B;
  while (Result = 0) and ((SA <> '') or (SB <> '')) do
  begin
    P := Pos('.', SA);
    if P > 0 then begin VA := StrToIntDef(Copy(SA, 1, P - 1), 0); Delete(SA, 1, P); end
    else begin VA := StrToIntDef(SA, 0); SA := ''; end;
    P := Pos('.', SB);
    if P > 0 then begin VB := StrToIntDef(Copy(SB, 1, P - 1), 0); Delete(SB, 1, P); end
    else begin VB := StrToIntDef(SB, 0); SB := ''; end;
    if VA < VB then Result := -1
    else if VA > VB then Result := 1;
  end;
end;

// D16: fecha o app (qualquer cópia, por causa da instância única) e espera 1,5 s.
procedure CloseRunningApp(AppDir: String);
var
  Exe: String;
  Code: Integer;
begin
  Exe := AddBackslash(AppDir) + 'MixerDeAudio.exe';
  if FileExists(Exe) then
  begin
    Exec(Exe, '--sair', '', SW_HIDE, ewWaitUntilTerminated, Code);
    // O --sair redireciona para a cópia aberta e volta; dá 1,5 s para ela terminar de fechar.
    Sleep(1500);
  end;
end;

// D16 (conferência): fecha com --sair toda cópia aberta do MixerDeAudio.exe, de qualquer pasta (publicado\ ou instalada),
// porque cada caminho tem a sua própria instância única. Usa o WMI só para achar os caminhos; falha é ignorada.
procedure CloseAllRunning();
var
  Locator, Service, Procs, Proc, Value: Variant;
  I, Count, Code: Integer;
  Path: String;
begin
  Count := 0;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\cimv2');
    Procs := Service.ExecQuery('SELECT ExecutablePath FROM Win32_Process WHERE Name = ''MixerDeAudio.exe''');
    for I := 0 to Procs.Count - 1 do
    begin
      Proc := Procs.ItemIndex(I);
      Value := Proc.ExecutablePath;
      if not VarIsNull(Value) then
      begin
        Path := Value;
        if (Path <> '') and FileExists(Path) then
        begin
          Exec(Path, '--sair', '', SW_HIDE, ewWaitUntilTerminated, Code);
          Count := Count + 1;
        end;
      end;
    end;
  except
  end;
  if Count > 0 then Sleep(1500);
end;

// D13: recusa instalar uma versão mais antiga por cima de uma mais nova.
function InitializeSetup(): Boolean;
var
  Installed: String;
begin
  Result := True;
  if RegQueryStringValue(HKCU, UninstallKey, 'DisplayVersion', Installed) then
  begin
    if CompareVersions(Installed, '{#AppVersion}') > 0 then
    begin
      MsgBox('Já existe uma versão mais nova do Mixer de Áudio instalada (' + Installed + '). Desinstale-a antes.',
        mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;
  // Fecha toda cópia aberta, de qualquer pasta: a instância única do Windows App SDK vale por caminho do .exe.
  CloseAllRunning();
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  CloseRunningApp(ExpandConstant('{app}'));
end;

// D18: remove o valor MixerDeAudio de Run (e de StartupApproved\Run) só se o caminho dele estiver dentro de {app}.
procedure RemoveStartupEntries();
var
  Value, AppDir: String;
begin
  AppDir := Lowercase(RemoveBackslash(ExpandConstant('{app}')));
  if AppDir = '' then Exit;
  if RegQueryStringValue(HKCU, RunKey, 'MixerDeAudio', Value) then
  begin
    if Pos(AppDir + '\', Lowercase(Value)) > 0 then
    begin
      RegDeleteValue(HKCU, RunKey, 'MixerDeAudio');
      RegDeleteValue(HKCU, ApprovedKey, 'MixerDeAudio');
    end;
  end;
end;

function HasParam(const Name: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Name) = 0 then
    begin
      Result := True;
      Exit;
    end;
end;

procedure DeleteUserData();
var
  Dir: String;
begin
  // Só a pasta de dados do usuário, nunca {app}.
  Dir := ExpandConstant('{localappdata}\MixerDeAudio');
  if (Length(Dir) > 15) and DirExists(Dir) then
    DelTree(Dir, True, True, True);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Apagar: Boolean;
begin
  if CurUninstallStep = usUninstall then
    RemoveStartupEntries();

  if CurUninstallStep = usPostUninstall then
  begin
    // D18: padrão Não. No silencioso nunca apaga, a não ser com /APAGARDADOS.
    if UninstallSilent() then
      Apagar := HasParam('/APAGARDADOS')
    else
      Apagar := MsgBox('Apagar também as configurações e o registro (pasta %LOCALAPPDATA%\MixerDeAudio)? Isso não pode ser desfeito.',
        mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
    if Apagar then
      DeleteUserData();
  end;
end;
