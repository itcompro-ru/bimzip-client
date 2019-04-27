#!/usr/bin/env bash

OUTDIR="Build.linux-x64"
BUILDFILE="BimZipClient"
NETBUILDPATH="$OUTDIR/unpacked"

if [ ! -f ./warp-packer ]; then
    curl -Lo warp-packer https://github.com/dgiagio/warp/releases/download/v0.3.0/linux-x64.warp-packer
    chmod +x warp-packer
fi
if [ -d "$OUTDIR" ]; then rm -Rf $OUTDIR; fi

dotnet publish -c Release -r linux-x64 --self-contained --output $NETBUILDPATH
./warp-packer --arch linux-x64 --input_dir $NETBUILDPATH --exec $BUILDFILE --output $BUILDFILE
mv $BUILDFILE $OUTDIR/$BUILDFILE
chmod +x $OUTDIR/$BUILDFILE
du -hs $OUTDIR/$BUILDFILE
