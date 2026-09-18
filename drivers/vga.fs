\
\ Fcode payload for QEMU VGA graphics card
\
\ This is the Forth source for an Fcode payload to initialise
\ the QEMU VGA graphics card.
\
\ (C) Copyright 2013 Mark Cave-Ayland
\

fcode-version3

\
\ Dictionary lookups for words that don't have an FCode
\

: (find-xt)   \ ( str len -- xt | -1 )
  $find if
    exit
  else
    -1
  then
;

" openbios-video-width" (find-xt) cell+ value openbios-video-width-xt
" openbios-video-height" (find-xt) cell+ value openbios-video-height-xt
" depth-bits" (find-xt) cell+ value depth-bits-xt
" line-bytes" (find-xt) cell+ value line-bytes-xt

: openbios-video-width openbios-video-width-xt @ ;
: openbios-video-height openbios-video-height-xt @ ;
: depth-bits depth-bits-xt @ ;
: line-bytes line-bytes-xt @ ;

" fb8-fillrect" (find-xt) value fb8-fillrect-xt
: fb8-fillrect fb8-fillrect-xt execute ;

" fw-cfg-read-file" (find-xt) value fw-cfg-read-file-xt
: fw-cfg-read-file fw-cfg-read-file-xt execute ;

\
\ IO port words
\

" ioc!" (find-xt) value ioc!-xt
" iow!" (find-xt) value iow!-xt

: ioc! ioc!-xt execute ;
: iow! iow!-xt execute ;

" le-w!" (find-xt) value le-w!-xt

: le-w! le-w!-xt execute ;

\
\ PCI
\

" pci-bar>pci-addr" (find-xt) value pci-bar>pci-addr-xt
: pci-bar>pci-addr pci-bar>pci-addr-xt execute ;

h# 10 constant cfg-bar0    \ Framebuffer BAR
h# 18 constant cfg-bar2    \ QEMU MMIO ioport BAR
-1 value fb-addr
-1 value mmio-addr

\
\ NVIDIA GeForce3 (NV20): unlike the generic Bochs-VBE card above,
\ BAR0 is MMIO (registers) and BAR1 is VRAM (the framebuffer) -- the
\ opposite arrangement, and real NV20 hardware's own layout (same as
\ Linux's nouveau driver expects). Detected at runtime from this
\ node's own vendor-id/device-id properties (already set generically
\ by ob_pci_add_properties before this file's init word ever runs),
\ so the one compiled "QEMU,VGA.bin" payload can drive either card.
\

h# 10de constant nv-vendor-id-geforce3
h# 0200 constant nv-device-id-geforce3

h# 10 constant nv-cfg-bar0    \ MMIO BAR
h# 14 constant nv-cfg-bar1    \ VRAM BAR
-1 value nv-mmio-addr
-1 value nv-vram-addr

\ get-my-property's own convention is inverted from the usual
\ ?dup/if idiom: failure leaves just a lone "true" (name-str/name-len
\ already consumed), success leaves "prop-addr prop-len false".
: get-my-int-property ( name-str name-len -- n )
  get-my-property if
    0
  else
    \ decode-int leaves ( addr2 len2 n ), n on top
    decode-int nip nip
  then
;

: my-vendor-id ( -- n )  " vendor-id" get-my-int-property ;
: my-device-id ( -- n )  " device-id" get-my-int-property ;

: nv-geforce3? ( -- flag )
  my-vendor-id nv-vendor-id-geforce3 =
  my-device-id nv-device-id-geforce3 = and
;

\
\ VGA registers
\

h# 3c0 constant vga-addr
h# 3c8 constant dac-write-addr
h# 3c9 constant dac-data-addr

defer vga-ioc!

: vga-legacy-ioc!  ( val addr )
  ioc! 
;

: vga-mmio-ioc!  ( val addr )
  h# 3c0 - h# 400 + mmio-addr + c!
;

: vga-color!  ( r g b index -- )
  \ Set the VGA colour registers
  dac-write-addr vga-ioc! rot
  2 >> dac-data-addr vga-ioc! swap
  2 >> dac-data-addr vga-ioc!
  2 >> dac-data-addr vga-ioc!
;

\
\ VBE registers
\

h# 0 constant VBE_DISPI_INDEX_ID
h# 1 constant VBE_DISPI_INDEX_XRES
h# 2 constant VBE_DISPI_INDEX_YRES
h# 3 constant VBE_DISPI_INDEX_BPP
h# 4 constant VBE_DISPI_INDEX_ENABLE
h# 5 constant VBE_DISPI_INDEX_BANK
h# 6 constant VBE_DISPI_INDEX_VIRT_WIDTH
h# 7 constant VBE_DISPI_INDEX_VIRT_HEIGHT
h# 8 constant VBE_DISPI_INDEX_X_OFFSET
h# 9 constant VBE_DISPI_INDEX_Y_OFFSET
h# a constant VBE_DISPI_INDEX_NB

h# 0 constant VBE_DISPI_DISABLED
h# 1 constant VBE_DISPI_ENABLED

\
\ Bochs VBE register writes
\

defer vbe-iow!

: vbe-legacy-iow!  ( val addr -- )
  h# 1ce iow!
  h# 1d0 iow!
;

: vbe-mmio-iow!  ( val addr -- )
  1 lshift h# 500 + mmio-addr + cr .s cr le-w!
;

\
\ Initialise Bochs VBE mode
\

: vbe-init  ( -- )
  h# 0 vga-addr vga-ioc!    \ Enable blanking
  VBE_DISPI_DISABLED VBE_DISPI_INDEX_ENABLE vbe-iow!
  h# 0 VBE_DISPI_INDEX_X_OFFSET vbe-iow!
  h# 0 VBE_DISPI_INDEX_Y_OFFSET vbe-iow!
  openbios-video-width VBE_DISPI_INDEX_XRES vbe-iow!
  openbios-video-height VBE_DISPI_INDEX_YRES vbe-iow!
  depth-bits VBE_DISPI_INDEX_BPP vbe-iow!
  VBE_DISPI_ENABLED VBE_DISPI_INDEX_ENABLE vbe-iow!
  h# 0 vga-addr vga-ioc!
  h# 20 vga-addr vga-ioc!   \ Disable blanking
;

\
\ PCI BAR mapping
\

: map-fb ( -- )
  cfg-bar0 pci-bar>pci-addr if   \ ( pci-addr.lo pci-addr.mid pci-addr.hi size )
    " map-in" $call-parent
    to fb-addr
  then
;

: map-mmio ( -- )
  cfg-bar2 pci-bar>pci-addr if   \ ( pci-addr.lo pci-addr.mid pci-addr.hi size )
    " map-in" $call-parent
    to mmio-addr
  then
;

: nv-map-mmio ( -- )
  nv-cfg-bar0 pci-bar>pci-addr if
    " map-in" $call-parent
    to nv-mmio-addr
  then
;

: nv-map-vram ( -- )
  nv-cfg-bar1 pci-bar>pci-addr if
    " map-in" $call-parent
    to nv-vram-addr
  then
;

\
\ NV20 CRTC (extended VGA-style index/data ports, at fixed offsets
\ within BAR0): index goes to 0x3d4, the value for that index to
\ 0x3d5, exactly like real (and this emulation's) VGA-compatible CRTC
\ access.
\

h# 6013d4 constant nv-crtc-index-addr
h# 6013d5 constant nv-crtc-data-addr

: nv-crtc! ( val index -- )
  nv-mmio-addr nv-crtc-index-addr + c!
  nv-mmio-addr nv-crtc-data-addr + c!
;

\
\ Program a fixed 640x480, 8bpp-indexed mode at VRAM offset 0, the
\ simplest mode this emulation's CRTC decode (nv_geforce3_get_mode())
\ accepts -- register values are the same ones a stock 640x480
\ standard-VGA mode already uses, since CRTC_MAX (0x18) and below is
\ modelled identically to real VGA here:
\   reg 1  (h# 4f) -- (width/8)-1        = 640/8-1  = 0x4f
\   reg 7  (h#  2) -- bit 1 = height bit 8
\   reg 18 (h# df) -- height low 8 bits  = 480-1 low8 = 0xdf
\ and NV20-specific extensions above CRTC_MAX for pitch/bpp/offset:
\   reg h# 13 -- pitch>>3    = 640>>3 = h# 50
\   reg h# 0c/h# 0d -- start-address hi/lo, 0 for offset 0
\   reg h# 19 -- pitch/offset extension bits, 0 (640/0 both fit below)
\   reg h# 28 -- bpp code: 1 = 8bpp indexed (also the "mode enabled" flag)
\   reg h# 25/h# 2d/h# 41/h# 42 -- overflow bits for >1024 sizes, 0 here
\

: nv20-init ( -- )
  h# 4f 1 nv-crtc!
  h#  2 7 nv-crtc!
  h# df h# 12 nv-crtc!
  h# 50 h# 13 nv-crtc!
  0 h# 0c nv-crtc!
  0 h# 0d nv-crtc!
  0 h# 19 nv-crtc!
  1 h# 28 nv-crtc!
  0 h# 25 nv-crtc!
  0 h# 2d nv-crtc!
  0 h# 41 nv-crtc!
  0 h# 42 nv-crtc!
;

\
\ NV20 DAC (palette) access, at its own fixed offset within BAR0.
\ Unlike vga-color!, this emulation's DAC stores full 8-bit
\ components as written -- no >>2 scaling to 6-bit VGA precision,
\ since nv_geforce3_draw_8bpp() reads them straight back out as
\ 8-bit RGB.
\

h# 6813c8 constant nv-dac-write-addr
h# 6813c9 constant nv-dac-data-addr

: nv-dac-byte! ( byte -- )
  nv-mmio-addr nv-dac-data-addr + c!
;

: nv-color! ( r g b index -- )
  nv-mmio-addr nv-dac-write-addr + c!
  >r >r
  nv-dac-byte!
  r> nv-dac-byte!
  r> nv-dac-byte!
;

\ Console text draws with palette index 0 as background and index
\ h# ff as foreground (see forth/device/display.fs); this emulation's
\ palette otherwise defaults to all-black, which would make the
\ console invisible (black on black) even with a correctly-set mode.
: nv-init-colors ( -- )
  0 0 0 0 nv-color!
  h# ff h# ff h# ff h# ff nv-color!
;

\
\ Legacy IO port or QEMU MMIO accesses
\
\ legacy: use standard VGA ioport registers
\ MMIO: use QEMU PCI MMIO VGA registers
\
\ If building for QEMU, default to MMIO access since it allows
\ programming of the VGA card regardless of its position in the
\ PCI topology
\

[IFDEF] CONFIG_QEMU
['] vga-mmio-ioc! to vga-ioc!
['] vbe-mmio-iow! to vbe-iow!
[ELSE]
['] vga-legacy-ioc! to vga-ioc!
['] vbe-legacy-iow! to vbe-iow!
[THEN]

\
\ Publically visible words
\

external

[IFDEF] CONFIG_MOL
defer mol-color!

\ Hook for MOL (see packages/molvideo.c)
\
\ Perhaps for neatness this there should be a separate molvga.fs
\ but let's leave it here for now.

: generic-color!  ( r g b index -- )
  mol-color!
;

[ELSE]

\ Standard VGA

: generic-color!  ( r g b index -- )
  vga-color!
;

[THEN]

: color!  ( r g b index -- )
  nv-geforce3? if
    nv-color!
  else
    generic-color!
  then
;

: fill-rectangle  ( color_ind x y width height -- )
  fb8-fillrect
;

: dimensions  ( -- width height )
  openbios-video-width
  openbios-video-height
;

: set-colors  ( table start count -- )
  0 do
    over dup        \ ( table start table table )
    c@ swap 1+      \ ( table start r table-g )
    dup c@ swap 1+  \ ( table start r g table-b )
    c@ 3 pick       \ ( table start r g b index )
    color!          \ ( table start )
    1+
    swap 3 + swap   \ ( table+3 start+1 )
  loop
;

\
\ Cancel Bochs VBE mode
\

: vbe-deinit ( -- )
  \ Switching VBE on and off clears the framebuffer
  VBE_DISPI_DISABLED VBE_DISPI_INDEX_ENABLE vbe-iow!
  VBE_DISPI_ENABLED VBE_DISPI_INDEX_ENABLE vbe-iow!
  VBE_DISPI_DISABLED VBE_DISPI_INDEX_ENABLE vbe-iow!
;

headerless

\
\ Installation
\

: qemu-vga-driver-install ( -- )
  mmio-addr -1 = if
    map-mmio vbe-init
  then
  fb-addr -1 = if
    map-fb fb-addr to frame-buffer-adr
    default-font set-font

    frame-buffer-adr encode-int " address" property

    openbios-video-width openbios-video-height over char-width / over char-height /
    fb8-install
  then
;

: qemu-vga-driver-init
  openbios-video-width encode-int " width" property
  openbios-video-height encode-int " height" property
  depth-bits encode-int " depth" property
  line-bytes encode-int " linebytes" property

  \ Is the VGA NDRV driver enabled? (PPC only)
  " /options" find-package drop s" vga-ndrv?" rot get-package-property not if
    decode-string 2swap 2drop    \ ( addr len )
    s" true" drop -rot comp 0= if
      \ Embed NDRV driver via fw-cfg if it exists
      " ndrv/qemu_vga.ndrv" fw-cfg-read-file if
        encode-string " driver,AAPL,MacOS,PowerPC" property
      then
    then
  then

  ['] qemu-vga-driver-install is-install
;

\
\ NV20 installation. Fixed at 640x480 8bpp-indexed -- the simplest
\ mode nv20-init's fixed register set programs -- rather than reading
\ openbios-video-width/-height, since those drive the *generic* Bochs
\ card's arbitrary -g WxHxD resolution and nv20-init doesn't compute
\ CRTC values generically. fb8-install still propagates 640/480 into
\ them itself, so setup_video()'s C-side wiring ends up consistent
\ either way.
\

: nv-geforce3-install ( -- )
  nv-mmio-addr -1 = if
    nv-map-mmio
    nv20-init
    nv-init-colors
  then
  nv-vram-addr -1 = if
    nv-map-vram nv-vram-addr to frame-buffer-adr
    default-font set-font

    frame-buffer-adr encode-int " address" property

    640 480 over char-width / over char-height /
    fb8-install
  then
;

: nv-geforce3-driver-init ( -- )
  640 encode-int " width" property
  480 encode-int " height" property
  8 encode-int " depth" property
  640 encode-int " linebytes" property

  ['] nv-geforce3-install is-install
;

nv-geforce3? if
  nv-geforce3-driver-init
else
  qemu-vga-driver-init
then

end0
