    .section .rodata.palm_rom, "a"
    .balign 4
    .global palm_rom_start
    .global palm_rom_end

palm_rom_start:
    .incbin "C:/Users/VRLAB083/Documents/ESP32-PALM/Palm-IIIx-3.1.rom"
palm_rom_end:
    .byte 0
