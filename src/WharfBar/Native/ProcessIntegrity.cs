using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>
/// Reads the integrity level of the current process token.
///
/// Windows enforces UIPI between integrity levels: a process below medium cannot drive the
/// shell — <c>ShowWindow(Shell_TrayWnd, SW_HIDE)</c> is ignored, <c>SHAppBarMessage(ABM_NEW)</c>
/// returns 0, and <c>SetWindowLong</c> is refused. A restricted launcher (a sandboxed agent
/// terminal, or a low-integrity parent) therefore produces a WharfBar that silently cannot hide
/// the taskbar or reserve the top edge. Startup checks this and warns instead of running a
/// desktop that no longer matches the bar.
/// </summary>
internal static class ProcessIntegrity
{
    /// <summary><c>SECURITY_MANDATORY_MEDIUM_RID</c> (S-1-16-8192).</summary>
    internal const int MediumRid = 0x2000;

    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;

    /// <summary>
    /// True when a known integrity RID sits below medium. <c>SECURITY_MANDATORY_UNTRUSTED_RID</c>
    /// is <c>0</c>, so it counts; <c>-1</c> is the failed-probe sentinel and is fail-open (never
    /// restricted), so a failed probe cannot block startup.
    /// </summary>
    internal static bool IsBelowMedium(int rid) => rid >= 0 && rid < MediumRid;

    /// <summary>
    /// Reads this process's integrity RID into <paramref name="rid"/>. Returns <c>false</c> and
    /// leaves <paramref name="rid"/> at <c>-1</c> when the token cannot be read; callers treat that
    /// as fail-open, not as a restricted launch.
    /// </summary>
    internal static bool TryGetCurrentRid(out int rid)
    {
        rid = -1;
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token))
            {
                return false;
            }

            _ = GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out int length);
            if (length <= 0 || length > 256)
            {
                return false;
            }

            IntPtr buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, length, out _))
                {
                    return false;
                }

                // TOKEN_MANDATORY_LABEL → SID_AND_ATTRIBUTES.Sid points at the S-1-16-N SID;
                // the last sub-authority is the RID (0 untrusted, 4096 low, 8192 medium, 12288 high).
                IntPtr sid = Marshal.ReadIntPtr(buffer);
                int sidLength = GetLengthSid(sid);
                if (sidLength < 8 || sidLength > 256)
                {
                    return false;
                }

                byte[] bytes = new byte[sidLength];
                Marshal.Copy(sid, bytes, 0, sidLength);
                return TryReadLastSubAuthority(bytes, out rid);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception)
        {
            rid = -1;
            return false;
        }
        finally
        {
            if (token != IntPtr.Zero)
            {
                _ = CloseHandle(token);
            }
        }
    }

    /// <summary>
    /// RID from a raw SID: a SID is revision + sub-authority count + 6-byte authority + that many
    /// little-endian 4-byte sub-authorities, so the last one is the RID. Returns false for a
    /// malformed or authority-only SID.
    /// </summary>
    internal static bool TryReadLastSubAuthority(byte[] sidBytes, out int rid)
    {
        rid = -1;
        if (sidBytes is null || sidBytes.Length < 12)
        {
            return false;
        }

        int subAuthorityCount = sidBytes[1];
        if (subAuthorityCount <= 0 || sidBytes.Length != 8 + (subAuthorityCount * 4))
        {
            return false;
        }

        rid = BitConverter.ToInt32(sidBytes, sidBytes.Length - 4);
        return true;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr token,
        int infoClass,
        IntPtr info,
        int length,
        out int returnedLength);

    [DllImport("advapi32.dll")]
    private static extern int GetLengthSid(IntPtr sid);
}
