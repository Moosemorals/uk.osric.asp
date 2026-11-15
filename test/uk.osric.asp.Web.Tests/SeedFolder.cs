// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

/// <summary>A throwaway seed folder, so a test reads a pack it wrote.</summary>
internal sealed class SeedFolder : IDisposable {

    /// <summary>Writes the named files under a fresh folder.</summary>
    /// <param name="files">Each file's name under the folder and its exact source.</param>
    public SeedFolder(params (string Name, string Source)[] files) {
        Directory = Path.Combine(Path.GetTempPath(), $"uk.osric.asp.seeds.{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(Directory);

        foreach ((string name, string source) in files) {
            string path = Path.Combine(Directory, name.Replace('/', Path.DirectorySeparatorChar));
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, source);
        }
    }

    /// <summary>The folder the seeds were written to.</summary>
    public string Directory { get; }

    public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
}
