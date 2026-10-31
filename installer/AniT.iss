#ifndef AppVersion
  #define AppVersion "2.0.3"
#endif

#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

#define AppName "AniT"
#define AppExeName "AniT.exe"
#define AppPublisher "Pedro Vitor"
#define AppUrl "https://github.com/PedroVitor-Dev/AniT"

[Setup]
#ifdef SigningEnabled
SignTool=AniTReleaseSign
SignedUninstaller=yes
SignToolRetryCount=2
#endif
AppId={{B5E77420-FA12-42AC-B811-32EAE2C6DAF3}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
AppCopyright=Copyright (C) 2026 {#AppPublisher}
AppComments=Sua biblioteca local de animes, organizada e sob o seu controle.
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=AniT-Setup-{#AppVersion}-win-x64
SetupIconFile=..\assets\anit.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}
LicenseFile=..\LICENSE
InfoBeforeFile=INSTALLATION_NOTES.txt
WizardStyle=modern dark polar includetitlebar
WizardSizePercent=120,120
WizardImageFile=assets\wizard-large.png
WizardSmallImageFile=assets\wizard-small.png
DisableWelcomePage=no
DisableReadyPage=no
DisableFinishedPage=no
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
DirExistsWarning=no
Compression=lzma2/ultra64
SolidCompression=yes
LZMANumBlockThreads=4
TimeStampsInUTC=yes
VersionInfoVersion={#AppVersion}.0
VersionInfoProductVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription=Instalador oficial do {#AppName}
VersionInfoCompany={#AppPublisher}
VersionInfoCopyright=Copyright (C) 2026 {#AppPublisher}

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\AniT"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"
Name: "{autodesktop}\AniT"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Description: "{cm:StartAniT}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[CustomMessages]
brazilianportuguese.StartAniT=Iniciar o AniT
english.StartAniT=Start AniT
brazilianportuguese.WelcomeTitle=Bem-vindo ao AniT
brazilianportuguese.WelcomeBody=Sua biblioteca de animes está prestes a ganhar um novo lar.%n%nO assistente instalará tudo o que o AniT precisa para funcionar. É rápido, seguro e seus dados pessoais continuarão sob o seu controle.
brazilianportuguese.FinishedTitle=Tudo pronto para a próxima história!
brazilianportuguese.FinishedBody=Clique em Concluir com a opção Iniciar o AniT marcada para abrir o aplicativo. Na primeira utilização, configure seu perfil e a pasta dos seus animes.
english.WelcomeTitle=Welcome to AniT
english.WelcomeBody=Your anime library is about to get a new home.%n%nThis wizard installs everything AniT needs. It is fast, safe, and your personal data remains under your control.
english.FinishedTitle=Ready for your next story!
english.FinishedBody=Click Finish with Start AniT checked to open the app. On first use, set up your profile and anime folder.

[Code]
var
  FinishLaunchInitialized: Boolean;
  WizardForegroundRequested: Boolean;

procedure CurPageChanged(CurPageID: Integer);
begin
  { Request foreground only once, after the first page is shown. }
  if not WizardSilent and not WizardForegroundRequested then
  begin
    WizardForegroundRequested := True;
    BringToFrontAndRestore;
  end;

  if (CurPageID = wpFinished) and not FinishLaunchInitialized and (WizardForm.RunList.Items.Count > 0) then
  begin
    WizardForm.RunList.Checked[0] := True;
    FinishLaunchInitialized := True;
  end;
end;

procedure InitializeWizard;
begin
  WizardForm.Caption := 'AniT ' + '{#AppVersion}';
  WizardForm.WelcomeLabel1.Caption := CustomMessage('WelcomeTitle');
  WizardForm.WelcomeLabel2.Caption := CustomMessage('WelcomeBody');
  WizardForm.FinishedHeadingLabel.Caption := CustomMessage('FinishedTitle');
  WizardForm.FinishedLabel.Caption := CustomMessage('FinishedBody');
end;
