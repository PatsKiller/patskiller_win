# PatsKiller Pro

Ford & Lincoln PATS Key Programming Solution

## Version 2.0.0

### Features

- Auto-detection of vehicle via VIN reading
- Key programming with incode authentication
- Key erasing for all-keys-lost situations
- BCM Factory Defaults restoration
- ESCL/CEI steering lock initialization
- Door keypad code read/write
- 2020+ Gateway unlock support
- DTC clearing (P160A, B10A2, Crush Event)
- Comprehensive tooltips on all UI elements

### Requirements

- Windows 10/11 (64-bit)
- .NET 8.0 Runtime
- J2534 v2 compliant pass-thru device
- patskiller.com account with tokens

### Supported Vehicles

- Ford vehicles 2014-current with PATS
- Lincoln vehicles 2014-current with PATS

### Build Instructions

```bash
# Restore and build
dotnet restore
dotnet build --configuration Release

# Create single-file executable
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

### Token Costs

| Operation | Tokens |
|-----------|--------|
| Get Incode | 1 |
| Program Keys | FREE* |
| Erase All Keys | 1 |
| Initialize ESCL | 1 |
| Read/Write Keypad | 1 each |
| Clear P160A/B10A2 | 1 each |
| Gateway Unlock | 1 |
| BCM Factory Defaults | 2-3 |
| Clear DTCs | FREE |
| Vehicle Reset | FREE |

*FREE after incode obtained

### Support

- Website: https://patskiller.com
- FAQs: https://patskiller.com/faqs

---

© 2026 PatsKiller. All rights reserved.
