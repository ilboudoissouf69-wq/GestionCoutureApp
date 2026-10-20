; =====================================================================
;  Gestion Couture — Script Inno Setup
;  Produit : Setup.exe (raccourcis Bureau + Menu Démarrer, désinstallation)
;
;  PRÉREQUIS :
;    1. Publier l'application :
;         dotnet publish ..\GestionCoutureApp.csproj -c Release -p:PublishProfile=SelfContainedWin64
;       → dossier ..\publish\ (exécutable autonome, .NET non requis sur le poste).
;    2. Compiler l'installeur avec Inno Setup (gratuit) :
;         ISCC.exe GestionCouture.iss   (ou via l'éditeur Inno Setup : Build > Compile)
;       → installer\Output\GestionCouture_Setup.exe
;
;  Les données (base SQLite) ne sont PAS touchées : elles vivent dans
;  %LOCALAPPDATA%\GestionCoutureApp, créées au premier lancement.
; =====================================================================

#define MyAppName           "Gestion Couture"
#define MyAppVersion        "2.0.0"
#define MyAppPublisher      "Atelier Ilboudo"
#define MyAppExeName        "GestionCoutureApp.exe"
#define MyAppExeRel         "publish\GestionCoutureApp.exe"

[Setup]
AppId={{8F2B6C90-1A2E-4D3F-B7C9-GESTIONCOUTURE}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\GestionCouture
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=GestionCouture_Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64
; Icône de l'installeur et de la désinstallation
SetupIconFile=..\Resources\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
; Une seule instance (cohérent avec le Mutex dans App.cs)
; (inutile côté installeur, mais on laisse .NET gérer le verrou runtime)
PrivilegesRequired=admin

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"; Flags: checkedonce

[Files]
; Application autonome (couture + matériaux + webcam + Google Drive, etc.)
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\Désinstaller {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Lancement optionnel après installation
Filename: "{app}\{#MyAppExeName}"; Description: "Lancer {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Nettoyer uniquement le dossier d'installation (jamais les données utilisateur)
Type: filesandordirs; Name: "{app}"

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;
