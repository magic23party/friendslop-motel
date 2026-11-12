#!/bin/sh
# check + build + shots (для итераций P1)
cd "$(dirname "$0")/.." || exit 1
powershell -ExecutionPolicy Bypass -File tools/check.ps1 || exit 1
powershell -ExecutionPolicy Bypass -File tools/build.ps1 || exit 1
powershell -ExecutionPolicy Bypass -File tools/shots.ps1 -Label "${1:-p1}"
