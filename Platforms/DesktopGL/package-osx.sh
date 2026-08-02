#!/bin/sh
# Builds UltimateTicTacToe.app and leaves it in <output-dir>.
#
#     ./Platforms/DesktopGL/package-osx.sh 2.0.0 artifacts/osx
#
# Has to run on macOS: codesign only exists there, and Apple Silicon kills any
# process whose Mach-O images aren't signed, so an unsigned build is a build
# that never starts.
#
# The bundle carries a full native tree per architecture rather than one merged
# universal tree. lipo can fuse the dylibs, but a self-contained .NET app also
# ships framework assemblies compiled per architecture, and those are managed PE
# files that lipo can't merge. Pairing one architecture's assemblies with the
# other's coreclr is not a supported configuration, so each tree stays exactly as
# dotnet publish produced it and a launcher picks between them.

set -eu

VERSION=${1:?usage: package-osx.sh <version> <output-dir>}
OUTPUT=${2:?usage: package-osx.sh <version> <output-dir>}

PROJECT=$(cd "$(dirname "$0")" && pwd)

APP="$OUTPUT/UltimateTicTacToe.app"
CONTENTS="$APP/Contents"

rm -rf "$APP"
mkdir -p "$CONTENTS/MacOS" "$CONTENTS/Resources"

for arch in arm64 x64; do
    dotnet publish "$PROJECT" -c Release -r "osx-$arch" --self-contained \
        -p:Version="$VERSION" --output "$CONTENTS/MacOS/$arch"
done

# MonoGame's TitleContainer probes ../Resources then ../../Resources before
# falling back to the base directory. The executable sits two levels down in
# MacOS/<arch>/, so ../../Resources lands here and both trees share one copy.
mv "$CONTENTS/MacOS/arm64/Content" "$CONTENTS/Resources/Content"
rm -rf "$CONTENTS/MacOS/x64/Content"

cp "$PROJECT/Icon.icns" "$CONTENTS/Resources/Icon.icns"
sed "s/__VERSION__/$VERSION/g" "$PROJECT/Info.plist" > "$CONTENTS/Info.plist"
printf 'APPL????' > "$CONTENTS/PkgInfo"

cat > "$CONTENTS/MacOS/UltimateTicTacToe" <<'LAUNCHER'
#!/bin/sh
# exec replaces this process in place, so the game inherits the identity
# LaunchServices handed the bundle and the Dock tile stays put.
DIR=$(cd "$(dirname "$0")" && pwd)
case $(uname -m) in
    arm64) exec "$DIR/arm64/UltimateTicTacToe" "$@" ;;
    *)     exec "$DIR/x64/UltimateTicTacToe" "$@" ;;
esac
LAUNCHER
chmod +x "$CONTENTS/MacOS/UltimateTicTacToe" \
         "$CONTENTS/MacOS/arm64/UltimateTicTacToe" \
         "$CONTENTS/MacOS/x64/UltimateTicTacToe"

# Ad-hoc sign every Mach-O we ship. Most arrive signed already, but re-signing
# is idempotent and cheap next to shipping one stray image that gets the whole
# process killed on launch.
# The inner set -e matters: without it a failing codesign leaves the loop's exit
# status to whichever file happened to come last, and a silently unsigned bundle
# is one that dies on launch with nothing in the build log to explain why.
find "$CONTENTS/MacOS" -mindepth 2 -type f -exec sh -c '
    set -e
    for f do
        case $(file -b "$f") in
            *Mach-O*) codesign --force --sign - "$f" ;;
        esac
    done
' sh {} +

# The bundle itself is deliberately left unsigned. Without an Apple Developer ID
# there is nothing to notarize against, and a bundle seal that gets stripped in
# transit reads to Gatekeeper as tampering, which is worse than no seal at all.

echo "Built $APP ($(du -sh "$APP" | cut -f1))"
