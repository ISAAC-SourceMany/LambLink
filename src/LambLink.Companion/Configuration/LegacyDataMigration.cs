namespace LambLink.Companion.Configuration;

internal static class LegacyDataMigration
{
    public static int CopyMissingFiles(string legacyDirectory, string targetDirectory)
    {
        var legacyFullPath = Path.GetFullPath(legacyDirectory);
        var targetFullPath = Path.GetFullPath(targetDirectory);
        if (string.Equals(legacyFullPath, targetFullPath, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(legacyFullPath))
        {
            return 0;
        }

        Directory.CreateDirectory(targetFullPath);
        return CopyMissingFilesRecursive(legacyFullPath, targetFullPath);
    }

    private static int CopyMissingFilesRecursive(string sourceDirectory, string targetDirectory)
    {
        var copied = 0;
        Directory.CreateDirectory(targetDirectory);

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory))
        {
            var targetFile = Path.Combine(targetDirectory, Path.GetFileName(sourceFile));
            if (File.Exists(targetFile))
                continue;

            File.Copy(sourceFile, targetFile, overwrite: false);
            copied++;
        }

        foreach (var sourceChild in Directory.EnumerateDirectories(sourceDirectory))
        {
            var attributes = File.GetAttributes(sourceChild);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                continue;

            copied += CopyMissingFilesRecursive(
                sourceChild,
                Path.Combine(targetDirectory, Path.GetFileName(sourceChild)));
        }

        return copied;
    }
}
