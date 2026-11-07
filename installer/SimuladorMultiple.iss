; Instalador de Windows del Simulador multiple (Inno Setup 6).
; Lo compila la CI: iscc /DAppVersion=0.1.N /DSourceDir=<carpeta publicada> /DOutputDir=<salida> installer\SimuladorMultiple.iss

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\SimuladorMultiple"
#endif
#ifndef OutputDir
  #define OutputDir "..\publish"
#endif

[Setup]
AppId={{6F0D3A52-6B7E-4B4B-9E7A-3C1E5B2A9D41}
AppName=Simulador multiple
AppVersion={#AppVersion}
AppPublisher=Simulador multiple
DefaultDirName={autopf}\Simulador multiple
DefaultGroupName=Simulador multiple
DisableProgramGroupPage=yes
; Se instala para el usuario actual: no pide permisos de administrador.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=SimuladorMultiple-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\SimuladorMultiple.exe
; Icono del propio instalador (el mismo logotipo que la aplicación).
SetupIconFile=..\src\Simulador.App\Assets\icono.ico

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Simulador multiple"; Filename: "{app}\SimuladorMultiple.exe"
Name: "{autodesktop}\Simulador multiple"; Filename: "{app}\SimuladorMultiple.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SimuladorMultiple.exe"; Description: "{cm:LaunchProgram,Simulador multiple}"; Flags: nowait postinstall skipifsilent
