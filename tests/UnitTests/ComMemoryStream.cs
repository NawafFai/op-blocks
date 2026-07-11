using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace OPBlocks.UnitTests
{
    /// <summary>
    /// Minimal in-memory <see cref="IStream"/> for exercising a block's
    /// IPersistStream(Init) implementation without a real CAPE-OPEN host.
    /// Only Read/Write/Seek are used by the persistence code under test.
    /// </summary>
    internal sealed class ComMemoryStream : IStream
    {
        private readonly MemoryStream _ms;

        public ComMemoryStream() { _ms = new MemoryStream(); }
        public ComMemoryStream(byte[] initial) { _ms = new MemoryStream(); _ms.Write(initial, 0, initial.Length); _ms.Position = 0; }

        public byte[] ToArray() { return _ms.ToArray(); }
        public void Rewind() { _ms.Position = 0; }

        public void Read(byte[] pv, int cb, IntPtr pcbRead)
        {
            int n = _ms.Read(pv, 0, cb);
            if (pcbRead != IntPtr.Zero) Marshal.WriteInt32(pcbRead, n);
        }

        public void Write(byte[] pv, int cb, IntPtr pcbWritten)
        {
            _ms.Write(pv, 0, cb);
            if (pcbWritten != IntPtr.Zero) Marshal.WriteInt32(pcbWritten, cb);
        }

        public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
        {
            var origin = (SeekOrigin)dwOrigin; // STREAM_SEEK_* maps to SeekOrigin
            long pos = _ms.Seek(dlibMove, origin);
            if (plibNewPosition != IntPtr.Zero) Marshal.WriteInt64(plibNewPosition, pos);
        }

        public void SetSize(long libNewSize) { _ms.SetLength(libNewSize); }
        public void Stat(out System.Runtime.InteropServices.ComTypes.STATSTG pstatstg, int grfStatFlag)
        {
            pstatstg = new System.Runtime.InteropServices.ComTypes.STATSTG { cbSize = _ms.Length };
        }

        // Unused by the code under test.
        public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten) { throw new NotImplementedException(); }
        public void Commit(int grfCommitFlags) { }
        public void Revert() { }
        public void LockRegion(long libOffset, long cb, int dwLockType) { }
        public void UnlockRegion(long libOffset, long cb, int dwLockType) { }
        public void Clone(out IStream ppstm) { throw new NotImplementedException(); }
    }
}
