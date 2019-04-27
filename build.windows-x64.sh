#!/usr/bin/env bash

OUTDIR="Build.win-x64"
BUILDFILE="BimZipClient.exe"
NETBUILDPATH="$OUTDIR/unpacked"

if [ ! -f ./warp-packer ]; then
    curl -Lo warp-packer https://github.com/dgiagio/warp/releases/download/v0.3.0/linux-x64.warp-packer
    chmod +x warp-packer
fi
if [ -d "$OUTDIR" ]; then rm -Rf $OUTDIR; fi

dotnet publish -c Release -r win-x64 --self-contained --output $NETBUILDPATH
./warp-packer --arch windows-x64 --input_dir $NETBUILDPATH --exec $BUILDFILE --output $BUILDFILE
mv $BUILDFILE $OUTDIR/$BUILDFILE
du -hs $OUTDIR/$BUILDFILE