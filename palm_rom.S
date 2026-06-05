    .section .rodata.palm_rom, "a"
    .balign 4
    .global palm_rom_start
    .global palm_rom_end

palm_rom_start:
    .incbin "Palm-IIIc-4.1-en.rom"
palm_rom_end:
    .byte 0
