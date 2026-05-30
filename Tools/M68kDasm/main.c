#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "m68k.h"

static unsigned char *g_bytes = NULL;
static size_t g_size = 0;

unsigned int m68k_read_disassembler_8(unsigned int address) {
    if (address >= g_size) return 0;
    return g_bytes[address];
}

unsigned int m68k_read_disassembler_16(unsigned int address) {
    return (m68k_read_disassembler_8(address) << 8) |
           m68k_read_disassembler_8(address + 1);
}

unsigned int m68k_read_disassembler_32(unsigned int address) {
    return (m68k_read_disassembler_16(address) << 16) |
           m68k_read_disassembler_16(address + 2);
}

static int read_file(const char *path) {
    FILE *f = fopen(path, "rb");
    long len;
    if (!f) {
        fprintf(stderr, "open failed: %s\n", path);
        return 0;
    }

    if (fseek(f, 0, SEEK_END) != 0) {
        fclose(f);
        return 0;
    }
    len = ftell(f);
    if (len < 0) {
        fclose(f);
        return 0;
    }
    if (fseek(f, 0, SEEK_SET) != 0) {
        fclose(f);
        return 0;
    }

    g_size = (size_t)len;
    g_bytes = (unsigned char*)malloc(g_size);
    if (!g_bytes) {
        fclose(f);
        return 0;
    }

    if (fread(g_bytes, 1, g_size, f) != g_size) {
        fclose(f);
        return 0;
    }
    fclose(f);
    return 1;
}

int main(int argc, char **argv) {
    unsigned int pc = 0;
    unsigned int end;
    if (argc < 2 || argc > 4) {
        fprintf(stderr, "usage: m68kdasm_tool file [start_hex] [length_hex]\n");
        return 2;
    }

    if (!read_file(argv[1])) {
        fprintf(stderr, "failed reading file: %s\n", argv[1]);
        return 1;
    }

    if (argc >= 3) pc = (unsigned int)strtoul(argv[2], NULL, 0);
    if (pc > g_size) pc = (unsigned int)g_size;
    if (argc >= 4) {
        unsigned int length = (unsigned int)strtoul(argv[3], NULL, 0);
        end = pc + length;
        if (end < pc || end > g_size) end = (unsigned int)g_size;
    } else {
        end = (unsigned int)g_size;
    }

    while (pc < end) {
        char text[256];
        unsigned int old_pc = pc;
        unsigned int length = m68k_disassemble(text, pc, M68K_CPU_TYPE_68000);
        unsigned int next = old_pc + length;
        if (length == 0 || next <= old_pc || next > g_size) {
            printf("%06X: dc.w $%04X\n", old_pc, m68k_read_disassembler_16(old_pc));
            pc = old_pc + 2;
        } else {
            printf("%06X: %-24s ;", old_pc, text);
            for (unsigned int i = old_pc; i < next && i < old_pc + 10; ++i) {
                printf(" %02X", g_bytes[i]);
            }
            printf("\n");
            pc = next;
        }
    }

    free(g_bytes);
    return 0;
}
