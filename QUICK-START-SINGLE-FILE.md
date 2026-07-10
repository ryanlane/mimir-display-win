# 🚀 Quick Start: Single-File Build

## The Problem
Too many DLLs in your build folder? That's because you're using `dotnet build`.

## The Solution
Use `dotnet publish` for single-file deployment!

---

## ONE COMMAND TO RULE THEM ALL

```powershell
.\publish-single-file.ps1
```

**Output:** `publish\MimirDisplay.exe` (80 MB, everything bundled!)

---

## What You Get

### Before
```
bin\Release\net8.0-windows\win-x64\
├── 400+ DLL files
├── Satellite assemblies
└── Native dependencies
```

### After
```
publish\
├── MimirDisplay.exe  ← ALL IN ONE!
├── .env
└── appsettings.json
```

---

## Daily Workflow

### Development Loop
```powershell
# Fast build for testing
dotnet build MimirDisplay\MimirDisplay.csproj -c Release
```

### Release Build
```powershell
# Create single-file executable
.\publish-single-file.ps1

# Test it
.\publish\MimirDisplay.exe
```

### Create Installer
```powershell
# 1. Publish first
.\publish-single-file.ps1

# 2. Generate GUID (one-time)
[guid]::NewGuid()

# 3. Update MimirDisplaySetup.iss with GUID

# 4. Compile in Inno Setup
# File → Open: MimirDisplaySetup.iss
# Build → Compile
```

---

## Files to Read

| Priority | File | What It Is |
|----------|------|------------|
| ⭐⭐⭐ | `SINGLE-FILE-SUMMARY.md` | Overview & FAQ |
| ⭐⭐ | `SINGLE-FILE-RESULTS.md` | Before/after details |
| ⭐ | `SINGLE-FILE-DEPLOYMENT.md` | Technical deep-dive |

---

## That's It!

You now have a single-file deployment system. No more DLL clutter! 🎉

**Command to remember:** `.\publish-single-file.ps1`
