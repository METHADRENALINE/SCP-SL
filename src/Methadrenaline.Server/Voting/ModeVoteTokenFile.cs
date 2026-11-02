using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Methadrenaline.Server.Voting;

public static class ModeVoteTokenFile
{
    public static string Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) throw new InvalidOperationException("Voting token file requires an absolute external path.");
        var bytes = new byte[513];
        try
        {
            int size;
            if (Environment.OSVersion.Platform == PlatformID.Unix) size = ReadUnix(Path.GetFullPath(path), bytes);
            else
            {
                using var stream = OpenWindows(Path.GetFullPath(path));
                size = 0;
                int count;
                while (size < bytes.Length && (count = stream.Read(bytes, size, bytes.Length - size)) > 0) size += count;
            }
            string token = Encoding.UTF8.GetString(bytes, 0, size).TrimEnd('\r', '\n');
            if (size == 513 || token.Length < 32 || token.Length > 256) throw new InvalidOperationException("Voting token must contain 32 to 256 ASCII token characters.");
            foreach (char character in token)
                if (character < 33 || character > 126) throw new InvalidOperationException("Voting token must contain ASCII token characters only.");
            return token;
        }
        finally { Array.Clear(bytes, 0, bytes.Length); }
    }

    private static int ReadUnix(string path, byte[] bytes)
    {
        if (IntPtr.Size != 8) throw new PlatformNotSupportedException("Token permission validation requires Linux x64.");
        int fd;
        do { fd = open(path, 0x20800); } while (fd < 0 && Marshal.GetLastWin32Error() == 4);
        if (fd < 0) throw new IOException("Cannot open voting token file securely.");
        IntPtr stat = IntPtr.Zero;
        GCHandle buffer = default;
        try
        {
            stat = Marshal.AllocHGlobal(256);
            if (fstat(fd, stat) != 0) throw new IOException("Cannot inspect voting token permissions.");
            int mode = Marshal.ReadInt32(stat, 24);
            uint uid = unchecked((uint)Marshal.ReadInt32(stat, 28));
            int permissions = mode & 0xfff;
            if ((mode & 0xf000) != 0x8000 || (permissions != 0x100 && permissions != 0x180) || uid != geteuid() || Marshal.ReadInt64(stat, 16) != 1)
                throw new UnauthorizedAccessException("Voting token must be an owned regular file with mode 400 or 600 and no links.");
            buffer = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            int size = 0;
            while (size < bytes.Length)
            {
                long count = read(fd, IntPtr.Add(buffer.AddrOfPinnedObject(), size), (UIntPtr)(uint)(bytes.Length - size)).ToInt64();
                if (count == 0) break;
                if (count < 0)
                {
                    if (Marshal.GetLastWin32Error() == 4) continue;
                    throw new IOException("Cannot read voting token file.");
                }
                size += checked((int)count);
            }
            return size;
        }
        finally
        {
            if (buffer.IsAllocated) buffer.Free();
            if (stat != IntPtr.Zero) Marshal.FreeHGlobal(stat);
            close(fd);
        }
    }

    private static FileStream OpenWindows(string path)
    {
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Voting token files require Linux x64 or Windows.");
#endif
        var file = new FileInfo(path);
        if ((file.Attributes & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException("Voting token file cannot be a link.");
        var acl = file.GetAccessControl();
        string user = WindowsIdentity.GetCurrent().User!.Value;
        if (acl.GetOwner(typeof(SecurityIdentifier))?.Value != user) throw new UnauthorizedAccessException("Voting token must be owned by the server user.");
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && rule.IdentityReference.Value != user && rule.IdentityReference.Value != "S-1-5-18" && rule.IdentityReference.Value != "S-1-5-32-544")
                throw new UnauthorizedAccessException("Voting token ACL must allow only its owner, SYSTEM and Administrators.");
        return file.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int fstat(int fd, IntPtr stat);
    [DllImport("libc", SetLastError = true)] private static extern IntPtr read(int fd, IntPtr buffer, UIntPtr count);
    [DllImport("libc", SetLastError = true)] private static extern int close(int fd);
    [DllImport("libc")] private static extern uint geteuid();
}
