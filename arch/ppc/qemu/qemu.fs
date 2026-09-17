\   qemu specific initialization code
\
\   Copyright (C) 2005 Stefan Reinauer
\
\   This program is free software; you can redistribute it and/or
\   modify it under the terms of the GNU General Public License
\   as published by the Free Software Foundation
\


\ -------------------------------------------------------------------------
\ initialization
\ -------------------------------------------------------------------------

: make-openable ( path )
  find-dev if
    begin ?dup while
      \ install trivial open and close methods
      dup active-package! is-open
      parent
    repeat
  then
;

: preopen ( chosen-str node-path )
  2dup make-openable

  " /chosen" find-device
  open-dev ?dup if
    encode-int 2swap property
  else
    2drop
  then
;

\ preopen device nodes (and store the ihandles under /chosen)
:noname
  " rtc" " rtc" preopen
  " memory" " /memory" preopen
; SYSTEM-initializer


\ use the tty interface if available
: activate-tty-interface
  " /packages/terminal-emulator" find-dev if drop
  then
;

variable keyboard-phandle 0 keyboard-phandle !

: (find-keyboard-device) ( phandle -- )
  recursive
  keyboard-phandle @ 0= if  \ Return first match
    >dn.child @
    begin ?dup while
      dup dup " device_type" rot get-package-property 0= if
        drop dup cstrlen
        " keyboard" strcmp 0= if
          dup to keyboard-phandle
        then
      then
      (find-keyboard-device)
      >dn.peer @
    repeat
  else
    drop
  then
;

\ create the keyboard devalias 
:noname
  device-tree @ (find-keyboard-device)
  keyboard-phandle @ if
    active-package
    " /aliases" find-device
    keyboard-phandle @ get-package-path 2dup
    encode-string " kbd" property
    encode-string " keyboard" property
    active-package!  
  then
; SYSTEM-initializer

\ -------------------------------------------------------------------------
\ FCode display boot-console mode (screen-mode)
\ -------------------------------------------------------------------------
\
\ Apple's OF hands a card's FCode a 32-byte mode record on the card's own
\ node -- Mac OS's display driver saves its current mode there through
\ NVRAM -- and the ROM's open then brings the console up in that mode
\ instead of its built-in 640x480 default. We have no such NVRAM record,
\ so build one from the screen-mode config variable ("WIDTHxHEIGHT"),
\ using the properties the ROM's own probe published for the fields the
\ ROM checks it against. Each ROM has its own record and mode ids.

: (screen-mode) ( -- width height true | false )
  screen-mode dup 0= if 2drop false exit then
  ascii x left-split                          ( hstr hlen wstr wlen )
  base @ >r decimal
  $number if r> base ! 2drop false exit then  ( hstr hlen width )
  -rot $number if r> base ! drop false exit then  ( width height )
  r> base ! true
;

\ A mode table is a list of ( width height id ) cell triples ended by zeros.
: (mode-table-id) ( width height table -- id true | false )
  begin dup @ while
    dup @ 3 pick = over cell+ @ 3 pick = and if
      2 cells + @ nip nip true exit
    then
    3 cells +
  repeat
  drop 2drop false
;

\ ATI Rage 128 (ATYN)
\
\ The ROM's open (word 0x9a4 in 109-72700-136) reads "ATYN":
\   [0..1] mode id (the ROM's mode-table id, 16-bit big-endian)
\   [2..3] monitor sense code (only checked when there is no EDID)
\   [5]    depth code, h# 80 = 8bpp
\   [7]    checksum byte of the monitor's EDID (must match)
\ Sense code and EDID checksum come from the ATY,Flags and EDID
\ properties. The mode ids below are the ROM's (show-modes order:
\ 800x600@60 is mode 0, id h# 10); a mode the ROM has not enabled from
\ the EDID is refused by set-mode as "Mode disabled" and the ROM keeps
\ its default, so this is always safe.

create aty-mode-table
  d# 640  , d# 480  , 6     ,   \ 640x480@60Hz
  d# 800  , d# 600  , h# 10 ,   \ 800x600@60Hz
  d# 832  , d# 624  , h# 17 ,   \ 832x624@75Hz
  d# 1024 , d# 768  , h# 19 ,   \ 1024x768@60Hz
  d# 1152 , d# 870  , h# 21 ,   \ 1152x870@75Hz
  d# 1280 , d# 1024 , h# 24 ,   \ 1280x1024@75Hz
  0 , 0 , 0 ,

create aty-atyn-buf h# 20 allot

: (aty-console-mode) ( phandle -- )
  (screen-mode) 0= if drop exit then          ( ph width height )
  aty-mode-table (mode-table-id) 0= if drop exit then   ( ph id )
  aty-atyn-buf h# 20 0 fill
  dup 8 rshift aty-atyn-buf c! aty-atyn-buf 1+ c!
  h# 80 aty-atyn-buf 5 + c!
  " ATY,Flags" 2 pick get-package-property 0= if
    decode-int nip nip d# 16 rshift
    dup 8 rshift aty-atyn-buf 2 + c! aty-atyn-buf 3 + c!
  then
  " EDID" 2 pick get-package-property 0= if
    dup h# 80 = if + 1- c@ aty-atyn-buf 7 + c! else 2drop then
  then
  active-package swap active-package!
  aty-atyn-buf h# 20 encode-bytes " ATYN" property
  active-package!
;

\ NVIDIA GeForce3 (NVPR)
\
\ NVIDIA's ROM (1057.019, nv_oem_3_1057019_agp_64k.rom) reads "NVPR" in
\ its open (word 0x8e9, checked again by 0x98d):
\   [0]       h# 80 | h# 04 (record type is in byte 2) | depth code, 0 = 8bpp
\   [1]       mode id: the ROM's show-modes number plus one (show-modes
\             counts from 0, NVPR and my-args from 1)
\   [2]       record type, 3 = mode id (plus a custom timing when id is h# ff)
\   [3]       checksum: the sum of the other 31 bytes, modulo 256
\   [5]       log2 of the frame-buffer offset in VRAM; h# e = h# 4000,
\             the ROM's own default
\   [18..1b]  bytes 8..11 of the monitor's EDID (manufacturer and product
\             ids): the ROM discards the record unless they match what
\             its own DDC read returns
\ Without an EDID the ROM ignores the record and forces mode 1 (640x480),
\ so nothing is done when its probe published no EDID property. The
\ table below lists the lowest-refresh entry of each size in the ROM's
\ mode table (640x480@60 is show-modes 0, id 1; 800x600@60 is 5, id 6).

create nvda-mode-table
  d# 640  , d# 480  , 1     ,   \ 640x480@60Hz
  d# 800  , d# 600  , 6     ,   \ 800x600@60Hz
  d# 832  , d# 624  , d# 11 ,   \ 832x624@75Hz
  d# 1024 , d# 768  , d# 13 ,   \ 1024x768@60Hz
  d# 1152 , d# 870  , d# 19 ,   \ 1152x870@75Hz
  d# 1280 , d# 1024 , d# 20 ,   \ 1280x1024@60Hz
  d# 1600 , d# 1024 , d# 23 ,   \ 1600x1024@60Hz
  d# 1600 , d# 1200 , d# 24 ,   \ 1600x1200@60Hz
  d# 1920 , d# 1440 , d# 28 ,   \ 1920x1440@60Hz
  0 , 0 , 0 ,

create nvda-nvpr-buf h# 20 allot

: (nvda-console-mode) ( phandle -- )
  " EDID" 2 pick get-package-property if drop exit then   ( ph edid len )
  h# 80 <> if 2drop exit then                             ( ph edid )
  (screen-mode) 0= if 2drop exit then                     ( ph edid width height )
  nvda-mode-table (mode-table-id) 0= if 2drop exit then   ( ph edid id )
  nvda-nvpr-buf h# 20 0 fill
  h# 84 nvda-nvpr-buf c!
  nvda-nvpr-buf 1+ c!                                     ( ph edid )
  3 nvda-nvpr-buf 2 + c!
  h# e nvda-nvpr-buf 5 + c!
  8 + nvda-nvpr-buf h# 18 + 4 move                        ( ph )
  0 h# 20 0 do nvda-nvpr-buf i + c@ + loop
  h# ff and nvda-nvpr-buf 3 + c!
  active-package swap active-package!
  nvda-nvpr-buf h# 20 encode-bytes " NVPR" property
  active-package!
;

:noname
  0 begin " display" iterate-device-type ?dup while
    dup " ATY,Fcode" rot get-package-property 0= if
      2drop dup (aty-console-mode)
    then
    dup " NVDA,Features" rot get-package-property 0= if
      2drop dup (nvda-console-mode)
    then
  repeat
; SYSTEM-initializer

\ -------------------------------------------------------------------------
\ pre-booting
\ -------------------------------------------------------------------------

: update-chosen
  " /chosen" find-device
  stdin @ encode-int " stdin" property
  stdout @ encode-int " stdout" property
  device-end
;

:noname
  set-defaults
; PREPOST-initializer

\ -------------------------------------------------------------------------
\ Mac OF specific words
\ -------------------------------------------------------------------------

: parse-1hex 1 parse-nhex ;
: parse-2hex 2 parse-nhex ;
: parse-3hex 3 parse-nhex ;

\ -------------------------------------------------------------------------
\ copyright property handling
\ -------------------------------------------------------------------------

: insert-copyright-property
  \ As required for MacOS 9 and below
  " Pbclevtug 1983-2001 Nccyr Pbzchgre, Vap. GUVF ZRFFNTR SBE PBZCNGVOVYVGL BAYL"
  rot13-str encode-string " copyright"
  " /" find-package if
    " set-property" $find if
      execute
    else
      3drop drop
    then
  then
;

: delete-copyright-property
  \ Remove copyright property created above
  active-package
  " /" find-package if
      active-package!
      " copyright" delete-property
  then
  active-package!
;

: (exit)
  \ Clean up before returning to the interpreter
  delete-copyright-property
;

\ -------------------------------------------------------------------------
\ Adler-32 wrapper
\ -------------------------------------------------------------------------

: adler32 ( adler buf len -- checksum )
  " (adler32)" $find if
    execute
  else
    ." Can't find " ( adler32-name ) type cr
    3drop 0
  then
;
