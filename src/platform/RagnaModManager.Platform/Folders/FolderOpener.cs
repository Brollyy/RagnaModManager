using System.Diagnostics;
using RagnaModManager.Core.Common;

namespace RagnaModManager.Platform.Folders;

public sealed record OpenFolderCommand(string FileName, string Arguments, string DisplayCommand);

public sealed class FolderOpener
{
    public OpenFolderCommand BuildCommand(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
        {
            return new OpenFolderCommand("explorer.exe", $"\"{fullPath}\"", $"explorer.exe \"{fullPath}\"");
        }

        if (OperatingSystem.IsMacOS())
        {
            return new OpenFolderCommand("open", $"\"{fullPath}\"", $"open \"{fullPath}\"");
        }

        return new OpenFolderCommand("xdg-open", $"\"{fullPath}\"", $"xdg-open \"{fullPath}\"");
    }

    public Result Open(string path)
    {
        if (!Directory.Exists(path))
        {
            return Result.Fail($"Folder does not exist: {path}");
        }

        var command = BuildCommand(path);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = command.FileName,
                Arguments = command.Arguments,
                UseShellExecute = false
            });
            return Result.Ok();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Result.Fail($"Could not open folder: {ex.Message}");
        }
    }
}
