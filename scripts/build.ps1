# GrepSQL Build Script for Windows
# Requires: Visual Studio Build Tools, Git, .NET SDK, Protocol Buffers compiler

param(
    [switch]$SkipNative,
    [switch]$SkipProtobuf,
    [switch]$SkipBuild,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host "🏗️  Building GrepSQL with libpg_query (Windows)" -ForegroundColor Green

# Get script directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir

# Configuration
$LibPgQueryBranch = "17-latest"
$LibPgQueryRepo = "https://github.com/pganalyze/libpg_query.git"
$TargetRid = "win-x64"

Set-Location $ProjectRoot

# Step 1: Check prerequisites
Write-Host "🔍 Checking prerequisites..." -ForegroundColor Yellow

# Check for Git
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Error "Git is required but not found in PATH"
    exit 1
}

# Check for .NET SDK
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error ".NET SDK is required but not found in PATH"
    exit 1
}

# --- FIX: LOGIK ZUM AUTOMATISCHEN EINRICHTEN DER MSVC-UMGEBUNG ---
# Step 1.5: Setup MSVC Environment if nmake is not found
if (-not (Get-Command nmake -ErrorAction SilentlyContinue)) {
    Write-Host "⚠️ nmake.exe not found in PATH. Attempting to locate and configure MSVC environment..."
    
    # Pfad zu vswhere.exe (auf GitHub Actions Runnern vorhanden)
    $vswherePath = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswherePath)) {
        Write-Error "❌ vswhere.exe not found. Cannot automatically configure MSVC environment."
        exit 1
    }

    # Finde den Installationspfad von Visual Studio mit C++ Build Tools
    $vsInstallPath = & $vswherePath -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vsInstallPath) {
        Write-Error "❌ Could not find a Visual Studio installation with C++ Build Tools."
        exit 1
    }

    # Pfad zum vcvarsall.bat-Skript, das die Umgebung einrichtet
    $vcvarsall = Join-Path $vsInstallPath "VC\Auxiliary\Build\vcvarsall.bat"
    if (-not (Test-Path $vcvarsall)) {
        Write-Error "❌ Could not find vcvarsall.bat in the located Visual Studio installation."
        exit 1
    }

    Write-Host "✅ Found MSVC environment setup script at: $vcvarsall"
    # Wir werden dieses Skript später direkt vor dem nmake-Aufruf verwenden.
    $vcVarsCommand = """$vcvarsall"" x64"
}
else {
    Write-Host "✅ nmake.exe is already in PATH. Skipping MSVC environment setup."
    $vcVarsCommand = $null
}
# --- ENDE DES FIXES ---


# Step 2: Clone and prepare libpg_query
if (-not $SkipNative) {
    Write-Host "📥 Setting up libpg_query..." -ForegroundColor Yellow
    
    if (-not (Test-Path "libpg_query")) {
        Write-Host "Cloning libpg_query (branch: $LibPgQueryBranch)..."
        & git clone -b $LibPgQueryBranch $LibPgQueryRepo
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    else {
        Write-Host "libpg_query already exists, updating..."
        Set-Location libpg_query
        & git fetch origin
        & git checkout $LibPgQueryBranch
        & git pull origin $LibPgQueryBranch
        Set-Location ..
    }

    # Build libpg_query using Windows Makefile
    Write-Host "🔨 Building libpg_query for Windows..." -ForegroundColor Yellow
    Set-Location libpg_query
    
    if (Test-Path "Makefile.msvc") {
        Write-Host "Using Visual Studio tools (nmake)..."

        # --- FIX: Führe nmake innerhalb der konfigurierten Umgebung aus ---
        if ($vcVarsCommand) {
            # Umgebung einrichten UND dann nmake ausführen
            & cmd.exe /c "call $vcVarsCommand && nmake /F Makefile.msvc clean"
            & cmd.exe /c "call $vcVarsCommand && nmake /F Makefile.msvc"
        }
        else {
            # Umgebung war bereits eingerichtet, nmake direkt aufrufen
            & nmake /F Makefile.msvc clean
            & nmake /F Makefile.msvc
        }
        # --- ENDE DES FIXES ---
    }
    else {
        Write-Error "Makefile.msvc not found. Cannot build libpg_query."
        exit 1
    }
    
    Set-Location $ProjectRoot
}

# (Rest des Skripts bleibt unverändert)

# Step 3: Create runtime directories and copy libraries
if (-not $SkipNative) {
    Write-Host "📁 Setting up runtime directories..." -ForegroundColor Yellow
    
    $ProjectRuntimeDir = "src\GrepSQL\runtimes\$TargetRid\native"
    New-Item -Path $ProjectRuntimeDir -ItemType Directory -Force | Out-Null
    
    # Define source and destination for the DLL
    $SourceDll = "libpg_query\pg_query.dll"
    $DestinationDll = Join-Path $ProjectRuntimeDir "libpgquery_wrapper.dll"

    if (Test-Path $SourceDll) {
        Write-Host "Copying $SourceDll to $DestinationDll"
        # The .NET project expects the DLL to be named libpgquery_wrapper.dll
        Copy-Item $SourceDll $DestinationDll -Force
    }
    else {
        Write-Error "❌ Build artifact pg_query.dll not found in libpg_query directory."
        exit 1
    }
}

# Step 4: Generate protobuf files
if (-not $SkipProtobuf) {
    Write-Host "🔧 Generating protobuf files..." -ForegroundColor Yellow
    
    # Check for protoc
    $ProtocPath = Get-Command protoc -ErrorAction SilentlyContinue
    if (-not $ProtocPath) {
        Write-Warning "⚠️  protoc not found. Attempting to install via dotnet tool..."
        try {
            & dotnet tool install --global Grpc.Tools
        }
        catch {
            Write-Error "❌ Failed to install Grpc.Tools. Please install the Protocol Buffers compiler (protoc) manually and add it to your PATH."
            exit 1
        }
        
        # Check again
        $ProtocPath = Get-Command protoc -ErrorAction SilentlyContinue
        if (-not $ProtocPath) {
            Write-Error "❌ Could not find protoc after installation. Please add the dotnet tools directory to your PATH (usually %USERPROFILE%\.dotnet\tools)."
            exit 1
        }
    }
    
    # Define paths for protobuf generation
    $ProtoSrc = "libpg_query\protobuf"
    $ProtoOut = "src\GrepSQL\AST\Generated"
    
    New-Item -Path $ProtoOut -ItemType Directory -Force | Out-Null
    
    # Clean previous generated files
    Get-ChildItem "$ProtoOut\*.cs" -ErrorAction SilentlyContinue | Remove-Item -Force
    
    Write-Host "Generating C# protobuf classes..."
    
    # Execute the protoc command
    & $ProtocPath --proto_path="$ProtoSrc" --csharp_out="$ProtoOut" --csharp_opt=file_extension=.g.cs "$ProtoSrc\pg_query.proto"
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "❌ Protobuf generation failed."
        exit $LASTEXITCODE
    }
    
    # Verify that the main generated file exists
    if (-not (Test-Path "$ProtoOut\PgQuery.g.cs")) {
        Write-Error "❌ Expected protobuf file PgQuery.g.cs was not generated."
        exit 1
    }
    
    Write-Host "✅ Protobuf generation completed." -ForegroundColor Green
}

# Step 5: Build .NET project
if (-not $SkipBuild) {
    Write-Host "🔨 Building .NET project..." -ForegroundColor Yellow
    
    & dotnet restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    
    & dotnet build --configuration $Configuration --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        Write-Error "❌ .NET Build failed"
        exit $LASTEXITCODE
    }
    
    Write-Host "✅ .NET Build completed successfully." -ForegroundColor Green
    
    # Run tests if they exist
    if (Test-Path "tests\GrepSQL.Tests.csproj") {
        Write-Host "🧪 Running tests..." -ForegroundColor Yellow
        & dotnet test --configuration $Configuration --verbosity minimal
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "⚠️  Tests failed."
        }
    }
    
    # Create NuGet package if the project is packable
    Write-Host "📦 Creating NuGet package..." -ForegroundColor Yellow
    New-Item -Path "artifacts" -ItemType Directory -Force | Out-Null
    & dotnet pack "src\GrepSQL" --configuration $Configuration --output "artifacts"
}

Write-Host ""
Write-Host "🎉 Build process finished!" -ForegroundColor Green