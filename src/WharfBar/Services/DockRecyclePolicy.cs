namespace WharfBar.Services;

/// <summary>
/// Whether a drop may be sent to the recycle bin, and whether the shell call actually
/// recycled it. A failed or aborted call is not success — the caller must not delete the
/// original itself.
/// </summary>
internal static class DockRecyclePolicy
{
    internal static bool CanRecycle(bool exists, bool attributesReadable, bool isDirectory, bool isReparse)
    {
        if (!exists || !attributesReadable)
        {
            return false;
        }

        return !(isDirectory && isReparse);
    }

    internal static bool ApiSucceeded(int result, bool aborted) => result == 0 && !aborted;
}
