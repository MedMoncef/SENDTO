#define MyAppName "DropRoom"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "DropRoom"
#define MyAppExeName "DropRoom.exe"

[Setup]
AppId={{C6E1B260-A2BE-4FD1-B1AE-2A6A3D4C5E10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\DropRoom
DefaultGroupName=DropRoom
OutputDir=.
OutputBaseFilename=DropRoom-Setup
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64

[Files]
Source: "..\App\bin\Release\net10.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
Root: HKCU; Subkey: "Software\Classes\*\shell\SendToDropRoom"; ValueType: string; ValueName: ""; ValueData: "Send with DropRoom..."; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\*\shell\SendToDropRoom\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" send ""%1"""
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\ReceiveFromDropRoom"; ValueType: string; ValueName: ""; ValueData: "Grab from DropRoom..."; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\ReceiveFromDropRoom\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" receive ""%V"""
Root: HKCU; Subkey: "Software\Classes\Directory\shell\ReceiveFromDropRoom"; ValueType: string; ValueName: ""; ValueData: "Grab from DropRoom..."; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\ReceiveFromDropRoom\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" receive ""%1"""

[Run]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""DropRoom LAN"" dir=in action=allow protocol=TCP localport=41872 profile=private"; Flags: runhidden

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""DropRoom LAN"""; Flags: runhidden

[Icons]
Name: "{autoprograms}\DropRoom"; Filename: "{app}\{#MyAppExeName}"
