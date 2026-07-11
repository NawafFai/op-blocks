using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace OPBlocks.Core.Persistence
{
    /// <summary>
    /// Low-level helpers for moving a byte payload in and out of a COM
    /// <see cref="IStream"/>. Used by <see cref="OPBlocks.Core.UnitBase"/>'s
    /// <c>IPersistStreamInit</c> implementation so block parameters survive
    /// save → close host → reopen inside .apw/.bkp/.dwxmz files (spec §8.6).
    ///
    /// Payloads are length-prefixed (4-byte little-endian count) so a Load can
    /// read exactly what Save wrote without knowing the stream length up front.
    /// </summary>
    internal static class StreamPersist
    {
        /// <summary>Writes a length-prefixed block to the stream.</summary>
        public static void WriteBlock(IStream stream, byte[] data)
        {
            if (data == null) data = new byte[0];
            byte[] len = BitConverter.GetBytes(data.Length);
            stream.Write(len, len.Length, IntPtr.Zero);
            if (data.Length > 0)
                stream.Write(data, data.Length, IntPtr.Zero);
        }

        /// <summary>Reads a length-prefixed block previously written by <see cref="WriteBlock"/>.</summary>
        public static byte[] ReadBlock(IStream stream)
        {
            byte[] len = new byte[4];
            ReadFully(stream, len, 4);
            int count = BitConverter.ToInt32(len, 0);
            if (count < 0 || count > 64 * 1024 * 1024)
                throw new IOException("Corrupt OP-Blocks persistence stream (implausible length " + count + ").");
            byte[] data = new byte[count];
            ReadFully(stream, data, count);
            return data;
        }

        private static void ReadFully(IStream stream, byte[] buffer, int count)
        {
            if (count == 0) return;
            IntPtr pRead = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                int total = 0;
                while (total < count)
                {
                    // IStream.Read fills from index 0, so read into a temp then copy.
                    byte[] tmp = new byte[count - total];
                    stream.Read(tmp, tmp.Length, pRead);
                    int got = Marshal.ReadInt32(pRead);
                    if (got <= 0)
                        throw new EndOfStreamException("Unexpected end of OP-Blocks persistence stream.");
                    Array.Copy(tmp, 0, buffer, total, got);
                    total += got;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pRead);
            }
        }
    }
}
