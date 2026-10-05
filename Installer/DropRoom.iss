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
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64

[Files]
Source: "..\App\bin\Release\net10.0-windows\win-x64\publish\DropRoom.exe"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
Root: HKCU; Subkey: "Software\Classes\*\shell\SendToRoom"; ValueType: string; ValueName: ""; ValueData: "Send to room..."; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\*\shell\SendToRoom\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" send ""%1"""
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\ReceiveHere"; ValueType: string; ValueName: ""; ValueData: "Receive here..."; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\ReceiveHere\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" receive ""%V"""
Root: HKCU; Subkey: "Software\Classes\Directory\shell\ReceiveHere"; ValueType: string; ValueName: ""; ValueData: "Receive here..."; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\ReceiveHere\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" receive ""%1"""

[Icons]
Name: "{autoprograms}\DropRoom"; Filename: "{app}\{#MyAppExeName}"
