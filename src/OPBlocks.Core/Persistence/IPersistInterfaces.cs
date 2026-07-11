using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace OPBlocks.Core.Persistence
{
    // The CLR does not ship managed definitions of IPersistStream /
    // IPersistStreamInit, so we declare them with their canonical IIDs. Aspen
    // Plus persists CAPE-OPEN units through IPersistStreamInit; COFE/DWSIM use
    // IPersistStream. Implementing both makes a block save/restore correctly in
    // every host (spec §8.6). Method (vtable) order includes IPersist.GetClassID
    // first, so the layout matches the COM standard exactly.

    [ComImport]
    [Guid("00000109-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPersistStream
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load(IStream pstm);
        void Save(IStream pstm, [MarshalAs(UnmanagedType.Bool)] bool fClearDirty);
        void GetSizeMax(out long pcbSize);
    }

    [ComImport]
    [Guid("7FD52380-4E07-101B-AE2D-08002B2EC713")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPersistStreamInit
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load(IStream pstm);
        void Save(IStream pstm, [MarshalAs(UnmanagedType.Bool)] bool fClearDirty);
        void GetSizeMax(out long pcbSize);
        void InitNew();
    }
}
