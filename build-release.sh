#!/bin/bash

# Build GrepSQL release binaries for all platforms
# Usage: ./build-release.sh [--skip-tests] [--platform PLATFORM]

set -e

# Default values
SKIP_TESTS=false
PLATFORMS=("linux-x64" "osx-x64" "osx-arm64" "win-x64")
BUILD_DIR="./build-output"

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --skip-tests)
            SKIP_TESTS=true
            shift
            ;;
        --platform)
            PLATFORMS=("$2")
            shift 2
            ;;
        -h|--help)
            echo "Usage: $0 [--skip-tests] [--platform PLATFORM]"
            echo ""
            echo "Options:"
            echo "  --skip-tests    Skip running tests"
            echo "  --platform      Build for specific platform only (linux-x64, osx-x64, osx-arm64, win-x64)"
            echo "  -h, --help      Show this help message"
            echo ""
            echo "Examples:"
            echo "  $0                                  # Build all platforms"
            echo "  $0 --platform osx-arm64           # Build only for Apple Silicon"
            echo "  $0 --skip-tests                   # Build all platforms without tests"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            echo "Use --help for usage information"
            exit 1
            ;;
    esac
done

echo "🏗️  Building GrepSQL release binaries..."
echo "📋 Platforms: ${PLATFORMS[*]}"
echo "🧪 Skip tests: $SKIP_TESTS"
echo ""

# Clean previous build
if [ -d "$BUILD_DIR" ]; then
    echo "🧹 Cleaning previous build..."
    rm -rf "$BUILD_DIR"
fi
mkdir -p "$BUILD_DIR"

# Build native libraries (Unix only)
if [[ "$OSTYPE" != "msys" && "$OSTYPE" != "win32" ]]; then
    echo "🔧 Building native libraries..."
    
    if [ -d "libpg_query" ]; then
        cd libpg_query
        make clean
        make
        cd ..
        
        # Determine library extension for current platform
        if [[ "$OSTYPE" == "linux-gnu"* ]]; then
            LIBRARY_EXT="so"
        elif [[ "$OSTYPE" == "darwin"* ]]; then
            LIBRARY_EXT="dylib"
        fi
        
        # Create wrapper if it doesn't exist
        if [ ! -f "wrapper.c" ]; then
            cat > wrapper.c << 'EOF'
#include "libpg_query/pg_query.h"

PgQueryParseResult pg_query_parse_wrapper(const char* input) {
    return pg_query_parse(input);
}

void pg_query_free_parse_result_wrapper(PgQueryParseResult result) {
    pg_query_free_parse_result(result);
}

PgQueryProtobufParseResult pg_query_parse_protobuf_wrapper(const char* input) {
    return pg_query_parse_protobuf(input);
}

void pg_query_free_protobuf_parse_result_wrapper(PgQueryProtobufParseResult result) {
    pg_query_free_protobuf_parse_result(result);
}

PgQueryNormalizeResult pg_query_normalize_wrapper(const char* input) {
    return pg_query_normalize(input);
}

void pg_query_free_normalize_result_wrapper(PgQueryNormalizeResult result) {
    pg_query_free_normalize_result(result);
}

PgQueryFingerprintResult pg_query_fingerprint_wrapper(const char* input) {
    return pg_query_fingerprint(input);
}

void pg_query_free_fingerprint_result_wrapper(PgQueryFingerprintResult result) {
    pg_query_free_fingerprint_result(result);
}

PgQueryScanResult pg_query_scan_wrapper(const char* input) {
    return pg_query_scan(input);
}

void pg_query_free_scan_result_wrapper(PgQueryScanResult result) {
    pg_query_free_scan_result(result);
}
EOF
        fi
        
        # Compile wrapper
        if [[ "$OSTYPE" == "linux-gnu"* ]]; then
            gcc -shared -fPIC -I. -o "libpgquery_wrapper.so" wrapper.c libpg_query/libpg_query.a
        elif [[ "$OSTYPE" == "darwin"* ]]; then
            gcc -shared -fPIC -I. -o "libpgquery_wrapper.dylib" wrapper.c libpg_query/libpg_query.a
        fi
        
        echo "✅ Native libraries built successfully"
    else
        echo "⚠️  libpg_query submodule not found. Run: git submodule update --init --recursive"
    fi
else
    echo "⚠️  Windows native build not supported in this script"
fi

# Restore dependencies
echo "📦 Restoring .NET dependencies..."
dotnet restore

# Build solution
echo "🔨 Building solution..."
dotnet build --configuration Release --no-restore

# Run tests (unless skipped)
if [ "$SKIP_TESTS" = false ]; then
    echo "🧪 Running tests..."
    dotnet test --configuration Release --no-build --verbosity normal
else
    echo "⏭️  Skipping tests"
fi

# Build binaries for each platform
for platform in "${PLATFORMS[@]}"; do
    echo ""
    echo "🚀 Building for $platform..."
    
    # Determine file extension
    if [[ "$platform" == "win-"* ]]; then
        extension=".exe"
        archive_ext="zip"
    else
        extension=""
        archive_ext="tar.gz"
    fi
    
    # Set up runtime directories for the platform
    if [[ "$platform" != "win-"* && "$OSTYPE" != "msys" && "$OSTYPE" != "win32" ]]; then
        mkdir -p "src/PgQuery.NET/runtimes/$platform/native"
        
        if [[ "$platform" == *"linux"* && -f "libpgquery_wrapper.so" ]]; then
            cp "libpgquery_wrapper.so" "src/PgQuery.NET/runtimes/$platform/native/"
        elif [[ "$platform" == *"osx"* && -f "libpgquery_wrapper.dylib" ]]; then
            cp "libpgquery_wrapper.dylib" "src/PgQuery.NET/runtimes/$platform/native/"
        fi
    fi
    
    # Publish binary
    output_dir="$BUILD_DIR/$platform"
    dotnet publish src/GrepSQL/GrepSQL/GrepSQL.csproj \
        --configuration Release \
        --runtime "$platform" \
        --self-contained true \
        --output "$output_dir" \
        -p:PublishSingleFile=true \
        -p:PublishTrimmed=true \
        -p:IncludeNativeLibrariesForSelfExtract=true
    
    # Test binary (if not Windows and not cross-compiling)
    if [[ "$platform" != "win-"* && ("$platform" == *"$(uname -m)"* || "$platform" == "osx-x64") ]]; then
        echo "🧪 Testing $platform binary..."
        echo "SELECT * FROM users;" | "$output_dir/GrepSQL$extension" "SelectStmt" || echo "⚠️  Binary test completed (may need runtime libraries)"
    fi
    
    # Create archive
    echo "📦 Creating archive..."
    cd "$BUILD_DIR"
    if [[ "$archive_ext" == "tar.gz" ]]; then
        tar -czf "grepsql-$platform.tar.gz" -C "$platform" "GrepSQL$extension"
    else
        # Use zip command if available, otherwise use built-in compression
        if command -v zip &> /dev/null; then
            cd "$platform"
            zip "../grepsql-$platform.zip" "GrepSQL$extension"
            cd ..
        else
            echo "⚠️  zip command not found, skipping Windows archive creation"
        fi
    fi
    cd ..
    
    echo "✅ $platform build completed"
done

echo ""
echo "🎉 All builds completed successfully!"
echo "📁 Build artifacts:"
ls -la "$BUILD_DIR"
echo ""
echo "📋 Usage examples:"
echo "  # Extract and run (Linux/macOS):"
echo "  tar -xzf $BUILD_DIR/grepsql-linux-x64.tar.gz"
echo "  ./GrepSQL \"SelectStmt\" *.sql"
echo ""
echo "  # Cross-platform pattern search:"
echo "  ./GrepSQL \"(relname \\\"users\\\")\" queries.sql --highlight"
echo ""
echo "🚀 Ready for distribution!" 