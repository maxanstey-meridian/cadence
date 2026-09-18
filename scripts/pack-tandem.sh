#!/bin/sh
set -eu

TANDEM_REPOSITORY=${TANDEM_REPOSITORY:-"$HOME/Sites/tandem"}
VERSION=${TANDEM_VERSION:-"0.1.10-local"}
mkdir -p "$(dirname -- "$0")/../packages"
FEED=$(CDPATH= cd -- "$(dirname -- "$0")/../packages" && pwd)

for package in meridian.tandem.generators meridian.tandem meridian.tandem.advanced meridian.tandem.ledger meridian.tandem.openaicompatible meridian.tandem.terminal meridian.tandem.packets; do
  rm -rf "$HOME/.nuget/packages/$package/$VERSION"
done

dotnet pack "$TANDEM_REPOSITORY/src/Tandem.Generators/Tandem.Generators.csproj" \
  --configuration Release --output "$FEED" -p:Version="$VERSION"
dotnet pack "$TANDEM_REPOSITORY/src/Tandem/Tandem.csproj" \
  --configuration Release --output "$FEED" -p:Version="$VERSION"
dotnet pack "$TANDEM_REPOSITORY/src/Tandem.Advanced/Tandem.Advanced.csproj" \
  --configuration Release --output "$FEED" -p:Version="$VERSION"
dotnet pack "$TANDEM_REPOSITORY/src/Tandem.Ledger/Tandem.Ledger.csproj" \
  --configuration Release --output "$FEED" -p:Version="$VERSION"
dotnet pack "$TANDEM_REPOSITORY/src/Tandem.OpenAICompatible/Tandem.OpenAICompatible.csproj" \
  --configuration Release --output "$FEED" -p:Version="$VERSION"
dotnet pack "$TANDEM_REPOSITORY/src/Tandem.Terminal/Tandem.Terminal.csproj" \
  --configuration Release --output "$FEED" -p:Version="$VERSION"
dotnet pack "$TANDEM_REPOSITORY/src/Tandem.Packets/Tandem.Packets.csproj" \
  --configuration Release --output "$FEED" -p:Version="$VERSION"
