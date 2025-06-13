#!/bin/bash

# GrepSQL - Easy to use wrapper script
# Usage: ./grepsql.sh "pattern" [files...] [options]

cd "$(dirname "$0")"
exec ./src/GrepSQL/GrepSQL/bin/Debug/net9.0/osx-arm64/GrepSQL "$@" 