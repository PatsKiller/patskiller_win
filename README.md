# PatsKiller Pro v2.0 (V3 UI)

Ford & Lincoln PATS Key Programming Solution for Automotive Locksmiths

## What's New in V3

### UI Enhancements
- **Header with Token Display**: Shows purchase tokens (blue) and promo tokens (fluorescent green)
- **Gateway Session Banner**: Green banner shows countdown timer during free key programming session
- **Three-Tab Layout**: PATS Operations, Utility (1 token), Free Functions
- **Submit Button for Incode**: Operations locked until incode is verified
- **Iterative Parameter Reset**: Visual progress through BCM → ABS → PCM modules

### New Features
- Gateway Unlock for 2020+ vehicles opens 10-minute FREE key programming session
- Troubleshooting options: "ABS on CAN 2" and "Skip ABS (2 modules only)"
- Clear Crash Event operation (DID 5B17)
- Activity log with color-coded messages

### Architecture Improvements
- Services folder for future API integration
- Models folder for data structures
- Prepared for NativeAOT compilation (IP protection)
- Machine ID service for license binding

## Building

### Prerequisites
- .NET 8 SDK
- Windows 10/11
- Visual Studio 2022 (recommended) or VS Code

### Build Commands

```bash
# Debug build
dotnet build -c Debug

# Release build
dotnet publish -c Release -r win-x64 --self-contained

# With NativeAOT (production - uncomment in .csproj first)
dotnet publish -c Release -r win-x64 --self-contained -p:PublishAot=true
```

### Build Output
- Debug: `bin\Debug\net8.0-windows\`
- Release: `bin\Release\net8.0-windows\win-x64\publish\`

## Project Structure

```
PatsKillerPro/
├── J2534/              # J2534 device communication
├── Vehicle/            # Ford PATS operations
├── Communication/      # UDS protocol
├── Utils/              # Logger, Settings
├── Services/           # API integration (Phase 1-2)
├── Models/             # Data models
├── Resources/          # Icons, images
└── MainForm.cs         # Main UI (V3)
```

## Integration Phases

### Current (V3 Base)
- Complete V3 UI matching mockup
- All PATS operations functional
- Offline mode (manual incode entry)

### Phase 1 (Coming)
- WebView2 login to patskiller.com
- Machine activation system
- Real token display from account

### Phase 2 (Coming)
- Automatic incode from API
- WebSocket real-time balance updates
- Gateway session management via API

## Usage

1. **Connect**: Scan for J2534 devices, select, and connect
2. **Read Vehicle**: Auto-detects VIN and vehicle model
3. **Get Incode**: Copy outcode to patskiller.com/calculator
4. **Submit Incode**: Enter incode and click Submit to unlock operations
5. **Perform Operations**: Key programming, Parameter Reset, etc.

### 2020+ Vehicles
For 2020+ vehicles, use **Gateway Unlock** first to get 10 minutes of FREE key programming!

## Support

- Website: https://patskiller.com
- Calculator: https://patskiller.com/calculator
- Email: support@patskiller.com

---
© 2026 PatsKiller. All rights reserved.
