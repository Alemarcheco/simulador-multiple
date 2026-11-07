// Traducido a C# de avr8js (src/cpu/instruction.ts), licencia MIT.
// Copyright (c) Uri Shaked and contributors. Ver THIRD-PARTY-NOTICES.md.
// Se mantiene la estructura original para poder compararlo con el código de partida.
#pragma warning disable IDE0011, IDE0047, IDE0048, IDE0054, IDE0059, IDE0078, IDE0007, IDE0008, IDE0045
using System.Runtime.CompilerServices;

namespace Simulador.Arduino.Runtime.Avr;

/// <summary>Decodifica y ejecuta una instrucción AVR en la posición del contador de programa.</summary>
internal static class AvrInstruction
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool B(int x) => x != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool B(bool x) => x;

    private static bool IsTwoWordInstruction(int opcode) {
        return (
        /* LDS */
        (opcode & 0xfe0f) == 0x9000 ||
            /* STS */
            (opcode & 0xfe0f) == 0x9200 ||
            /* CALL */
            (opcode & 0xfe0e) == 0x940e ||
            /* JMP */
            (opcode & 0xfe0e) == 0x940c);
    }
    private static int Decode(int opcode)
    {
        if (B((opcode & 0xfc00) == 0x1c00)) return 0;
        if (B((opcode & 0xfc00) == 0xc00)) return 1;
        if (B((opcode & 0xff00) == 0x9600)) return 2;
        if (B((opcode & 0xfc00) == 0x2000)) return 3;
        if (B((opcode & 0xf000) == 0x7000)) return 4;
        if (B((opcode & 0xfe0f) == 0x9405)) return 5;
        if (B((opcode & 0xff8f) == 0x9488)) return 6;
        if (B((opcode & 0xfe08) == 0xf800)) return 7;
        if (B((opcode & 0xfc00) == 0xf400)) return 8;
        if (B((opcode & 0xfc00) == 0xf000)) return 9;
        if (B((opcode & 0xff8f) == 0x9408)) return 10;
        if (B((opcode & 0xfe08) == 0xfa00)) return 11;
        if (B((opcode & 0xfe0e) == 0x940e)) return 12;
        if (B((opcode & 0xff00) == 0x9800)) return 13;
        if (B((opcode & 0xfe0f) == 0x9400)) return 14;
        if (B((opcode & 0xfc00) == 0x1400)) return 15;
        if (B((opcode & 0xfc00) == 0x400)) return 16;
        if (B((opcode & 0xf000) == 0x3000)) return 17;
        if (B((opcode & 0xfc00) == 0x1000)) return 18;
        if (B((opcode & 0xfe0f) == 0x940a)) return 19;
        if (B(opcode == 0x9519)) return 20;
        if (B(opcode == 0x9419)) return 21;
        if (B(opcode == 0x95d8)) return 22;
        if (B((opcode & 0xfe0f) == 0x9006)) return 23;
        if (B((opcode & 0xfe0f) == 0x9007)) return 24;
        if (B((opcode & 0xfc00) == 0x2400)) return 25;
        if (B((opcode & 0xff88) == 0x308)) return 26;
        if (B((opcode & 0xff88) == 0x380)) return 27;
        if (B((opcode & 0xff88) == 0x388)) return 28;
        if (B(opcode == 0x9509)) return 29;
        if (B(opcode == 0x9409)) return 30;
        if (B((opcode & 0xf800) == 0xb000)) return 31;
        if (B((opcode & 0xfe0f) == 0x9403)) return 32;
        if (B((opcode & 0xfe0e) == 0x940c)) return 33;
        if (B((opcode & 0xfe0f) == 0x9206)) return 34;
        if (B((opcode & 0xfe0f) == 0x9205)) return 35;
        if (B((opcode & 0xfe0f) == 0x9207)) return 36;
        if (B((opcode & 0xf000) == 0xe000)) return 37;
        if (B((opcode & 0xfe0f) == 0x9000)) return 38;
        if (B((opcode & 0xfe0f) == 0x900c)) return 39;
        if (B((opcode & 0xfe0f) == 0x900d)) return 40;
        if (B((opcode & 0xfe0f) == 0x900e)) return 41;
        if (B((opcode & 0xfe0f) == 0x8008)) return 42;
        if (B((opcode & 0xfe0f) == 0x9009)) return 43;
        if (B((opcode & 0xfe0f) == 0x900a)) return 44;
        if (B((opcode & 0xd208) == 0x8008) && B((opcode & 7) | ((opcode & 0xc00) >> 7) | ((opcode & 0x2000) >> 8))) return 45;
        if (B((opcode & 0xfe0f) == 0x8000)) return 46;
        if (B((opcode & 0xfe0f) == 0x9001)) return 47;
        if (B((opcode & 0xfe0f) == 0x9002)) return 48;
        if (B((opcode & 0xd208) == 0x8000) && B((opcode & 7) | ((opcode & 0xc00) >> 7) | ((opcode & 0x2000) >> 8))) return 49;
        if (B(opcode == 0x95c8)) return 50;
        if (B((opcode & 0xfe0f) == 0x9004)) return 51;
        if (B((opcode & 0xfe0f) == 0x9005)) return 52;
        if (B((opcode & 0xfe0f) == 0x9406)) return 53;
        if (B((opcode & 0xfc00) == 0x2c00)) return 54;
        if (B((opcode & 0xff00) == 0x100)) return 55;
        if (B((opcode & 0xfc00) == 0x9c00)) return 56;
        if (B((opcode & 0xff00) == 0x200)) return 57;
        if (B((opcode & 0xff88) == 0x300)) return 58;
        if (B((opcode & 0xfe0f) == 0x9401)) return 59;
        if (B(opcode == 0)) return 60;
        if (B((opcode & 0xfc00) == 0x2800)) return 61;
        if (B((opcode & 0xf000) == 0x6000)) return 62;
        if (B((opcode & 0xf800) == 0xb800)) return 63;
        if (B((opcode & 0xfe0f) == 0x900f)) return 64;
        if (B((opcode & 0xfe0f) == 0x920f)) return 65;
        if (B((opcode & 0xf000) == 0xd000)) return 66;
        if (B(opcode == 0x9508)) return 67;
        if (B(opcode == 0x9518)) return 68;
        if (B((opcode & 0xf000) == 0xc000)) return 69;
        if (B((opcode & 0xfe0f) == 0x9407)) return 70;
        if (B((opcode & 0xfc00) == 0x800)) return 71;
        if (B((opcode & 0xf000) == 0x4000)) return 72;
        if (B((opcode & 0xff00) == 0x9a00)) return 73;
        if (B((opcode & 0xff00) == 0x9900)) return 74;
        if (B((opcode & 0xff00) == 0x9b00)) return 75;
        if (B((opcode & 0xff00) == 0x9700)) return 76;
        if (B((opcode & 0xfe08) == 0xfc00)) return 77;
        if (B((opcode & 0xfe08) == 0xfe00)) return 78;
        if (B(opcode == 0x9588)) return 79;
        if (B(opcode == 0x95e8)) return 80;
        if (B(opcode == 0x95f8)) return 81;
        if (B((opcode & 0xfe0f) == 0x9200)) return 82;
        if (B((opcode & 0xfe0f) == 0x920c)) return 83;
        if (B((opcode & 0xfe0f) == 0x920d)) return 84;
        if (B((opcode & 0xfe0f) == 0x920e)) return 85;
        if (B((opcode & 0xfe0f) == 0x8208)) return 86;
        if (B((opcode & 0xfe0f) == 0x9209)) return 87;
        if (B((opcode & 0xfe0f) == 0x920a)) return 88;
        if (B((opcode & 0xd208) == 0x8208) && B((opcode & 7) | ((opcode & 0xc00) >> 7) | ((opcode & 0x2000) >> 8))) return 89;
        if (B((opcode & 0xfe0f) == 0x8200)) return 90;
        if (B((opcode & 0xfe0f) == 0x9201)) return 91;
        if (B((opcode & 0xfe0f) == 0x9202)) return 92;
        if (B((opcode & 0xd208) == 0x8200) && B((opcode & 7) | ((opcode & 0xc00) >> 7) | ((opcode & 0x2000) >> 8))) return 93;
        if (B((opcode & 0xfc00) == 0x1800)) return 94;
        if (B((opcode & 0xf000) == 0x5000)) return 95;
        if (B((opcode & 0xfe0f) == 0x9402)) return 96;
        if (B(opcode == 0x95a8)) return 97;
        if (B((opcode & 0xfe0f) == 0x9204)) return 98;
        return -1;
    }

    /// <summary>Instrucción que corresponde a cada código de operación (se calcula una vez).</summary>
    private static readonly short[] Table = BuildTable();

    private static short[] BuildTable()
    {
        var t = new short[65536];
        for (int op = 0; op < 65536; op++) t[op] = (short)Decode(op);
        return t;
    }

    public static void Execute(AvrCpu cpu)
    {
        int opcode = cpu.Prog[cpu.PC];
        switch (Table[opcode])
        {
            case 0:
            {
                /* ADC, 0001 11rd dddd rrrr */
                int d = cpu.Data[(opcode & 0x1f0) >> 4];
                int r = cpu.Data[(opcode & 0xf) | ((opcode & 0x200) >> 5)];
                int sum = d + r + (cpu.Data[95] & 1);
                int R = sum & 255;
                cpu.Data[(opcode & 0x1f0) >> 4] = R;
                int sreg = cpu.Data[95] & 0xc0;
                sreg |= B(R) ? 0 : 2;
                sreg |= B(128 & R) ? 4 : 0;
                sreg |= B((R ^ r) & (d ^ R) & 128) ? 8 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ ((sreg >> 3) & 1)) ? 0x10 : 0;
                sreg |= B(sum & 256) ? 1 : 0;
                sreg |= B(1 & ((d & r) | (r & ~R) | (~R & d))) ? 0x20 : 0;
                cpu.Data[95] = sreg;
                break;
            }
            case 1:
            {
                /* ADD, 0000 11rd dddd rrrr */
                int d = cpu.Data[(opcode & 0x1f0) >> 4];
                int r = cpu.Data[(opcode & 0xf) | ((opcode & 0x200) >> 5)];
                int R = (d + r) & 255;
                cpu.Data[(opcode & 0x1f0) >> 4] = R;
                int sreg = cpu.Data[95] & 0xc0;
                sreg |= B(R) ? 0 : 2;
                sreg |= B(128 & R) ? 4 : 0;
                sreg |= B((R ^ r) & (R ^ d) & 128) ? 8 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ ((sreg >> 3) & 1)) ? 0x10 : 0;
                sreg |= B((d + r) & 256) ? 1 : 0;
                sreg |= B(1 & ((d & r) | (r & ~R) | (~R & d))) ? 0x20 : 0;
                cpu.Data[95] = sreg;
                break;
            }
            case 2:
            {
                /* ADIW, 1001 0110 KKdd KKKK */
                int addr = 2 * ((opcode & 0x30) >> 4) + 24;
                int value = cpu.Get16(addr);
                int R = (value + ((opcode & 0xf) | ((opcode & 0xc0) >> 2))) & 0xffff;
                cpu.Set16(addr, R);
                int sreg = cpu.Data[95] & 0xe0;
                sreg |= B(R) ? 0 : 2;
                sreg |= B(0x8000 & R) ? 4 : 0;
                sreg |= B(~value & R & 0x8000) ? 8 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ ((sreg >> 3) & 1)) ? 0x10 : 0;
                sreg |= B(~R & value & 0x8000) ? 1 : 0;
                cpu.Data[95] = sreg;
                cpu.Cycles++;
                break;
            }
            case 3:
            {
                /* AND, 0010 00rd dddd rrrr */
                int R = cpu.Data[(opcode & 0x1f0) >> 4] & cpu.Data[(opcode & 0xf) | ((opcode & 0x200) >> 5)];
                cpu.Data[(opcode & 0x1f0) >> 4] = R;
                int sreg = cpu.Data[95] & 0xe1;
                sreg |= B(R) ? 0 : 2;
                sreg |= B(128 & R) ? 4 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ ((sreg >> 3) & 1)) ? 0x10 : 0;
                cpu.Data[95] = sreg;
                break;
            }
            case 4:
            {
                /* ANDI, 0111 KKKK dddd KKKK */
                int R = cpu.Data[((opcode & 0xf0) >> 4) + 16] & ((opcode & 0xf) | ((opcode & 0xf00) >> 4));
                cpu.Data[((opcode & 0xf0) >> 4) + 16] = R;
                int sreg = cpu.Data[95] & 0xe1;
                sreg |= B(R) ? 0 : 2;
                sreg |= B(128 & R) ? 4 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ ((sreg >> 3) & 1)) ? 0x10 : 0;
                cpu.Data[95] = sreg;
                break;
            }
            case 5:
            {
                /* ASR, 1001 010d dddd 0101 */
                int value = cpu.Data[(opcode & 0x1f0) >> 4];
                int R = (value >> 1) | (128 & value);
                cpu.Data[(opcode & 0x1f0) >> 4] = R;
                int sreg = cpu.Data[95] & 0xe0;
                sreg |= B(R) ? 0 : 2;
                sreg |= B(128 & R) ? 4 : 0;
                sreg |= value & 1;
                sreg |= B(((sreg >> 2) & 1) ^ (sreg & 1)) ? 8 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ ((sreg >> 3) & 1)) ? 0x10 : 0;
                cpu.Data[95] = sreg;
                break;
            }
            case 6:
            {
                /* BCLR, 1001 0100 1sss 1000 */
                cpu.Data[95] &= ~(1 << ((opcode & 0x70) >> 4));
                break;
            }
            case 7:
            {
                /* BLD, 1111 100d dddd 0bbb */
                int b = opcode & 7;
                int d = (opcode & 0x1f0) >> 4;
                cpu.Data[d] = (~(1 << b) & cpu.Data[d]) | (((cpu.Data[95] >> 6) & 1) << b);
                break;
            }
            case 8:
            {
                /* BRBC, 1111 01kk kkkk ksss */
                if (B(!B(cpu.Data[95] & (1 << (opcode & 7))))) {
                    cpu.PC = cpu.PC + (((opcode & 0x1f8) >> 3) - (B(opcode & 0x200) ? 0x40 : 0));
                    cpu.Cycles++;
                }
                break;
            }
            case 9:
            {
                /* BRBS, 1111 00kk kkkk ksss */
                if (B(cpu.Data[95] & (1 << (opcode & 7)))) {
                    cpu.PC = cpu.PC + (((opcode & 0x1f8) >> 3) - (B(opcode & 0x200) ? 0x40 : 0));
                    cpu.Cycles++;
                }
                break;
            }
            case 10:
            {
                /* BSET, 1001 0100 0sss 1000 */
                cpu.Data[95] |= 1 << ((opcode & 0x70) >> 4);
                break;
            }
            case 11:
            {
                /* BST, 1111 101d dddd 0bbb */
                int d = cpu.Data[(opcode & 0x1f0) >> 4];
                int b = opcode & 7;
                cpu.Data[95] = (cpu.Data[95] & 0xbf) | (B((d >> b) & 1) ? 0x40 : 0);
                break;
            }
            case 12:
            {
                /* CALL, 1001 010k kkkk 111k kkkk kkkk kkkk kkkk */
                int k = cpu.Prog[cpu.PC + 1] | ((opcode & 1) << 16) | ((opcode & 0x1f0) << 13);
                int ret = cpu.PC + 2;
                int sp = cpu.Get16(93);
                bool pc22Bits = cpu.Pc22Bits;
                cpu.Data[sp] = 255 & ret;
                cpu.Data[sp - 1] = (ret >> 8) & 255;
                if (B(pc22Bits)) {
                    cpu.Data[sp - 2] = (ret >> 16) & 255;
                }
                cpu.Set16(93, sp - (B(pc22Bits) ? 3 : 2));
                cpu.PC = k - 1;
                cpu.Cycles += B(pc22Bits) ? 4 : 3;
                break;
            }
            case 13:
            {
                /* CBI, 1001 1000 AAAA Abbb */
                int A = opcode & 0xf8;
                int b = opcode & 7;
                int R = cpu.ReadData((A >> 3) + 32);
                int mask = 1 << b;
                cpu.WriteData((A >> 3) + 32, R & ~mask, mask);
                break;
            }
            case 14:
            {
                /* COM, 1001 010d dddd 0000 */
                int d = (opcode & 0x1f0) >> 4;
                int R = 255 - cpu.Data[d];
                cpu.Data[d] = R;
                int sreg = (cpu.Data[95] & 0xe1) | 1;
                sreg |= B(R) ? 0 : 2;
                sreg |= B(128 & R) ? 4 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ ((sreg >> 3) & 1)) ? 0x10 : 0;
                cpu.Data[95] = sreg;
                break;
            }
            case 15:
            {
                /* CP, 0001 01rd dddd rrrr */
                int val1 = cpu.Data[(opcode & 0x1f0) >> 4];
                int val2 = cpu.Data[(opcode & 0xf) | ((opcode & 0x200) >> 5)];
                int R = val1 - val2;
                int sreg = cpu.Data[95] & 0xc0;
                sreg |= B(R) ? 0 : 2;
                sreg |= B(128 & R) ? 4 : 0;
                sreg |= B(0 != ((val1 ^ val2) & (val1 ^ R) & 128)) ? 8 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ ((sreg >> 3) & 1)) ? 0x10 : 0;
                sreg |= B(val2 > val1) ? 1 : 0;
                sreg |= B(1 & ((~val1 & val2) | (val2 & R) | (R & ~val1))) ? 0x20 : 0;
                cpu.Data[95] = sreg;
                break;
            }
            case 16:
            {
                /* CPC, 0000 01rd dddd rrrr */
                int arg1 = cpu.Data[(opcode & 0x1f0) >> 4];
                int arg2 = cpu.Data[(opcode & 0xf) | ((opcode & 0x200) >> 5)];
                int sreg = cpu.Data[95];
                int r = arg1 - arg2 - (sreg & 1);
                sreg = (sreg & 0xc0) | (!B(r) && B((sreg >> 1) & 1) ? 2 : 0) | (B(arg2 + (sreg & 1) > arg1) ? 1 : 0);
                sreg |= B(128 & r) ? 4 : 0;
                sreg |= B((arg1 ^ arg2) & (arg1 ^ r) & 128) ? 8 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ ((sreg >> 3) & 1)) ? 0x10 : 0;
                sreg |= B(1 & ((~arg1 & arg2) | (arg2 & r) | (r & ~arg1))) ? 0x20 : 0;
                cpu.Data[95] = sreg;
                break;
            }
            case 17:
            {
                /* CPI, 0011 KKKK dddd KKKK */
                int arg1 = cpu.Data[((opcode & 0xf0) >> 4) + 16];
                int arg2 = (opcode & 0xf) | ((opcode & 0xf00) >> 4);
                int r = arg1 - arg2;
                int sreg = cpu.Data[95] & 0xc0;
                sreg |= B(r) ? 0 : 2;
                sreg |= B(128 & r) ? 4 : 0;
                sreg |= B((arg1 ^ arg2) & (arg1 ^ r) & 128) ? 8 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ ((sreg >> 3) & 1)) ? 0x10 : 0;
                sreg |= B(arg2 > arg1) ? 1 : 0;
                sreg |= B(1 & ((~arg1 & arg2) | (arg2 & r) | (r & ~arg1))) ? 0x20 : 0;
                cpu.Data[95] = sreg;
                break;
            }
            case 18:
            {
                /* CPSE, 0001 00rd dddd rrrr */
                if (B(cpu.Data[(opcode & 0x1f0) >> 4] == cpu.Data[(opcode & 0xf) | ((opcode & 0x200) >> 5)])) {
                    int nextOpcode = cpu.Prog[cpu.PC + 1];
                    int skipSize = B(IsTwoWordInstruction(nextOpcode)) ? 2 : 1;
                    cpu.PC += skipSize;
                    cpu.Cycles += skipSize;
                }
                break;
            }
            case 19:
            {
                /* DEC, 1001 010d dddd 1010 */
                int value = cpu.Data[(opcode & 0x1f0) >> 4];
                int R = value - 1;
                cpu.Data[(opcode & 0x1f0) >> 4] = R;
                int sreg = cpu.Data[95] & 0xe1;
                sreg |= B(R) ? 0 : 2;
                sreg |= B(128 & R) ? 4 : 0;
                sreg |= B(128 == value) ? 8 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ ((sreg >> 3) & 1)) ? 0x10 : 0;
                cpu.Data[95] = sreg;
                break;
            }
            case 20:
            {
                /* EICALL, 1001 0101 0001 1001 */
                int retAddr = cpu.PC + 1;
                int sp = cpu.Get16(93);
                int eind = cpu.Data[0x5c];
                cpu.Data[sp] = retAddr & 255;
                cpu.Data[sp - 1] = (retAddr >> 8) & 255;
                cpu.Data[sp - 2] = (retAddr >> 16) & 255;
                cpu.Set16(93, sp - 3);
                cpu.PC = ((eind << 16) | cpu.Get16(30)) - 1;
                cpu.Cycles += 3;
                break;
            }
            case 21:
            {
                /* EIJMP, 1001 0100 0001 1001 */
                int eind = cpu.Data[0x5c];
                cpu.PC = ((eind << 16) | cpu.Get16(30)) - 1;
                cpu.Cycles++;
                break;
            }
            case 22:
            {
                /* ELPM, 1001 0101 1101 1000 */
                int rampz = cpu.Data[0x5b];
                cpu.Data[0] = cpu.ProgByte((rampz << 16) | cpu.Get16(30));
                cpu.Cycles += 2;
                break;
            }
            case 23:
            {
                /* ELPM(REG), 1001 000d dddd 0110 */
                int rampz = cpu.Data[0x5b];
                cpu.Data[(opcode & 0x1f0) >> 4] =
                    cpu.ProgByte((rampz << 16) | cpu.Get16(30));
                cpu.Cycles += 2;
                break;
            }
            case 24:
            {
                /* ELPM(INC), 1001 000d dddd 0111 */
                int rampz = cpu.Data[0x5b];
                int i = cpu.Get16(30);
                cpu.Data[(opcode & 0x1f0) >> 4] = cpu.ProgByte((rampz << 16) | i);
                cpu.Set16(30, i + 1);
                if (B(i == 0xffff)) {
                    cpu.Data[0x5b] = (rampz + 1) % (cpu.ProgBytesLength >> 16);
                }
                cpu.Cycles += 2;
                break;
            }
            case 25:
            {
                /* EOR, 0010 01rd dddd rrrr */
                int R = cpu.Data[(opcode & 0x1f0) >> 4] ^ cpu.Data[(opcode & 0xf) | ((opcode & 0x200) >> 5)];
                cpu.Data[(opcode & 0x1f0) >> 4] = R;
                int sreg = cpu.Data[95] & 0xe1;
                sreg |= B(R) ? 0 : 2;
                sreg |= B(128 & R) ? 4 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ ((sreg >> 3) & 1)) ? 0x10 : 0;
                cpu.Data[95] = sreg;
                break;
            }
            case 26:
            {
                /* FMUL, 0000 0011 0ddd 1rrr */
                int v1 = cpu.Data[((opcode & 0x70) >> 4) + 16];
                int v2 = cpu.Data[(opcode & 7) + 16];
                int R = (v1 * v2) << 1;
                cpu.Set16(0, R);
                cpu.Data[95] = (cpu.Data[95] & 0xfc) | (B(0xffff & R) ? 0 : 2) | (B((v1 * v2) & 0x8000) ? 1 : 0);
                cpu.Cycles++;
                break;
            }
            case 27:
            {
                /* FMULS, 0000 0011 1ddd 0rrr */
                int v1 = cpu.GetInt8(((opcode & 0x70) >> 4) + 16);
                int v2 = cpu.GetInt8((opcode & 7) + 16);
                int R = (v1 * v2) << 1;
                cpu.Set16(0, R);
                cpu.Data[95] = (cpu.Data[95] & 0xfc) | (B(0xffff & R) ? 0 : 2) | (B((v1 * v2) & 0x8000) ? 1 : 0);
                cpu.Cycles++;
                break;
            }
            case 28:
            {
                /* FMULSU, 0000 0011 1ddd 1rrr */
                int v1 = cpu.GetInt8(((opcode & 0x70) >> 4) + 16);
                int v2 = cpu.Data[(opcode & 7) + 16];
                int R = (v1 * v2) << 1;
                cpu.Set16(0, R);
                cpu.Data[95] = (cpu.Data[95] & 0xfc) | (B(0xffff & R) ? 2 : 0) | (B((v1 * v2) & 0x8000) ? 1 : 0);
                cpu.Cycles++;
                break;
            }
            case 29:
            {
                /* ICALL, 1001 0101 0000 1001 */
                int retAddr = cpu.PC + 1;
                int sp = cpu.Get16(93);
                bool pc22Bits = cpu.Pc22Bits;
                cpu.Data[sp] = retAddr & 255;
                cpu.Data[sp - 1] = (retAddr >> 8) & 255;
                if (B(pc22Bits)) {
                    cpu.Data[sp - 2] = (retAddr >> 16) & 255;
                }
                cpu.Set16(93, sp - (B(pc22Bits) ? 3 : 2));
                cpu.PC = cpu.Get16(30) - 1;
                cpu.Cycles += B(pc22Bits) ? 3 : 2;
                break;
            }
            case 30:
            {
                /* IJMP, 1001 0100 0000 1001 */
                cpu.PC = cpu.Get16(30) - 1;
                cpu.Cycles++;
                break;
            }
            case 31:
            {
                /* IN, 1011 0AAd dddd AAAA */
                int i = cpu.ReadData(((opcode & 0xf) | ((opcode & 0x600) >> 5)) + 32);
                cpu.Data[(opcode & 0x1f0) >> 4] = i;
                break;
            }
            case 32:
            {
                /* INC, 1001 010d dddd 0011 */
                int d = cpu.Data[(opcode & 0x1f0) >> 4];
                int r = (d + 1) & 255;
                cpu.Data[(opcode & 0x1f0) >> 4] = r;
                int sreg = cpu.Data[95] & 0xe1;
                sreg |= B(r) ? 0 : 2;
                sreg |= B(128 & r) ? 4 : 0;
                sreg |= B(127 == d) ? 8 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ ((sreg >> 3) & 1)) ? 0x10 : 0;
                cpu.Data[95] = sreg;
                break;
            }
            case 33:
            {
                /* JMP, 1001 010k kkkk 110k kkkk kkkk kkkk kkkk */
                cpu.PC = (cpu.Prog[cpu.PC + 1] | ((opcode & 1) << 16) | ((opcode & 0x1f0) << 13)) - 1;
                cpu.Cycles += 2;
                break;
            }
            case 34:
            {
                /* LAC, 1001 001r rrrr 0110 */
                int r = (opcode & 0x1f0) >> 4;
                int clear = cpu.Data[r];
                int value = cpu.ReadData(cpu.Get16(30));
                cpu.WriteData(cpu.Get16(30), value & (255 - clear));
                cpu.Data[r] = value;
                break;
            }
            case 35:
            {
                /* LAS, 1001 001r rrrr 0101 */
                int r = (opcode & 0x1f0) >> 4;
                int set = cpu.Data[r];
                int value = cpu.ReadData(cpu.Get16(30));
                cpu.WriteData(cpu.Get16(30), value | set);
                cpu.Data[r] = value;
                break;
            }
            case 36:
            {
                /* LAT, 1001 001r rrrr 0111 */
                int r = cpu.Data[(opcode & 0x1f0) >> 4];
                int R = cpu.ReadData(cpu.Get16(30));
                cpu.WriteData(cpu.Get16(30), r ^ R);
                cpu.Data[(opcode & 0x1f0) >> 4] = R;
                break;
            }
            case 37:
            {
                /* LDI, 1110 KKKK dddd KKKK */
                cpu.Data[((opcode & 0xf0) >> 4) + 16] = (opcode & 0xf) | ((opcode & 0xf00) >> 4);
                break;
            }
            case 38:
            {
                /* LDS, 1001 000d dddd 0000 kkkk kkkk kkkk kkkk */
                cpu.Cycles++;
                int value = cpu.ReadData(cpu.Prog[cpu.PC + 1]);
                cpu.Data[(opcode & 0x1f0) >> 4] = value;
                cpu.PC++;
                break;
            }
            case 39:
            {
                /* LDX, 1001 000d dddd 1100 */
                cpu.Cycles++;
                cpu.Data[(opcode & 0x1f0) >> 4] = cpu.ReadData(cpu.Get16(26));
                break;
            }
            case 40:
            {
                /* LDX(INC), 1001 000d dddd 1101 */
                int x = cpu.Get16(26);
                cpu.Cycles++;
                cpu.Data[(opcode & 0x1f0) >> 4] = cpu.ReadData(x);
                cpu.Set16(26, x + 1);
                break;
            }
            case 41:
            {
                /* LDX(DEC), 1001 000d dddd 1110 */
                int x = cpu.Get16(26) - 1;
                cpu.Set16(26, x);
                cpu.Cycles++;
                cpu.Data[(opcode & 0x1f0) >> 4] = cpu.ReadData(x);
                break;
            }
            case 42:
            {
                /* LDY, 1000 000d dddd 1000 */
                cpu.Cycles++;
                cpu.Data[(opcode & 0x1f0) >> 4] = cpu.ReadData(cpu.Get16(28));
                break;
            }
            case 43:
            {
                /* LDY(INC), 1001 000d dddd 1001 */
                int y = cpu.Get16(28);
                cpu.Cycles++;
                cpu.Data[(opcode & 0x1f0) >> 4] = cpu.ReadData(y);
                cpu.Set16(28, y + 1);
                break;
            }
            case 44:
            {
                /* LDY(DEC), 1001 000d dddd 1010 */
                int y = cpu.Get16(28) - 1;
                cpu.Set16(28, y);
                cpu.Cycles++;
                cpu.Data[(opcode & 0x1f0) >> 4] = cpu.ReadData(y);
                break;
            }
            case 45:
            {
                /* LDDY, 10q0 qq0d dddd 1qqq */
                cpu.Cycles++;
                cpu.Data[(opcode & 0x1f0) >> 4] = cpu.ReadData(cpu.Get16(28) +
                    ((opcode & 7) | ((opcode & 0xc00) >> 7) | ((opcode & 0x2000) >> 8)));
                break;
            }
            case 46:
            {
                /* LDZ, 1000 000d dddd 0000 */
                cpu.Cycles++;
                cpu.Data[(opcode & 0x1f0) >> 4] = cpu.ReadData(cpu.Get16(30));
                break;
            }
            case 47:
            {
                /* LDZ(INC), 1001 000d dddd 0001 */
                int z = cpu.Get16(30);
                cpu.Cycles++;
                cpu.Data[(opcode & 0x1f0) >> 4] = cpu.ReadData(z);
                cpu.Set16(30, z + 1);
                break;
            }
            case 48:
            {
                /* LDZ(DEC), 1001 000d dddd 0010 */
                int z = cpu.Get16(30) - 1;
                cpu.Set16(30, z);
                cpu.Cycles++;
                cpu.Data[(opcode & 0x1f0) >> 4] = cpu.ReadData(z);
                break;
            }
            case 49:
            {
                /* LDDZ, 10q0 qq0d dddd 0qqq */
                cpu.Cycles++;
                cpu.Data[(opcode & 0x1f0) >> 4] = cpu.ReadData(cpu.Get16(30) +
                    ((opcode & 7) | ((opcode & 0xc00) >> 7) | ((opcode & 0x2000) >> 8)));
                break;
            }
            case 50:
            {
                /* LPM, 1001 0101 1100 1000 */
                cpu.Data[0] = cpu.ProgByte(cpu.Get16(30));
                cpu.Cycles += 2;
                break;
            }
            case 51:
            {
                /* LPM(REG), 1001 000d dddd 0100 */
                cpu.Data[(opcode & 0x1f0) >> 4] = cpu.ProgByte(cpu.Get16(30));
                cpu.Cycles += 2;
                break;
            }
            case 52:
            {
                /* LPM(INC), 1001 000d dddd 0101 */
                int i = cpu.Get16(30);
                cpu.Data[(opcode & 0x1f0) >> 4] = cpu.ProgByte(i);
                cpu.Set16(30, i + 1);
                cpu.Cycles += 2;
                break;
            }
            case 53:
            {
                /* LSR, 1001 010d dddd 0110 */
                int value = cpu.Data[(opcode & 0x1f0) >> 4];
                int R = value >> 1;
                cpu.Data[(opcode & 0x1f0) >> 4] = R;
                int sreg = cpu.Data[95] & 0xe0;
                sreg |= B(R) ? 0 : 2;
                sreg |= value & 1;
                sreg |= B(((sreg >> 2) & 1) ^ (sreg & 1)) ? 8 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ ((sreg >> 3) & 1)) ? 0x10 : 0;
                cpu.Data[95] = sreg;
                break;
            }
            case 54:
            {
                /* MOV, 0010 11rd dddd rrrr */
                cpu.Data[(opcode & 0x1f0) >> 4] = cpu.Data[(opcode & 0xf) | ((opcode & 0x200) >> 5)];
                break;
            }
            case 55:
            {
                /* MOVW, 0000 0001 dddd rrrr */
                int r2 = 2 * (opcode & 0xf);
                int d2 = 2 * ((opcode & 0xf0) >> 4);
                cpu.Data[d2] = cpu.Data[r2];
                cpu.Data[d2 + 1] = cpu.Data[r2 + 1];
                break;
            }
            case 56:
            {
                /* MUL, 1001 11rd dddd rrrr */
                int R = cpu.Data[(opcode & 0x1f0) >> 4] * cpu.Data[(opcode & 0xf) | ((opcode & 0x200) >> 5)];
                cpu.Set16(0, R);
                cpu.Data[95] = (cpu.Data[95] & 0xfc) | (B(0xffff & R) ? 0 : 2) | (B(0x8000 & R) ? 1 : 0);
                cpu.Cycles++;
                break;
            }
            case 57:
            {
                /* MULS, 0000 0010 dddd rrrr */
                int R = cpu.GetInt8(((opcode & 0xf0) >> 4) + 16) * cpu.GetInt8((opcode & 0xf) + 16);
                cpu.Set16(0, R);
                cpu.Data[95] = (cpu.Data[95] & 0xfc) | (B(0xffff & R) ? 0 : 2) | (B(0x8000 & R) ? 1 : 0);
                cpu.Cycles++;
                break;
            }
            case 58:
            {
                /* MULSU, 0000 0011 0ddd 0rrr */
                int R = cpu.GetInt8(((opcode & 0x70) >> 4) + 16) * cpu.Data[(opcode & 7) + 16];
                cpu.Set16(0, R);
                cpu.Data[95] = (cpu.Data[95] & 0xfc) | (B(0xffff & R) ? 0 : 2) | (B(0x8000 & R) ? 1 : 0);
                cpu.Cycles++;
                break;
            }
            case 59:
            {
                /* NEG, 1001 010d dddd 0001 */
                int d = (opcode & 0x1f0) >> 4;
                int value = cpu.Data[d];
                int R = 0 - value;
                cpu.Data[d] = R;
                int sreg = cpu.Data[95] & 0xc0;
                sreg |= B(R) ? 0 : 2;
                sreg |= B(128 & R) ? 4 : 0;
                sreg |= B(128 == R) ? 8 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ ((sreg >> 3) & 1)) ? 0x10 : 0;
                sreg |= B(R) ? 1 : 0;
                sreg |= B(1 & (R | value)) ? 0x20 : 0;
                cpu.Data[95] = sreg;
                break;
            }
            case 60:
            {
                /* NOP, 0000 0000 0000 0000 */
                /* NOP */
                break;
            }
            case 61:
            {
                /* OR, 0010 10rd dddd rrrr */
                int R = cpu.Data[(opcode & 0x1f0) >> 4] | cpu.Data[(opcode & 0xf) | ((opcode & 0x200) >> 5)];
                cpu.Data[(opcode & 0x1f0) >> 4] = R;
                int sreg = cpu.Data[95] & 0xe1;
                sreg |= B(R) ? 0 : 2;
                sreg |= B(128 & R) ? 4 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ ((sreg >> 3) & 1)) ? 0x10 : 0;
                cpu.Data[95] = sreg;
                break;
            }
            case 62:
            {
                /* SBR, 0110 KKKK dddd KKKK */
                int R = cpu.Data[((opcode & 0xf0) >> 4) + 16] | ((opcode & 0xf) | ((opcode & 0xf00) >> 4));
                cpu.Data[((opcode & 0xf0) >> 4) + 16] = R;
                int sreg = cpu.Data[95] & 0xe1;
                sreg |= B(R) ? 0 : 2;
                sreg |= B(128 & R) ? 4 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ ((sreg >> 3) & 1)) ? 0x10 : 0;
                cpu.Data[95] = sreg;
                break;
            }
            case 63:
            {
                /* OUT, 1011 1AAr rrrr AAAA */
                cpu.WriteData(((opcode & 0xf) | ((opcode & 0x600) >> 5)) + 32, cpu.Data[(opcode & 0x1f0) >> 4]);
                break;
            }
            case 64:
            {
                /* POP, 1001 000d dddd 1111 */
                int value = cpu.Get16(93) + 1;
                cpu.Set16(93, value);
                cpu.Data[(opcode & 0x1f0) >> 4] = cpu.Data[value];
                cpu.Cycles++;
                break;
            }
            case 65:
            {
                /* PUSH, 1001 001d dddd 1111 */
                int value = cpu.Get16(93);
                cpu.Data[value] = cpu.Data[(opcode & 0x1f0) >> 4];
                cpu.Set16(93, value - 1);
                cpu.Cycles++;
                break;
            }
            case 66:
            {
                /* RCALL, 1101 kkkk kkkk kkkk */
                int k = (opcode & 0x7ff) - (B(opcode & 0x800) ? 0x800 : 0);
                int retAddr = cpu.PC + 1;
                int sp = cpu.Get16(93);
                bool pc22Bits = cpu.Pc22Bits;
                cpu.Data[sp] = 255 & retAddr;
                cpu.Data[sp - 1] = (retAddr >> 8) & 255;
                if (B(pc22Bits)) {
                    cpu.Data[sp - 2] = (retAddr >> 16) & 255;
                }
                cpu.Set16(93, sp - (B(pc22Bits) ? 3 : 2));
                cpu.PC += k;
                cpu.Cycles += B(pc22Bits) ? 3 : 2;
                break;
            }
            case 67:
            {
                /* RET, 1001 0101 0000 1000 */
                bool pc22Bits = cpu.Pc22Bits;
                int i = cpu.Get16(93) + (B(pc22Bits) ? 3 : 2);
                cpu.Set16(93, i);
                cpu.PC = (cpu.Data[i - 1] << 8) + cpu.Data[i] - 1;
                if (B(pc22Bits)) {
                    cpu.PC |= cpu.Data[i - 2] << 16;
                }
                cpu.Cycles += B(pc22Bits) ? 4 : 3;
                break;
            }
            case 68:
            {
                /* RETI, 1001 0101 0001 1000 */
                bool pc22Bits = cpu.Pc22Bits;
                int i = cpu.Get16(93) + (B(pc22Bits) ? 3 : 2);
                cpu.Set16(93, i);
                cpu.PC = (cpu.Data[i - 1] << 8) + cpu.Data[i] - 1;
                if (B(pc22Bits)) {
                    cpu.PC |= cpu.Data[i - 2] << 16;
                }
                cpu.Cycles += B(pc22Bits) ? 4 : 3;
                cpu.Data[95] |= 0x80; // Enable interrupts
                break;
            }
            case 69:
            {
                /* RJMP, 1100 kkkk kkkk kkkk */
                cpu.PC = cpu.PC + ((opcode & 0x7ff) - (B(opcode & 0x800) ? 0x800 : 0));
                cpu.Cycles++;
                break;
            }
            case 70:
            {
                /* ROR, 1001 010d dddd 0111 */
                int d = cpu.Data[(opcode & 0x1f0) >> 4];
                int r = (d >> 1) | ((cpu.Data[95] & 1) << 7);
                cpu.Data[(opcode & 0x1f0) >> 4] = r;
                int sreg = cpu.Data[95] & 0xe0;
                sreg |= B(r) ? 0 : 2;
                sreg |= B(128 & r) ? 4 : 0;
                sreg |= B(1 & d) ? 1 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ (sreg & 1)) ? 8 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ ((sreg >> 3) & 1)) ? 0x10 : 0;
                cpu.Data[95] = sreg;
                break;
            }
            case 71:
            {
                /* SBC, 0000 10rd dddd rrrr */
                int val1 = cpu.Data[(opcode & 0x1f0) >> 4];
                int val2 = cpu.Data[(opcode & 0xf) | ((opcode & 0x200) >> 5)];
                int sreg = cpu.Data[95];
                int R = val1 - val2 - (sreg & 1);
                cpu.Data[(opcode & 0x1f0) >> 4] = R;
                sreg = (sreg & 0xc0) | (!B(R) && B((sreg >> 1) & 1) ? 2 : 0) | (B(val2 + (sreg & 1) > val1) ? 1 : 0);
                sreg |= B(128 & R) ? 4 : 0;
                sreg |= B((val1 ^ val2) & (val1 ^ R) & 128) ? 8 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ ((sreg >> 3) & 1)) ? 0x10 : 0;
                sreg |= B(1 & ((~val1 & val2) | (val2 & R) | (R & ~val1))) ? 0x20 : 0;
                cpu.Data[95] = sreg;
                break;
            }
            case 72:
            {
                /* SBCI, 0100 KKKK dddd KKKK */
                int val1 = cpu.Data[((opcode & 0xf0) >> 4) + 16];
                int val2 = (opcode & 0xf) | ((opcode & 0xf00) >> 4);
                int sreg = cpu.Data[95];
                int R = val1 - val2 - (sreg & 1);
                cpu.Data[((opcode & 0xf0) >> 4) + 16] = R;
                sreg = (sreg & 0xc0) | (!B(R) && B((sreg >> 1) & 1) ? 2 : 0) | (B(val2 + (sreg & 1) > val1) ? 1 : 0);
                sreg |= B(128 & R) ? 4 : 0;
                sreg |= B((val1 ^ val2) & (val1 ^ R) & 128) ? 8 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ ((sreg >> 3) & 1)) ? 0x10 : 0;
                sreg |= B(1 & ((~val1 & val2) | (val2 & R) | (R & ~val1))) ? 0x20 : 0;
                cpu.Data[95] = sreg;
                break;
            }
            case 73:
            {
                /* SBI, 1001 1010 AAAA Abbb */
                int target = ((opcode & 0xf8) >> 3) + 32;
                int mask = 1 << (opcode & 7);
                cpu.WriteData(target, cpu.ReadData(target) | mask, mask);
                cpu.Cycles++;
                break;
            }
            case 74:
            {
                /* SBIC, 1001 1001 AAAA Abbb */
                int value = cpu.ReadData(((opcode & 0xf8) >> 3) + 32);
                if (B(!B(value & (1 << (opcode & 7))))) {
                    int nextOpcode = cpu.Prog[cpu.PC + 1];
                    int skipSize = B(IsTwoWordInstruction(nextOpcode)) ? 2 : 1;
                    cpu.Cycles += skipSize;
                    cpu.PC += skipSize;
                }
                break;
            }
            case 75:
            {
                /* SBIS, 1001 1011 AAAA Abbb */
                int value = cpu.ReadData(((opcode & 0xf8) >> 3) + 32);
                if (B(value & (1 << (opcode & 7)))) {
                    int nextOpcode = cpu.Prog[cpu.PC + 1];
                    int skipSize = B(IsTwoWordInstruction(nextOpcode)) ? 2 : 1;
                    cpu.Cycles += skipSize;
                    cpu.PC += skipSize;
                }
                break;
            }
            case 76:
            {
                /* SBIW, 1001 0111 KKdd KKKK */
                int i = 2 * ((opcode & 0x30) >> 4) + 24;
                int a = cpu.Get16(i);
                int l = (opcode & 0xf) | ((opcode & 0xc0) >> 2);
                int R = a - l;
                cpu.Set16(i, R);
                int sreg = cpu.Data[95] & 0xc0;
                sreg |= B(R) ? 0 : 2;
                sreg |= B(0x8000 & R) ? 4 : 0;
                sreg |= B(a & ~R & 0x8000) ? 8 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ ((sreg >> 3) & 1)) ? 0x10 : 0;
                sreg |= B(l > a) ? 1 : 0;
                sreg |= B(1 & ((~a & l) | (l & R) | (R & ~a))) ? 0x20 : 0;
                cpu.Data[95] = sreg;
                cpu.Cycles++;
                break;
            }
            case 77:
            {
                /* SBRC, 1111 110r rrrr 0bbb */
                if (B(!B(cpu.Data[(opcode & 0x1f0) >> 4] & (1 << (opcode & 7))))) {
                    int nextOpcode = cpu.Prog[cpu.PC + 1];
                    int skipSize = B(IsTwoWordInstruction(nextOpcode)) ? 2 : 1;
                    cpu.Cycles += skipSize;
                    cpu.PC += skipSize;
                }
                break;
            }
            case 78:
            {
                /* SBRS, 1111 111r rrrr 0bbb */
                if (B(cpu.Data[(opcode & 0x1f0) >> 4] & (1 << (opcode & 7)))) {
                    int nextOpcode = cpu.Prog[cpu.PC + 1];
                    int skipSize = B(IsTwoWordInstruction(nextOpcode)) ? 2 : 1;
                    cpu.Cycles += skipSize;
                    cpu.PC += skipSize;
                }
                break;
            }
            case 79:
            {
                /* SLEEP, 1001 0101 1000 1000 */
                /* not implemented */
                break;
            }
            case 80:
            {
                /* SPM, 1001 0101 1110 1000 */
                /* not implemented */
                break;
            }
            case 81:
            {
                /* SPM(INC), 1001 0101 1111 1000 */
                /* not implemented */
                break;
            }
            case 82:
            {
                /* STS, 1001 001d dddd 0000 kkkk kkkk kkkk kkkk */
                int value = cpu.Data[(opcode & 0x1f0) >> 4];
                int addr = cpu.Prog[cpu.PC + 1];
                cpu.WriteData(addr, value);
                cpu.PC++;
                cpu.Cycles++;
                break;
            }
            case 83:
            {
                /* STX, 1001 001r rrrr 1100 */
                cpu.WriteData(cpu.Get16(26), cpu.Data[(opcode & 0x1f0) >> 4]);
                cpu.Cycles++;
                break;
            }
            case 84:
            {
                /* STX(INC), 1001 001r rrrr 1101 */
                int x = cpu.Get16(26);
                cpu.WriteData(x, cpu.Data[(opcode & 0x1f0) >> 4]);
                cpu.Set16(26, x + 1);
                cpu.Cycles++;
                break;
            }
            case 85:
            {
                /* STX(DEC), 1001 001r rrrr 1110 */
                int i = cpu.Data[(opcode & 0x1f0) >> 4];
                int x = cpu.Get16(26) - 1;
                cpu.Set16(26, x);
                cpu.WriteData(x, i);
                cpu.Cycles++;
                break;
            }
            case 86:
            {
                /* STY, 1000 001r rrrr 1000 */
                cpu.WriteData(cpu.Get16(28), cpu.Data[(opcode & 0x1f0) >> 4]);
                cpu.Cycles++;
                break;
            }
            case 87:
            {
                /* STY(INC), 1001 001r rrrr 1001 */
                int i = cpu.Data[(opcode & 0x1f0) >> 4];
                int y = cpu.Get16(28);
                cpu.WriteData(y, i);
                cpu.Set16(28, y + 1);
                cpu.Cycles++;
                break;
            }
            case 88:
            {
                /* STY(DEC), 1001 001r rrrr 1010 */
                int i = cpu.Data[(opcode & 0x1f0) >> 4];
                int y = cpu.Get16(28) - 1;
                cpu.Set16(28, y);
                cpu.WriteData(y, i);
                cpu.Cycles++;
                break;
            }
            case 89:
            {
                /* STDY, 10q0 qq1r rrrr 1qqq */
                cpu.WriteData(cpu.Get16(28) +
                    ((opcode & 7) | ((opcode & 0xc00) >> 7) | ((opcode & 0x2000) >> 8)), cpu.Data[(opcode & 0x1f0) >> 4]);
                cpu.Cycles++;
                break;
            }
            case 90:
            {
                /* STZ, 1000 001r rrrr 0000 */
                cpu.WriteData(cpu.Get16(30), cpu.Data[(opcode & 0x1f0) >> 4]);
                cpu.Cycles++;
                break;
            }
            case 91:
            {
                /* STZ(INC), 1001 001r rrrr 0001 */
                int z = cpu.Get16(30);
                cpu.WriteData(z, cpu.Data[(opcode & 0x1f0) >> 4]);
                cpu.Set16(30, z + 1);
                cpu.Cycles++;
                break;
            }
            case 92:
            {
                /* STZ(DEC), 1001 001r rrrr 0010 */
                int i = cpu.Data[(opcode & 0x1f0) >> 4];
                int z = cpu.Get16(30) - 1;
                cpu.Set16(30, z);
                cpu.WriteData(z, i);
                cpu.Cycles++;
                break;
            }
            case 93:
            {
                /* STDZ, 10q0 qq1r rrrr 0qqq */
                cpu.WriteData(cpu.Get16(30) +
                    ((opcode & 7) | ((opcode & 0xc00) >> 7) | ((opcode & 0x2000) >> 8)), cpu.Data[(opcode & 0x1f0) >> 4]);
                cpu.Cycles++;
                break;
            }
            case 94:
            {
                /* SUB, 0001 10rd dddd rrrr */
                int val1 = cpu.Data[(opcode & 0x1f0) >> 4];
                int val2 = cpu.Data[(opcode & 0xf) | ((opcode & 0x200) >> 5)];
                int R = val1 - val2;
                cpu.Data[(opcode & 0x1f0) >> 4] = R;
                int sreg = cpu.Data[95] & 0xc0;
                sreg |= B(R) ? 0 : 2;
                sreg |= B(128 & R) ? 4 : 0;
                sreg |= B((val1 ^ val2) & (val1 ^ R) & 128) ? 8 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ ((sreg >> 3) & 1)) ? 0x10 : 0;
                sreg |= B(val2 > val1) ? 1 : 0;
                sreg |= B(1 & ((~val1 & val2) | (val2 & R) | (R & ~val1))) ? 0x20 : 0;
                cpu.Data[95] = sreg;
                break;
            }
            case 95:
            {
                /* SUBI, 0101 KKKK dddd KKKK */
                int val1 = cpu.Data[((opcode & 0xf0) >> 4) + 16];
                int val2 = (opcode & 0xf) | ((opcode & 0xf00) >> 4);
                int R = val1 - val2;
                cpu.Data[((opcode & 0xf0) >> 4) + 16] = R;
                int sreg = cpu.Data[95] & 0xc0;
                sreg |= B(R) ? 0 : 2;
                sreg |= B(128 & R) ? 4 : 0;
                sreg |= B((val1 ^ val2) & (val1 ^ R) & 128) ? 8 : 0;
                sreg |= B(((sreg >> 2) & 1) ^ ((sreg >> 3) & 1)) ? 0x10 : 0;
                sreg |= B(val2 > val1) ? 1 : 0;
                sreg |= B(1 & ((~val1 & val2) | (val2 & R) | (R & ~val1))) ? 0x20 : 0;
                cpu.Data[95] = sreg;
                break;
            }
            case 96:
            {
                /* SWAP, 1001 010d dddd 0010 */
                int d = (opcode & 0x1f0) >> 4;
                int i = cpu.Data[d];
                cpu.Data[d] = ((15 & i) << 4) | ((240 & i) >> 4);
                break;
            }
            case 97:
            {
                /* WDR, 1001 0101 1010 1000 */
                cpu.OnWatchdogReset();
                break;
            }
            case 98:
            {
                /* XCH, 1001 001r rrrr 0100 */
                int r = (opcode & 0x1f0) >> 4;
                int val1 = cpu.Data[r];
                int val2 = cpu.Data[cpu.Get16(30)];
                cpu.Data[cpu.Get16(30)] = val1;
                cpu.Data[r] = val2;
                break;
            }
        }
        cpu.PC = (cpu.PC + 1) % cpu.ProgLength;
        cpu.Cycles++;
    }
}
