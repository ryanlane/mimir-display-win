# Adding Icon and Installer to Mimir Display

## Application Icon Setup

### Icon File Requirements

**Important:** SVG files do **NOT** work for Windows application icons. You need a `.ico` file.

A `.ico` file is a Windows icon format that contains multiple resolutions of the same icon:
- 16×16 (taskbar, small icons)
- 32×32 (standard desktop icons)
- 48×48 (large icons)
- 256×256 (extra large icons, Windows Vista+)

### Step 1: Create or Convert Your Icon

#### Option A: Convert SVG to ICO (Recommended)

1. **Online Converters:**
   - https://convertio.co/svg-ico/
   - https://cloudconvert.com/svg-to-ico
   - Upload your SVG and download as `.ico`

2. **Command-line (ImageMagick):**
   ```powershell
   # Install ImageMagick first (if not already installed)
   winget install ImageMagick.ImageMagick

   # Convert SVG to multi-resolution ICO
   magick convert your-logo.svg -define icon:auto-resize=256,48,32,16 mimir.ico
   ```

3. **Photoshop/GIMP:**
   - Export your SVG as PNG at 256×256
   - Use a plugin to save as `.ico` with multiple sizes

#### Option B: Use an Existing PNG

If you have a PNG logo:
```powershell
magick convert your-logo.png -define icon:auto-resize=256,48,32,16 mimir.ico
```

### Step 2: Put the Icon File in the Right Place

Place your `mimir.ico` file here:
```
MimirDisplay\Resources\mimir.ico
```

The project is already configured to look for this file (line 61 of `MimirDisplay.csproj`).

### Step 3: Configure the Icon in the Project

Add this to your `MimirDisplay.csproj` inside the `<PropertyGroup>` section (around line 18):

```xml
<PropertyGroup>
  <!-- ... existing properties ... -->

  <!-- Application icon -->
  <ApplicationIcon>Resources\mimir.ico</ApplicationIcon>

  <!-- Assembly metadata -->
  <Company>Mimir</Company>
  <Product>Mimir Display Client</Product>
  <Description>Mimir smart display client for Windows</Description>
  <Copyright>Copyright © 2026</Copyright>
</PropertyGroup>
```

### Step 4: Rebuild the Project

```powershell
dotnet build -c Release
```

Your `.exe` will now have the icon!

---

## Creating a Windows Installer

For a professional installer, I recommend using **WiX Toolset** (free, industry-standard).

### Option A: WiX Installer (Recommended)

#### 1. Install WiX Tools

```powershell
# Install WiX Toolset v4 (or download from https://wixtoolset.org/)
dotnet tool install --global wix
```

#### 2. Create a WiX Project

Create a new directory for the installer:
```
mimir-display-win\
  ├── MimirDisplay\          (your existing project)
  └── MimirDisplay.Installer\  (new installer project)
	  ├── MimirDisplay.Installer.wixproj
	  └── Product.wxs
```

**MimirDisplay.Installer.wixproj:**
```xml
<Project Sdk="WixToolset.Sdk/4.0.5">
  <PropertyGroup>
	<OutputName>MimirDisplaySetup</OutputName>
	<OutputType>Package</OutputType>
  </PropertyGroup>

  <ItemGroup>
	<PackageReference Include="WixToolset.UI.wixext" Version="4.0.5" />
  </ItemGroup>
</Project>
```

**Product.wxs:**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Package 
	Name="Mimir Display" 
	Version="1.0.0" 
	Manufacturer="Mimir" 
	UpgradeCode="YOUR-GUID-HERE"
	Language="1033">

	<MajorUpgrade DowngradeErrorMessage="A newer version is already installed." />
	<MediaTemplate EmbedCab="yes" />

	<Feature Id="ProductFeature" Title="Mimir Display" Level="1">
	  <ComponentGroupRef Id="ProductComponents" />
	</Feature>
  </Package>

  <Fragment>
	<Directory Id="TARGETDIR" Name="SourceDir">
	  <Directory Id="ProgramFiles64Folder">
		<Directory Id="INSTALLFOLDER" Name="MimirDisplay" />
	  </Directory>
	  <Directory Id="ProgramMenuFolder">
		<Directory Id="ApplicationProgramsFolder" Name="Mimir Display"/>
	  </Directory>
	</Directory>
  </Fragment>

  <Fragment>
	<ComponentGroup Id="ProductComponents" Directory="INSTALLFOLDER">
	  <Component Id="MimirDisplayExe">
		<File Source="$(var.SolutionDir)MimirDisplay\bin\Release\net8.0-windows\win-x64\publish\MimirDisplay.exe" 
			  KeyPath="yes" 
			  Checksum="yes">
		  <Shortcut Id="startmenuMimirDisplay" 
					Directory="ApplicationProgramsFolder" 
					Name="Mimir Display"
					WorkingDirectory="INSTALLFOLDER" 
					Icon="MimirIcon.exe" 
					IconIndex="0" 
					Advertise="yes" />
		</File>
	  </Component>
	  <Component Id="MimirDisplayConfig">
		<File Source="$(var.SolutionDir)MimirDisplay\bin\Release\net8.0-windows\win-x64\publish\.env" />
	  </Component>
	</ComponentGroup>
  </Fragment>

  <Fragment>
	<Icon Id="MimirIcon.exe" SourceFile="$(var.SolutionDir)MimirDisplay\Resources\mimir.ico"/>
  </Fragment>
</Wix>
```

#### 3. Generate a Unique UpgradeCode GUID

```powershell
[guid]::NewGuid()
```

Copy the output and replace `YOUR-GUID-HERE` in the `Product.wxs` file.

#### 4. Build the Installer

```powershell
# Publish your app first
dotnet publish MimirDisplay\MimirDisplay.csproj -c Release -r win-x64 --self-contained true

# Build the installer
cd MimirDisplay.Installer
wix build Product.wxs -o MimirDisplaySetup.msi
```

---

### Option B: MSIX Package (Modern Windows)

MSIX is the modern Windows packaging format (for Windows Store and enterprise deployment).

#### 1. Add Packaging Project

In Visual Studio:
1. Right-click solution → **Add** → **New Project**
2. Search for **Windows Application Packaging Project**
3. Name it `MimirDisplay.Package`
4. Add reference to `MimirDisplay` project

#### 2. Configure Package

Edit `Package.appxmanifest`:
- Set Display Name: "Mimir Display"
- Set Publisher: Your organization
- Add icon references (PNG format, 44×44, 150×150, etc.)

#### 3. Build

```powershell
msbuild MimirDisplay.Package\MimirDisplay.Package.wixproj /p:Configuration=Release
```

This creates a `.msix` file that users can double-click to install.

---

### Option C: Inno Setup (Simpler Alternative)

If WiX is too complex, try **Inno Setup** (free, GUI-based).

1. Download from https://jrsoftware.org/isdl.php
2. Run the wizard:
   - Application name: Mimir Display
   - Version: 1.0.0
   - Publisher: Your name
   - Application executable: Point to your published `.exe`
   - Icon: Point to `mimir.ico`
   - Create Start Menu shortcuts: Yes
3. Save the script and compile

Inno Setup generates a `setup.exe` that's easy to distribute.

---

## Quick Start (Recommended Path)

### 1. Get an Icon File

If you have an SVG or PNG:

```powershell
# Install ImageMagick if needed
winget install ImageMagick.ImageMagick

# Convert to ICO (replace paths with your actual files)
magick convert your-logo.svg -define icon:auto-resize=256,48,32,16 MimirDisplay\Resources\mimir.ico
```

Or use an online converter: https://convertio.co/svg-ico/

### 2. Update Your Project File

I'll do this for you in the next step!

### 3. Choose Installer Type

- **Simple & fast:** Use Inno Setup (GUI, no coding)
- **Professional:** Use WiX (industry standard, scriptable)
- **Modern Windows:** Use MSIX (Windows Store compatible)

---

## Summary

| Requirement | Solution | File Location |
|-------------|----------|---------------|
| Application icon | `.ico` file (not SVG) | `MimirDisplay\Resources\mimir.ico` |
| Project setting | Add `<ApplicationIcon>` to `.csproj` | I'll update this for you |
| Simple installer | Inno Setup (GUI) | Download from jrsoftware.org |
| Professional installer | WiX Toolset | Requires separate `.wixproj` + `.wxs` files |
| Modern installer | MSIX | Requires packaging project in solution |

Would you like me to:
1. Update your `.csproj` file to use the icon (once you place `mimir.ico` in `Resources\`)?
2. Generate a complete WiX installer project for you?
3. Create an Inno Setup script template?
