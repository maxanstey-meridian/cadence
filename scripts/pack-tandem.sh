#!/bin/sh
set -eu

TANDEM_REPOSITORY=${TANDEM_REPOSITORY:-"$HOME/Sites/tandem"}
VERSION=${TANDEM_VERSION:-"0.1.9-local"}
mkdir -p "$(dirname -- "$0")/../packages"
FEED=$(CDPATH= cd -- "$(dirname -- "$0")/../packages" && pwd)

dotnet pack "$TANDEM_REPOSITORY/src/Tandem.Generators/Tandem.Generators.csproj" \
  --configuration Release --output "$FEED" -p:Version="$VERSION"
dotnet pack "$TANDEM_REPOSITORY/src/Tandem/Tandem.csproj" \
  --configuration Release --output "$FEED" -p:Version="$VERSION"
dotnet pack "$TANDEM_REPOSITORY/src/Tandem.Advanced/Tandem.Advanced.csproj" \
  --configuration Release --output "$FEED" -p:Version="$VERSION"
dotnet pack "$TANDEM_REPOSITORY/src/Tandem.Packets/Tandem.Packets.csproj" \
  --configuration Release --output "$FEED" -p:Version="$VERSION"
