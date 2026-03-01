#!/bin/bash
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

# Install .NET 10 SDK if not already installed
if ! dotnet --version 2>/dev/null | grep -q "^10\."; then
  echo "Installing .NET 10 SDK..."
  apt-get update -q
  apt-get install -y dotnet-sdk-10.0
  echo "✓ .NET 10 SDK installed"
else
  echo "✓ .NET 10 SDK already installed"
fi

# Restore NuGet packages
echo "Restoring NuGet packages..."
dotnet restore "$CLAUDE_PROJECT_DIR/PaperNexus.sln"
echo "✓ NuGet packages restored"
