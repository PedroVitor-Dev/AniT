#ifndef AppVersion
  #define AppVersion "1.0.0"
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
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,AniT}"; Flags: nowait postinstall skipifsilent

[CustomMessages]
brazilianportuguese.WelcomeTitle=Bem-vindo ao AniT
brazilianportuguese.WelcomeBody=Sua biblioteca de animes está prestes a ganhar um novo lar.%n%nO assistente instalará tudo o que o AniT precisa para funcionar. É rápido, seguro e seus dados pessoais continuarão sob o seu controle.
brazilianportuguese.FinishedTitle=Tudo pronto para a próxima história!
brazilianportuguese.FinishedBody=O AniT foi instalado com sucesso. Abra o aplicativo, escolha sua estante e deixe o Baki-Pi organizar sua jornada.
english.WelcomeTitle=Welcome to AniT
english.WelcomeBody=Your anime library is about to get a new home.%n%nThis wizard installs everything AniT needs. It is fast, safe, and your personal data remains under your control.
english.FinishedTitle=Ready for your next story!
english.FinishedBody=AniT was installed successfully. Open the app, choose your library folders, and let Baki-Pi organize your journey.

[Code]
procedure InitializeWizard;
begin
  WizardForm.Caption := 'AniT ' + '{#AppVersion}';
  WizardForm.WelcomeLabel1.Caption := CustomMessage('WelcomeTitle');
  WizardForm.WelcomeLabel2.Caption := CustomMessage('WelcomeBody');
  WizardForm.FinishedHeadingLabel.Caption := CustomMessage('FinishedTitle');
  WizardForm.FinishedLabel.Caption := CustomMessage('FinishedBody');
end;
