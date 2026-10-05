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

The application targets .NET 10 and uses WPF. The LAN transport keeps the same
encrypted payload format that a future cloud transport can use.

## Explorer integration

Build the app and compile `Installer\DropRoom.iss` with Inno Setup. The installer
registers the per-user `Send to room...` and `Receive here...` verbs, so no
administrator access is required.

## Security model

- Room keys are random and only their SHA-256-derived room IDs are advertised.
- Each transfer has a random file key; the file and metadata are encrypted
  before they leave the sender.
- PIN verification, per-recipient attempts, recipient count, and expiry are
  enforced by the sender in LAN mode.
- A 50 MB file cap and short default expiry reduce accidental abuse.

The current UI is intentionally focused on the MVP workflow. Cloud relay support,
tray integration, and delayed-rendered Explorer drag-out are subsequent additions.
