using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PortFlow.Runner;

/// <summary>
/// Configuration for the PortFlow backup runner.
/// </summary>
/// <remarks>
/// This class is deserialized from JSON (typically <c>portflow.backup.json</c>).
/// Property names are case-insensitive.
/// </remarks>
public sealed class BackupConfig
{
    /// <summary>
    /// Local folder to back up from.
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Folder name created under the target drive root. If empty, files are copied to the drive root.
    /// </summary>
    public string DestinationFolderName { get; set; } = "Backup";

    /// <summary>
    /// Whether the copy should mirror the source (equivalent to robocopy /MIR).
    /// </summary>
    public bool Mirror { get; set; } = true;

    /// <summary>
    /// File exclusion patterns passed to robocopy (equivalent to /XF entries).
    /// </summary>
    public List<string> Exclude { get; set; } = new();

    /// <summary>
    /// Path to the log file.
    /// </summary>
    public string LogPath { get; set; } = @"C:\ProgramData\PortFlowBackup\logs\portflow.log";

    /// <summary>
    /// When true, the runner stays active and listens for USB insertion events.
    /// </summary>
    public bool StayRunning { get; set; } = true;

    /// <summary>
    /// When true, logs more robocopy output. When false (default), logs are summarized/throttled.
    /// </summary>
    public bool VerboseRobocopyLog { get; set; } = false;

    /// <summary>
    /// Loads configuration from disk and performs basic validation.
    /// </summary>
    /// <param name="path">Path to the JSON config file.</param>
    /// <returns>The parsed configuration.</returns>
    /// <exception cref="ArgumentException">Config path is null/empty.</exception>
    /// <exception cref="FileNotFoundException">Config file not found.</exception>
    /// <exception cref="InvalidOperationException">Config file is invalid or missing required fields.</exception>
    public static BackupConfig Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Config path is required.");
        if (!File.Exists(path)) throw new FileNotFoundException("Config file not found.", path);

        var json = File.ReadAllText(path);
        var cfg = JsonSerializer.Deserialize<BackupConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Config file could not be parsed.");

        if (string.IsNullOrWhiteSpace(cfg.SourcePath))
            throw new InvalidOperationException("Config missing required field: sourcePath");
        if (string.IsNullOrWhiteSpace(cfg.DestinationFolderName))
            throw new InvalidOperationException("Config missing required field: destinationFolderName");
        if (string.IsNullOrWhiteSpace(cfg.LogPath))
            throw new InvalidOperationException("Config missing required field: logPath");

        Directory.CreateDirectory(Path.GetDirectoryName(cfg.LogPath)!);

        return cfg;
    }
}
