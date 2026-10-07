# DropRoom

DropRoom is a Windows room-based file transfer client. It is designed for small,
short-lived transfers between people who share a room key, with Explorer verbs
for both sending and receiving.

## Build

```powershell
dotnet restore .\RoomTransfer.slnx
dotnet build .\RoomTransfer.slnx
dotnet publish .\App\App.csproj -c Release -r win-x64 --self-contained true
```

The standalone executable is generated at:

```text
App\bin\Release\net10.0-windows\win-x64\publish\DropRoom.exe
```

You can launch that executable directly for a quick test. For Explorer
integration, install the published build instead of copying only the EXE:
compile `Installer\DropRoom.iss` with Inno Setup, then run the generated
`Installer\DropRoom-Setup.exe` as administrator. After installation, right-click
a file and choose **Show more options → Send with DropRoom...**, or right-click
inside a folder and choose **Show more options → Grab from DropRoom...**.

The application targets .NET 10 and uses WPF. The project includes a shared
DropRoom logo asset used by the app, published executable, Start Menu shortcut,
and installer.

## Delivery modes

The app deliberately names the two routes explicitly:

- **Office sharing:** the local-device route. The sender hosts the encrypted
  file and it expires locally. The current MVP has the local HTTP host and
  transport contracts; automatic cross-PC discovery, notifications, and
  synchronized room lists are still being completed.
- **Public send:** the external internet route. The client uploads encrypted payloads to an HTTP backend
  (normally a Cloudflare Worker backed by R2/D1). The client never receives or
  stores database credentials.

Configure **Settings → Public send → Backend URL** with the base URL of your
deployed Worker/API, for example `https://files.example.com/`, then choose
**Public send** as the default delivery. This project contains the typed client in
`Transport.Cloud`; it does not include a hosted Worker or database deployment.
The backend must implement the documented `/v1/rooms/{roomId}/transfers`
endpoints and enforce quotas, PIN attempts, access lists, and expiry.

## Explorer integration

Publish the app first, then compile `Installer\DropRoom.iss` with Inno Setup.
The installer registers the per-user **Send with DropRoom...** and **Grab from
DropRoom...** verbs, so no
administrator access is required.

On Windows 11, legacy verbs are under **Show more options**. The installer
registers both empty-folder background receive and folder receive commands.
The app name is explicitly `DropRoom.exe`, and the installer includes the full
self-contained publish directory rather than only the launcher.
The installer requests elevation because it installs under Program Files and
adds the LAN firewall rule; the Explorer verbs themselves are still written to
HKCU.

## Security model

- Room keys are random and only their SHA-256-derived room IDs are advertised.
- Each transfer has a random file key; the file and metadata are encrypted
  before they leave the sender.
- PIN verification, per-recipient attempts, recipient count, and expiry are
  enforced by the sender in LAN mode.
- A 50 MB file cap and short default expiry reduce accidental abuse.

Multicast discovery, access-request polish, delayed-rendered Explorer drag-out,
and full cross-PC Office sharing remain subsequent additions. DropRoom stays
running in the Windows notification area when its main window is closed or
after an Explorer send/receive action. Use the tray icon to reopen it, or
choose **Exit DropRoom** from the tray menu to stop the local server.
The receive flow includes a progress bar and cancellation while the encrypted
payload is downloaded. The current LAN fallback binds to loopback if Windows
refuses the non-loopback listener; in that case the installer/firewall and
network policy must be corrected before cross-PC LAN sharing is possible.
