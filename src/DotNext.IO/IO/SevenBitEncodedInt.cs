﻿using System.IO;
using System.Runtime.InteropServices;

namespace DotNext.IO
{
    internal static class SevenBitEncodedInt
    {
        internal interface IWriter
        {
            void WriteByte(byte value);
        }

        [StructLayout(LayoutKind.Auto)]
        internal struct Reader
        {
            private uint result;
            private int shift;

            internal bool Append(byte b)
            {
                if (shift == 5 * 7)
                    throw new InvalidDataException();
                result |= (b & 0x7FU) << shift;
                shift += 7;
                return (b & 0x80U) != 0U;
            }

            internal readonly uint Result => result;
        }

        internal static void Encode<TWriter>(ref TWriter writer, uint value)
            where TWriter : struct, IWriter
        {
            while (value >= 0x80U)
            {
                writer.WriteByte((byte)(value | 0x80U));
                value >>= 7;
            }
            writer.WriteByte((byte)value);
        }
    }
}
