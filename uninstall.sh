#!/usr/bin/env bash
# Remove Hark and its Claude Code hooks. Your other hooks and settings stay as they are.
set -euo pipefail
cd "$(dirname "$0")"
scripts/uninstall.sh
python3 -I hooks/install_hooks.py uninstall
