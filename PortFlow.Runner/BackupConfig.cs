using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PortFlow.Runner;

public sealed class BackupConfig
{
    public string SourcePath { get; set; } = string.Empty;
    public string DestinationFolderName { get; set; } = "Backup";
    public bool Mirror { get; set; } = true;
    public List<string> Exclude { get; set; } = new();
    public string LogPath { get; set; } = @"C:\ProgramData\PortFlowBackup\logs\portflow.log";
    public bool StayRunning { get; set; } = true;

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
