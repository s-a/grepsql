#!/bin/bash

# GrepSQL - Easy to use wrapper script
# Usage: ./grepsql.sh "pattern" [files...] [options]

cd "$(dirname "$0")"
exec ./src/GrepSQL/GrepSQL/bin/Release/net9.0/osx-arm64/publish/GrepSQL "$@" 