namespace WharfBar.Services;

internal static class NightLightBlob
{
    private static readonly byte[] Signature = [0x43, 0x42, 0x01, 0x00];
    private static readonly byte[] ActiveFlag = [0x02, 0x01];

    /// <summary>Best-effort: CloudStore blob layout değişebilir; yapı tanınmazsa false döner.</summary>
    internal static bool TryParse(byte[]? data, out bool enabled)
    {
        enabled = false;
        if (data is null || data.Length < 8)
        {
            return false;
        }

        if (!TryFindFlag(data, out int flagIndex, out enabled))
        {
            return false;
        }

        return flagIndex >= 0;
    }

    /// <summary>İkinci imzadan hemen sonra 02 01 bayrağı varsa enabled=true; bayrak yoksa false.</summary>
    internal static bool TryFindFlag(byte[] data, out int flagIndex, out bool enabled)
    {
        flagIndex = -1;
        enabled = false;

        int first = IndexOf(data, Signature, 0);
        if (first < 0)
        {
            return false;
        }

        int second = IndexOf(data, Signature, first + Signature.Length);
        if (second < 0)
        {
            return false;
        }

        flagIndex = second + Signature.Length;
        if (flagIndex >= data.Length)
        {
            return false;
        }

        enabled = flagIndex + 1 < data.Length
            && data[flagIndex] == ActiveFlag[0]
            && data[flagIndex + 1] == ActiveFlag[1];
        return true;
    }

    /// <summary>Bayrağı ekler/çıkarır; bayrak konumu bulunamazsa girdiyi olduğu gibi döner.</summary>
    internal static byte[] ToggleFlag(byte[] data, bool enable)
    {
        if (!TryFindFlag(data, out int flagIndex, out bool currentlyEnabled)
            || currentlyEnabled == enable)
        {
            return data;
        }

        if (enable)
        {
            var result = new byte[data.Length + ActiveFlag.Length];
            Array.Copy(data, result, flagIndex);
            ActiveFlag.CopyTo(result, flagIndex);
            Array.Copy(data, flagIndex, result, flagIndex + ActiveFlag.Length, data.Length - flagIndex);
            return result;
        }

        var trimmed = new byte[data.Length - ActiveFlag.Length];
        Array.Copy(data, trimmed, flagIndex);
        Array.Copy(data, flagIndex + ActiveFlag.Length, trimmed, flagIndex, data.Length - flagIndex - ActiveFlag.Length);
        return trimmed;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        for (int i = start; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}