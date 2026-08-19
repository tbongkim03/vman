using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace VMan.Core;

/// <summary>
/// 디렉터리 정션(mount point reparse point) 생성/조회/삭제.
/// 심볼릭 링크와 달리 일반 사용자 권한으로 만들 수 있다는 것이 핵심.
/// 윈도우 전용이다. 리눅스/WSL 에서는 <see cref="Links"/> 가 심볼릭 링크로 대체한다.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Junction
{
    private const uint FSCTL_SET_REPARSE_POINT = 0x000900A4;
    private const uint FSCTL_GET_REPARSE_POINT = 0x000900A8;
    private const uint FSCTL_DELETE_REPARSE_POINT = 0x000900AC;
    private const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x1;
    private const uint FILE_SHARE_WRITE = 0x2;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;

    private const int MAXIMUM_REPARSE_DATA_BUFFER_SIZE = 16 * 1024;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        byte[]? lpInBuffer, int nInBufferSize,
        byte[]? lpOutBuffer, int nOutBufferSize,
        out int lpBytesReturned, IntPtr lpOverlapped);

    /// <summary>해당 경로가 리파스 포인트(정션/심링크)인지.</summary>
    public static bool IsLink(string path)
    {
        if (!Directory.Exists(path)) return false;
        var attr = File.GetAttributes(path);
        return (attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
    }

    /// <summary>정션이 가리키는 실제 대상 경로. 정션이 아니면 null.</summary>
    public static string? GetTarget(string junctionPath)
    {
        if (!IsLink(junctionPath)) return null;

        using var handle = OpenReparsePoint(junctionPath, GENERIC_READ);
        byte[] outBuffer = new byte[MAXIMUM_REPARSE_DATA_BUFFER_SIZE];

        if (!DeviceIoControl(handle, FSCTL_GET_REPARSE_POINT, null, 0,
                outBuffer, outBuffer.Length, out _, IntPtr.Zero))
            return null;

        uint tag = BitConverter.ToUInt32(outBuffer, 0);
        if (tag != IO_REPARSE_TAG_MOUNT_POINT) return null;

        ushort subOffset = BitConverter.ToUInt16(outBuffer, 8);
        ushort subLength = BitConverter.ToUInt16(outBuffer, 10);
        // 헤더 8바이트 + MountPoint 헤더 8바이트 = 16 부터 PathBuffer
        string target = Encoding.Unicode.GetString(outBuffer, 16 + subOffset, subLength);

        // NT 네임스페이스 접두어 제거
        if (target.StartsWith(@"\??\", StringComparison.Ordinal))
            target = target[4..];
        return target;
    }

    /// <summary>정션 생성. 이미 뭔가 있으면 먼저 Remove를 호출할 것.</summary>
    public static void Create(string junctionPath, string targetDir)
    {
        targetDir = Path.GetFullPath(targetDir).TrimEnd('\\');
        if (!Directory.Exists(targetDir))
            throw new DirectoryNotFoundException($"대상 폴더가 없습니다: {targetDir}");

        Directory.CreateDirectory(junctionPath);

        byte[] subName = Encoding.Unicode.GetBytes(@"\??\" + targetDir);
        byte[] printName = Encoding.Unicode.GetBytes(targetDir);

        // PathBuffer = substituteName + '\0' + printName + '\0'
        int pathBufferLength = subName.Length + 2 + printName.Length + 2;
        ushort reparseDataLength = (ushort)(8 + pathBufferLength);
        byte[] buffer = new byte[8 + reparseDataLength];

        using (var ms = new MemoryStream(buffer))
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write(IO_REPARSE_TAG_MOUNT_POINT);              // ReparseTag
            bw.Write(reparseDataLength);                       // ReparseDataLength
            bw.Write((ushort)0);                               // Reserved
            bw.Write((ushort)0);                               // SubstituteNameOffset
            bw.Write((ushort)subName.Length);                  // SubstituteNameLength
            bw.Write((ushort)(subName.Length + 2));            // PrintNameOffset
            bw.Write((ushort)printName.Length);                // PrintNameLength
            bw.Write(subName);
            bw.Write((ushort)0);
            bw.Write(printName);
            bw.Write((ushort)0);
        }

        using var handle = OpenReparsePoint(junctionPath, GENERIC_WRITE);
        if (!DeviceIoControl(handle, FSCTL_SET_REPARSE_POINT, buffer, buffer.Length,
                null, 0, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"정션 생성 실패: {junctionPath} -> {targetDir}");
    }

    /// <summary>
    /// 정션 제거. 대상 폴더의 내용물은 절대 건드리지 않는다.
    /// 경로가 정션이 아닌 실제 폴더면 예외를 던진다(사고 방지).
    /// </summary>
    public static void Remove(string junctionPath)
    {
        if (!Directory.Exists(junctionPath)) return;
        if (!IsLink(junctionPath))
            throw new IOException($"정션이 아닌 실제 폴더입니다. 안전을 위해 삭제하지 않습니다: {junctionPath}");

        using (var handle = OpenReparsePoint(junctionPath, GENERIC_WRITE))
        {
            // 헤더만 있는 빈 버퍼로 리파스 포인트 해제
            byte[] buffer = new byte[8];
            BitConverter.GetBytes(IO_REPARSE_TAG_MOUNT_POINT).CopyTo(buffer, 0);
            DeviceIoControl(handle, FSCTL_DELETE_REPARSE_POINT, buffer, buffer.Length,
                null, 0, out _, IntPtr.Zero);
        }

        Directory.Delete(junctionPath);
    }

    /// <summary>Remove 후 Create. 전환의 실체는 이 한 줄이다.</summary>
    public static void Repoint(string junctionPath, string targetDir)
    {
        Remove(junctionPath);
        Create(junctionPath, targetDir);
    }

    private static SafeFileHandle OpenReparsePoint(string path, uint access)
    {
        var handle = CreateFile(path, access,
            FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);

        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"열기 실패: {path}");
        return handle;
    }
}
