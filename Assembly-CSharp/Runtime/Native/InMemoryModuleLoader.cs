using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace MU3.Mod.Native;

/// <summary>
/// Maps a trusted embedded x64 PE32+ DLL into private process memory without
/// creating a filesystem copy or a Windows loader module entry.
/// </summary>
internal sealed class InMemoryModuleLoader
{
    private const ushort ImageDosSignature = 0x5a4d;
    private const uint ImageNtSignature = 0x00004550;
    private const ushort ImageFileMachineAmd64 = 0x8664;
    private const ushort ImageFileDll = 0x2000;
    private const ushort ImageNtOptionalHdr64Magic = 0x20b;

    private const int ImageDirectoryEntryExport = 0;
    private const int ImageDirectoryEntryImport = 1;
    private const int ImageDirectoryEntryException = 3;
    private const int ImageDirectoryEntryBaseReloc = 5;
    private const int ImageDirectoryEntryTls = 9;
    private const int ImageDirectoryEntryLoadConfig = 10;
    private const int ImageDirectoryEntryDelayImport = 13;
    private const int ImageDirectoryEntryClr = 14;

    private const ushort ImageRelBasedAbsolute = 0;
    private const ushort ImageRelBasedDir64 = 10;
    private const uint ImageScnMemDiscardable = 0x02000000;
    private const uint ImageScnMemNotCached = 0x04000000;
    private const uint ImageScnMemExecute = 0x20000000;
    private const uint ImageScnMemRead = 0x40000000;
    private const uint ImageScnMemWrite = 0x80000000;

    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint MemDecommit = 0x4000;
    private const uint MemRelease = 0x8000;
    private const uint PageNoAccess = 0x01;
    private const uint PageReadOnly = 0x02;
    private const uint PageReadWrite = 0x04;
    private const uint PageExecute = 0x10;
    private const uint PageExecuteRead = 0x20;
    private const uint PageExecuteReadWrite = 0x40;
    private const uint TlsOutOfIndexes = 0xffffffff;
    private const uint PageNoCache = 0x200;

    private const uint DllProcessDetach = 0;
    private const uint DllProcessAttach = 1;

    private const int RuntimeFunctionSize = 12;

    [StructLayout(LayoutKind.Sequential)]
    private struct Section
    {
        internal uint VirtualSize;
        internal uint VirtualAddress;
        internal uint RawSize;
        internal uint RawAddress;
        internal uint Characteristics;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemInfo
    {
        internal ushort ProcessorArchitecture;
        internal ushort Reserved;
        internal uint PageSize;
        internal IntPtr MinimumApplicationAddress;
        internal IntPtr MaximumApplicationAddress;
        internal UIntPtr ActiveProcessorMask;
        internal uint NumberOfProcessors;
        internal uint ProcessorType;
        internal uint AllocationGranularity;
        internal ushort ProcessorLevel;
        internal ushort ProcessorRevision;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool DllEntry(IntPtr module, uint reason, IntPtr reserved);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void TlsCallback(IntPtr module, uint reason, IntPtr reserved);

    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint allocationType, uint protection);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool VirtualFree(IntPtr address, UIntPtr size, uint freeType);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint newProtection, out uint oldProtection);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);

    [DllImport("kernel32")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32")]
    private static extern void GetSystemInfo(out SystemInfo systemInfo);
    [DllImport("kernel32", SetLastError = true)]
    private static extern uint TlsAlloc();

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool TlsFree(uint index);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool TlsSetValue(uint index, IntPtr value);


    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string path);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [DllImport("kernel32", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, IntPtr ordinal);

    [DllImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool RtlAddFunctionTable(IntPtr functionTable, uint entryCount, ulong baseAddress);

    [DllImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool RtlDeleteFunctionTable(IntPtr functionTable);

    private readonly List<IntPtr> _imports = new();
    private readonly Section[] _sections;
    private readonly uint[] _directoryRvas = new uint[16];
    private readonly uint[] _directorySizes = new uint[16];
    private readonly uint _entryPointRva;
    private readonly uint _sizeOfImage;
    private readonly uint _sizeOfHeaders;
    private readonly IntPtr _baseAddress;
    private IntPtr _functionTable;
    private bool _functionTableRegistered;
    private uint _tlsIndex = TlsOutOfIndexes;
    private IntPtr _tlsData;
    private bool _tlsAttached;
    private bool _entryAttached;
    private bool _released;

    private InMemoryModuleLoader(byte[] image)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image));
        if (IntPtr.Size != 8)
            throw new PlatformNotSupportedException("Embedded native modules require a 64-bit process.");

        var peOffset = CheckedInt(ReadUInt32(image, 0x3c), "PE header offset");
        Require(peOffset >= 0x40 && peOffset <= image.Length - 24, "invalid PE header offset");
        Require(ReadUInt16(image, 0) == ImageDosSignature, "invalid DOS signature");
        Require(ReadUInt32(image, peOffset) == ImageNtSignature, "invalid PE signature");

        var fileHeader = peOffset + 4;
        Require(ReadUInt16(image, fileHeader) == ImageFileMachineAmd64, "embedded DLL is not x64");
        var sectionCount = ReadUInt16(image, fileHeader + 2);
        var optionalSize = ReadUInt16(image, fileHeader + 16);
        var characteristics = ReadUInt16(image, fileHeader + 18);
        Require(sectionCount > 0 && sectionCount <= 96, "invalid section count");
        Require((characteristics & ImageFileDll) != 0, "embedded image is not a DLL");

        var optional = fileHeader + 20;
        Require(optionalSize >= 112 && optional <= image.Length - optionalSize, "invalid optional header");
        Require(ReadUInt16(image, optional) == ImageNtOptionalHdr64Magic, "embedded DLL is not PE32+");
        _entryPointRva = ReadUInt32(image, optional + 16);
        var preferredBase = ReadUInt64(image, optional + 24);
        _sizeOfImage = ReadUInt32(image, optional + 56);
        _sizeOfHeaders = ReadUInt32(image, optional + 60);
        Require(_sizeOfImage != 0 && _sizeOfHeaders != 0 && _sizeOfHeaders <= _sizeOfImage,
            "invalid image size");
        Require(_sizeOfHeaders <= image.Length, "truncated PE headers");

        var directoryCount = Math.Min(ReadUInt32(image, optional + 108), 16u);
        Require(optionalSize >= 112 + directoryCount * 8, "truncated data directories");
        for (var i = 0; i < directoryCount; ++i)
        {
            _directoryRvas[i] = ReadUInt32(image, optional + 112 + i * 8);
            _directorySizes[i] = ReadUInt32(image, optional + 116 + i * 8);
            ValidateRange(_directoryRvas[i], _directorySizes[i], _sizeOfImage, "data directory");
        }

        Require(_directoryRvas[ImageDirectoryEntryLoadConfig] == 0,
            "embedded DLL load-configuration data is unsupported");
        Require(_directoryRvas[ImageDirectoryEntryDelayImport] == 0,
            "embedded DLL delay imports are unsupported");
        Require(_directoryRvas[ImageDirectoryEntryClr] == 0,
            "embedded CLR or mixed-mode DLLs are unsupported");

        var sectionTable = optional + optionalSize;
        Require(sectionTable <= image.Length - sectionCount * 40, "truncated section table");
        _sections = new Section[sectionCount];
        for (var i = 0; i < sectionCount; ++i)
        {
            var offset = sectionTable + i * 40;
            var section = new Section
            {
                VirtualSize = ReadUInt32(image, offset + 8),
                VirtualAddress = ReadUInt32(image, offset + 12),
                RawSize = ReadUInt32(image, offset + 16),
                RawAddress = ReadUInt32(image, offset + 20),
                Characteristics = ReadUInt32(image, offset + 36)
            };
            var mappedSize = Math.Max(section.VirtualSize, section.RawSize);
            ValidateRange(section.VirtualAddress, mappedSize, _sizeOfImage, "section virtual range");
            ValidateRange(section.RawAddress, section.RawSize, (uint)image.Length, "section raw range");
            _sections[i] = section;
        }

        var preferredAddress = new IntPtr(unchecked((long)preferredBase));
        _baseAddress = VirtualAlloc(preferredAddress, ToUIntPtr(_sizeOfImage),
            MemReserve | MemCommit, PageReadWrite);
        if (_baseAddress == IntPtr.Zero)
            _baseAddress = VirtualAlloc(IntPtr.Zero, ToUIntPtr(_sizeOfImage),
                MemReserve | MemCommit, PageReadWrite);
        if (_baseAddress == IntPtr.Zero)
            ThrowWin32("VirtualAlloc failed");

        try
        {
            Marshal.Copy(image, 0, _baseAddress, CheckedInt(_sizeOfHeaders, "header size"));
            foreach (var section in _sections)
            {
                if (section.RawSize == 0)
                    continue;
                Marshal.Copy(image, CheckedInt(section.RawAddress, "section raw offset"),
                    Add(_baseAddress, section.VirtualAddress), CheckedInt(section.RawSize, "section size"));
            }

            ApplyRelocations(unchecked((long)_baseAddress.ToInt64() - (long)preferredBase));
            BuildImportTable();
            InitializeTls();
            FinalizeSections();
            RegisterFunctionTable();
            _tlsAttached = true;
            ExecuteTlsCallbacks(DllProcessAttach);
            _entryAttached = true;
            CallEntryPoint(DllProcessAttach, true);
        }
        catch
        {
            Release();
            throw;
        }
    }


    internal static InMemoryModuleLoader Load(byte[] image)
    {
        return new InMemoryModuleLoader(image);
    }

    internal IntPtr GetExport(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Export name is required.", nameof(name));

        var directoryRva = _directoryRvas[ImageDirectoryEntryExport];
        var directorySize = _directorySizes[ImageDirectoryEntryExport];
        Require(directoryRva != 0 && directorySize >= 40, "embedded DLL has no export directory");

        var export = Add(_baseAddress, directoryRva);
        var ordinalBase = ReadUInt32(export, 16);
        var functionCount = ReadUInt32(export, 20);
        var nameCount = ReadUInt32(export, 24);
        var functionsRva = ReadUInt32(export, 28);
        var namesRva = ReadUInt32(export, 32);
        var ordinalsRva = ReadUInt32(export, 36);
        ValidateImageRange(functionsRva, CheckedMultiply(functionCount, 4, "export function table"));
        ValidateImageRange(namesRva, CheckedMultiply(nameCount, 4, "export name table"));
        ValidateImageRange(ordinalsRva, CheckedMultiply(nameCount, 2, "export ordinal table"));

        for (uint i = 0; i < nameCount; ++i)
        {
            var nameRva = ReadUInt32(Add(_baseAddress, namesRva), CheckedInt(i * 4, "export index"));
            if (!string.Equals(ReadAnsiString(nameRva), name, StringComparison.Ordinal))
                continue;

            var functionIndex = ReadUInt16(Add(_baseAddress, ordinalsRva), CheckedInt(i * 2, "ordinal index"));
            Require(functionIndex < functionCount, "invalid export ordinal");
            var functionRva = ReadUInt32(Add(_baseAddress, functionsRva), functionIndex * 4);
            Require(functionRva != 0, "export has no address");
            Require(functionRva < directoryRva || functionRva >= directoryRva + directorySize,
                "forwarded embedded exports are unsupported");
            ValidateImageRange(functionRva, 1);
            return Add(_baseAddress, functionRva);
        }

        throw new EntryPointNotFoundException("Embedded native export not found: " + name
            + " (ordinal base " + ordinalBase + ")");
    }

    private void ApplyRelocations(long delta)
    {
        if (delta == 0)
            return;

        var rva = _directoryRvas[ImageDirectoryEntryBaseReloc];
        var size = _directorySizes[ImageDirectoryEntryBaseReloc];
        Require(rva != 0 && size >= 8, "image relocation required but relocation table is absent");

        uint consumed = 0;
        while (consumed < size)
        {
            Require(size - consumed >= 8, "truncated relocation block");
            var block = Add(_baseAddress, rva + consumed);
            var pageRva = ReadUInt32(block, 0);
            var blockSize = ReadUInt32(block, 4);
            Require(blockSize >= 8 && blockSize <= size - consumed && (blockSize & 1) == 0,
                "invalid relocation block");
            var entryCount = (blockSize - 8) / 2;
            for (uint i = 0; i < entryCount; ++i)
            {
                var entry = ReadUInt16(block, CheckedInt(8 + i * 2, "relocation entry"));
                var type = (ushort)(entry >> 12);
                var offset = (uint)(entry & 0xfff);
                if (type == ImageRelBasedAbsolute)
                    continue;
                Require(type == ImageRelBasedDir64, "unsupported relocation type " + type);
                var targetRva = CheckedAdd(pageRva, offset, "relocation target");
                ValidateImageRange(targetRva, 8);
                var target = Add(_baseAddress, targetRva);
                Marshal.WriteInt64(target, unchecked(Marshal.ReadInt64(target) + delta));
            }
            consumed += blockSize;
        }
    }

    private void BuildImportTable()
    {
        var rva = _directoryRvas[ImageDirectoryEntryImport];
        var size = _directorySizes[ImageDirectoryEntryImport];
        if (rva == 0 || size == 0)
            return;

        uint offset = 0;
        while (offset + 20 <= size)
        {
            var descriptor = Add(_baseAddress, rva + offset);
            var originalFirstThunk = ReadUInt32(descriptor, 0);
            var nameRva = ReadUInt32(descriptor, 12);
            var firstThunk = ReadUInt32(descriptor, 16);
            if (originalFirstThunk == 0 && nameRva == 0 && firstThunk == 0)
                return;
            Require(nameRva != 0 && firstThunk != 0, "invalid import descriptor");

            var libraryName = ReadAnsiString(nameRva);
            var library = LoadLibrary(libraryName);
            if (library == IntPtr.Zero)
                ThrowWin32("LoadLibrary failed for native dependency " + libraryName);
            _imports.Add(library);

            var lookupRva = originalFirstThunk != 0 ? originalFirstThunk : firstThunk;
            for (uint index = 0; ; ++index)
            {
                var entryOffset = CheckedMultiply(index, 8, "import thunk offset");
                ValidateImageRange(CheckedAdd(lookupRva, entryOffset, "import lookup"), 8);
                ValidateImageRange(CheckedAdd(firstThunk, entryOffset, "import address"), 8);
                var lookup = unchecked((ulong)Marshal.ReadInt64(Add(_baseAddress, lookupRva + entryOffset)));
                if (lookup == 0)
                    break;

                IntPtr address;
                if ((lookup & 0x8000000000000000UL) != 0)
                {
                    address = GetProcAddress(library, new IntPtr((long)(lookup & 0xffff)));
                }
                else
                {
                    Require(lookup <= uint.MaxValue, "invalid import name RVA");
                    var importNameRva = (uint)lookup;
                    ValidateImageRange(importNameRva, 3);
                    address = GetProcAddress(library, ReadAnsiString(importNameRva + 2));
                }
                if (address == IntPtr.Zero)
                    ThrowWin32("GetProcAddress failed for an import from " + libraryName);
                Marshal.WriteInt64(Add(_baseAddress, firstThunk + entryOffset), address.ToInt64());
            }

            offset += 20;
        }
        throw new BadImageFormatException("Unterminated import descriptor table.");
    }

    private void FinalizeSections()
    {
        SystemInfo systemInfo;
        GetSystemInfo(out systemInfo);
        var pageSize = systemInfo.PageSize;
        Require(pageSize != 0 && (pageSize & (pageSize - 1)) == 0,
            "invalid system page size");

        var pageCount64 = ((ulong)_sizeOfImage + pageSize - 1) / pageSize;
        Require(pageCount64 <= int.MaxValue, "embedded DLL has too many memory pages");
        var pageCount = (int)pageCount64;
        var retained = new bool[pageCount];
        var characteristics = new uint[pageCount];

        MarkPages(retained, characteristics, 0, _sizeOfHeaders,
            ImageScnMemRead, pageSize);
        foreach (var section in _sections)
        {
            if ((section.Characteristics & ImageScnMemDiscardable) != 0)
                continue;
            var size = Math.Max(section.VirtualSize, section.RawSize);
            MarkPages(retained, characteristics, section.VirtualAddress, size,
                section.Characteristics, pageSize);
        }

        var start = 0;
        while (start < pageCount)
        {
            var keep = retained[start];
            var protection = keep ? GetProtection(characteristics[start]) : 0;
            var end = start + 1;
            while (end < pageCount && retained[end] == keep
                && (!keep || GetProtection(characteristics[end]) == protection))
            {
                ++end;
            }

            var offset = CheckedMultiply((uint)start, pageSize, "page offset");
            var size = CheckedMultiply((uint)(end - start), pageSize, "page range");
            var address = Add(_baseAddress, offset);
            if (keep)
            {
                if (!VirtualProtect(address, ToUIntPtr(size), protection, out _))
                    ThrowWin32("VirtualProtect failed");
            }
            else if (!VirtualFree(address, ToUIntPtr(size), MemDecommit))
            {
                ThrowWin32("VirtualFree(MEM_DECOMMIT) failed");
            }
            start = end;
        }

        if (!FlushInstructionCache(GetCurrentProcess(), _baseAddress, ToUIntPtr(_sizeOfImage)))
            ThrowWin32("FlushInstructionCache failed");
    }

    private static void MarkPages(bool[] retained, uint[] characteristics,
        uint offset, uint size, uint sectionCharacteristics, uint pageSize)
    {
        if (size == 0)
            return;
        var endOffset = (ulong)offset + size;
        Require(endOffset <= (ulong)retained.Length * pageSize,
            "section page range exceeds image");
        var firstPage = (int)(offset / pageSize);
        var endPage = (int)((endOffset + pageSize - 1) / pageSize);
        for (var page = firstPage; page < endPage; ++page)
        {
            retained[page] = true;
            characteristics[page] |= sectionCharacteristics;
        }
    }

    private static uint GetProtection(uint characteristics)
    {
        var executable = (characteristics & ImageScnMemExecute) != 0;
        var readable = (characteristics & ImageScnMemRead) != 0;
        var writable = (characteristics & ImageScnMemWrite) != 0;
        uint protection;
        if (executable)
            protection = writable ? PageExecuteReadWrite : readable ? PageExecuteRead : PageExecute;
        else
            protection = writable ? PageReadWrite : readable ? PageReadOnly : PageNoAccess;
        if ((characteristics & ImageScnMemNotCached) != 0)
            protection |= PageNoCache;
        return protection;
    }

    private void RegisterFunctionTable()
    {
        var rva = _directoryRvas[ImageDirectoryEntryException];
        var size = _directorySizes[ImageDirectoryEntryException];
        if (rva == 0 || size == 0)
            return;
        Require(size % RuntimeFunctionSize == 0, "invalid x64 exception directory");
        _functionTable = Add(_baseAddress, rva);
        if (!RtlAddFunctionTable(_functionTable, size / RuntimeFunctionSize,
                unchecked((ulong)_baseAddress.ToInt64())))
            ThrowWin32("RtlAddFunctionTable failed");
        _functionTableRegistered = true;
    }

    private void InitializeTls()
    {
        var rva = _directoryRvas[ImageDirectoryEntryTls];
        var size = _directorySizes[ImageDirectoryEntryTls];
        if (rva == 0 || size == 0)
            return;
        Require(size >= 40, "invalid TLS directory");

        var tls = Add(_baseAddress, rva);
        var rawStart = new IntPtr(Marshal.ReadInt64(tls, 0));
        var rawEnd = new IntPtr(Marshal.ReadInt64(tls, 8));
        var indexAddress = new IntPtr(Marshal.ReadInt64(tls, 16));
        var zeroFill = ReadUInt32(tls, 32);
        var rawLength64 = rawEnd.ToInt64() - rawStart.ToInt64();
        Require(rawLength64 >= 0 && rawLength64 <= int.MaxValue, "invalid TLS raw-data range");
        var rawLength = (int)rawLength64;
        Require(IsInImage(rawStart, rawLength), "TLS raw data lies outside the image");
        Require(IsInImage(indexAddress, 4), "TLS index lies outside the image");
        var totalLength64 = (ulong)rawLength + zeroFill;
        Require(totalLength64 <= int.MaxValue, "TLS data is too large");
        var totalLength = (int)totalLength64;

        _tlsIndex = TlsAlloc();
        if (_tlsIndex == TlsOutOfIndexes)
            ThrowWin32("TlsAlloc failed");
        Marshal.WriteInt32(indexAddress, unchecked((int)_tlsIndex));

        _tlsData = Marshal.AllocHGlobal(Math.Max(totalLength, 1));
        if (totalLength > 0)
        {
            var data = new byte[totalLength];
            if (rawLength > 0)
                Marshal.Copy(rawStart, data, 0, rawLength);
            Marshal.Copy(data, 0, _tlsData, totalLength);
        }
        if (!TlsSetValue(_tlsIndex, _tlsData))
            ThrowWin32("TlsSetValue failed");
    }

    private void ExecuteTlsCallbacks(uint reason)
    {
        var rva = _directoryRvas[ImageDirectoryEntryTls];
        var size = _directorySizes[ImageDirectoryEntryTls];
        if (rva == 0 || size == 0)
            return;
        Require(size >= 40, "invalid TLS directory");
        var callbacks = new IntPtr(Marshal.ReadInt64(Add(_baseAddress, rva), 24));
        if (callbacks == IntPtr.Zero)
            return;
        Require(IsInImage(callbacks, IntPtr.Size), "TLS callback table lies outside the image");

        for (var index = 0; ; ++index)
        {
            var callbackSlot = Add(callbacks, CheckedMultiply((uint)index, (uint)IntPtr.Size,
                "TLS callback index"));
            Require(IsInImage(callbackSlot, IntPtr.Size), "unterminated TLS callback table");
            var callbackAddress = Marshal.ReadIntPtr(callbackSlot);
            if (callbackAddress == IntPtr.Zero)
                return;
            Require(IsInImage(callbackAddress, 1), "TLS callback lies outside the image");
            var callback = (TlsCallback)Marshal.GetDelegateForFunctionPointer(
                callbackAddress, typeof(TlsCallback));
            callback(_baseAddress, reason, IntPtr.Zero);
        }
    }

    private void CallEntryPoint(uint reason, bool requireSuccess)
    {
        if (_entryPointRva == 0)
            return;
        ValidateImageRange(_entryPointRva, 1);
        var entry = (DllEntry)Marshal.GetDelegateForFunctionPointer(
            Add(_baseAddress, _entryPointRva), typeof(DllEntry));
        var success = entry(_baseAddress, reason, IntPtr.Zero);
        if (requireSuccess && !success)
            throw new BadImageFormatException("Embedded DLL entry point rejected process attach.");
    }

    private void Release()
    {
        if (_released)
            return;
        _released = true;

        try
        {
            if (_entryAttached)
                CallEntryPoint(DllProcessDetach, false);
            if (_tlsAttached)
                ExecuteTlsCallbacks(DllProcessDetach);
        }
        catch
        {
            // Only failed, unpublished loads use this path.
        }

        ReleaseTls();
        if (_functionTableRegistered)
        {
            RtlDeleteFunctionTable(_functionTable);
            _functionTableRegistered = false;
        }
        for (var i = _imports.Count - 1; i >= 0; --i)
            FreeLibrary(_imports[i]);
        _imports.Clear();
        VirtualFree(_baseAddress, UIntPtr.Zero, MemRelease);
    }

    private void ReleaseTls()
    {
        if (_tlsIndex == TlsOutOfIndexes)
            return;
        TlsSetValue(_tlsIndex, IntPtr.Zero);
        if (_tlsData != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_tlsData);
            _tlsData = IntPtr.Zero;
        }
        TlsFree(_tlsIndex);
        _tlsIndex = TlsOutOfIndexes;
    }

    private string ReadAnsiString(uint rva)
    {
        ValidateImageRange(rva, 1);
        var bytes = new List<byte>();
        for (uint offset = rva; offset < _sizeOfImage; ++offset)
        {
            var value = Marshal.ReadByte(Add(_baseAddress, offset));
            if (value == 0)
                return Encoding.ASCII.GetString(bytes.ToArray());
            bytes.Add(value);
        }
        throw new BadImageFormatException("Unterminated ANSI string in embedded DLL.");
    }

    private bool IsInImage(IntPtr address, int size)
    {
        var offset = address.ToInt64() - _baseAddress.ToInt64();
        return offset >= 0 && size >= 0 && (ulong)offset + (uint)size <= _sizeOfImage;
    }

    private void ValidateImageRange(uint rva, uint size)
    {
        ValidateRange(rva, size, _sizeOfImage, "image range");
    }

    private static void ValidateRange(uint offset, uint size, uint limit, string label)
    {
        Require(offset <= limit && size <= limit - offset, "invalid " + label);
    }

    private static uint CheckedAdd(uint left, uint right, string label)
    {
        var result = (ulong)left + right;
        Require(result <= uint.MaxValue, label + " overflow");
        return (uint)result;
    }

    private static uint CheckedMultiply(uint left, uint right, string label)
    {
        var result = (ulong)left * right;
        Require(result <= uint.MaxValue, label + " overflow");
        return (uint)result;
    }

    private static int CheckedInt(uint value, string label)
    {
        Require(value <= int.MaxValue, label + " exceeds managed array limits");
        return (int)value;
    }

    private static UIntPtr ToUIntPtr(uint value)
    {
        return new UIntPtr(value);
    }

    private static IntPtr Add(IntPtr address, uint offset)
    {
        return new IntPtr(unchecked(address.ToInt64() + offset));
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        Require(offset >= 0 && offset <= data.Length - 4, "truncated PE image");
        return BitConverter.ToUInt32(data, offset);
    }

    private static ushort ReadUInt16(byte[] data, int offset)
    {
        Require(offset >= 0 && offset <= data.Length - 2, "truncated PE image");
        return BitConverter.ToUInt16(data, offset);
    }

    private static ulong ReadUInt64(byte[] data, int offset)
    {
        Require(offset >= 0 && offset <= data.Length - 8, "truncated PE image");
        return BitConverter.ToUInt64(data, offset);
    }

    private static uint ReadUInt32(IntPtr address, int offset)
    {
        return unchecked((uint)Marshal.ReadInt32(address, offset));
    }

    private static ushort ReadUInt16(IntPtr address, int offset)
    {
        return unchecked((ushort)Marshal.ReadInt16(address, offset));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new BadImageFormatException(message);
    }

    private static void ThrowWin32(string message)
    {
        throw new InvalidOperationException(message + " (Win32 " + Marshal.GetLastWin32Error() + ").");
    }
}
