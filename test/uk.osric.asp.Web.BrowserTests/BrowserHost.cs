// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.BrowserTests;

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

/// <summary>The assistant limits a browser test host runs with.</summary>
internal sealed record BrowserHostSettings {

    /// <summary>The provider deadline for one step, measured as silence; small to prove a stall matters.</summary>
    public int RequestTimeoutSeconds { get; init; } = 120;

    /// <summary>The whole-run deadline.</summary>
    public int RunTimeoutSeconds { get; init; } = 300;

    /// <summary>How often the event stream sends a keep-alive.</summary>
    public int HeartbeatSeconds { get; init; } = 1;

    /// <summary>A provider to talk to instead of the scripted one; null starts the scripted provider.</summary>
    public Uri? ProviderBaseUrl { get; init; }

    /// <summary>The provider key to send, when a real provider needs one.</summary>
    public string? ApiKey { get; init; }

    /// <summary>The model name the conversation is recorded with.</summary>
    public string Model { get; init; } = "stub-model";

    /// <summary>The provider's output cap, small when a test wants a truncated answer.</summary>
    public int MaxOutputTokens { get; init; } = 4096;
}

/// <summary>
/// The real web app, running as a child process on a loopback port with a throwaway SQLite database
/// and a scripted provider. Everything the browser sees is the production pipeline, which is what a
/// browser test is for; nothing here is a test double except the model and the database file.
/// </summary>
internal sealed class BrowserHost : IAsyncDisposable {

    /// <summary>The account every browser test signs in as.</summary>
    public const string UserName = "editor";

    /// <summary>The password for <see cref="UserName"/>; long enough for the policy.</summary>
    public const string Password = "browser-test-password";

    private static readonly Lock s_gate = new();
    private static BrowserHost? s_shared;

    private readonly Process _process = new();
    private readonly StringBuilder _log = new();
    private readonly Lock _logLock = new();
    private readonly string _directory;

    private BrowserHost(string directory) => _directory = directory;

    /// <summary>The host every fixture uses unless it needs different limits.</summary>
    public static async Task<BrowserHost> SharedAsync() {
        lock (s_gate) {
            if (s_shared is { } existing) {
                return existing;
            }
        }

        BrowserHost started = await StartAsync(new BrowserHostSettings());
        lock (s_gate) {
            if (s_shared is null) {
                s_shared = started;
                return s_shared;
            }
        }

        // Another caller won the race; this one's process would otherwise run on unowned.
        await started.DisposeAsync();
        return s_shared;
    }

    /// <summary>Stops the shared host, which the suite owns.</summary>
    public static async Task StopSharedAsync() {
        BrowserHost? host;
        lock (s_gate) {
            host = s_shared;
            s_shared = null;
        }
        if (host is not null) {
            await host.DisposeAsync();
        }
    }

    /// <summary>Where the browser should point.</summary>
    public Uri BaseAddress { get; private set; } = null!;

    /// <summary>The scripted provider, when this host started one instead of using a real endpoint.</summary>
    public StubProvider? Provider { get; private set; }

    /// <summary>What the host has written to standard output and error so far.</summary>
    public string Log { get { lock (_logLock) { return _log.ToString(); } } }

    /// <summary>Starts a host with its own database, provider and port.</summary>
    public static async Task<BrowserHost> StartAsync(BrowserHostSettings settings) {
        string directory = Path.Combine(Path.GetTempPath(), $"uk.osric.asp.browser.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        BrowserHost host = new(directory);
        try {
            await host.LaunchAsync(settings);
            return host;
        } catch {
            await host.DisposeAsync();
            throw;
        }
    }

    /// <summary>Writes the host's log into the test output, for a failure that needs explaining.</summary>
    public void WriteDiagnostics(string to) {
        lock (_logLock) {
            Directory.CreateDirectory(to);
            File.WriteAllText(Path.Combine(to, "web.log"), _log.ToString());
        }
    }

    public async ValueTask DisposeAsync() {
        try {
            if (!_process.HasExited) {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        } catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException) {
        }
        _process.Dispose();
        if (Provider is not null) {
            await Provider.DisposeAsync();
        }
        try {
            Directory.Delete(_directory, recursive: true);
        } catch (IOException) {
        }
    }

    private async Task LaunchAsync(BrowserHostSettings settings) {
        string app = LocateApp();
        string assembly = Path.GetFileName(app);
        string workingDirectory = Path.GetDirectoryName(app)!;
        int port = FreePort();
        BaseAddress = new Uri($"http://127.0.0.1:{port}");
        if (settings.ProviderBaseUrl is null) {
            Provider = new StubProvider(FreePort());
        }

        Dictionary<string, string?> environment = new() {
            ["ASPNETCORE_ENVIRONMENT"] = "Staging",
            ["ConnectionStrings__Storage"] = $"Data Source={Path.Combine(_directory, "storage.db")}",
            ["Assistant__Enabled"] = "true",
            ["Assistant__ApiKey"] = settings.ApiKey ?? "browser-test-key",
            ["Assistant__BaseUrl"] = (settings.ProviderBaseUrl ?? new Uri(Provider!.BaseUrl)).ToString(),
            ["Assistant__Model"] = settings.Model,
            ["Assistant__MaxOutputTokens"] = settings.MaxOutputTokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Assistant__HeartbeatSeconds"] = settings.HeartbeatSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Assistant__RequestTimeoutSeconds"] = settings.RequestTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Assistant__RunTimeoutSeconds"] = settings.RunTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            // Nothing here can reach Prometheus, and the dashboard is not what these tests are about.
            ["Metrics__PrometheusUrl"] = "http://127.0.0.1:1",
            ["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information",
        };

        await RunToExitAsync(app, workingDirectory, environment, ["users", "add", UserName, "--password-stdin"], Password + Environment.NewLine);

        ProcessStartInfo start = new("dotnet", [assembly, "--urls", BaseAddress.ToString()]) {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach ((string key, string? value) in environment) {
            start.Environment[key] = value;
        }

        _process.StartInfo = start;
        _process.OutputDataReceived += (_, line) => Append(line.Data);
        _process.ErrorDataReceived += (_, line) => Append(line.Data);
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await WaitUntilListeningAsync();
    }

    private static async Task RunToExitAsync(string app, string workingDirectory, Dictionary<string, string?> environment, string[] args, string stdin) {
        ProcessStartInfo start = new("dotnet", [Path.GetFileName(app), .. args]) {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        foreach ((string key, string? value) in environment) {
            start.Environment[key] = value;
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("The account command did not start.");
        await process.StandardInput.WriteAsync(stdin);
        process.StandardInput.Close();
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) {
            throw new InvalidOperationException($"The account command failed with {process.ExitCode}: {output}{error}");
        }
    }

    private async Task WaitUntilListeningAsync() {
        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(5) };
        Stopwatch waited = Stopwatch.StartNew();
        while (waited.Elapsed < TimeSpan.FromSeconds(90)) {
            if (_process.HasExited) {
                throw new InvalidOperationException($"The web host exited with {_process.ExitCode}.{Environment.NewLine}{Log}");
            }
            try {
                using HttpResponseMessage response = await client.GetAsync(new Uri(BaseAddress, "/heath"));
                if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable) {
                    return;
                }
            } catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException) {
            }
            await Task.Delay(200);
        }
        throw new InvalidOperationException($"The web host did not start listening.{Environment.NewLine}{Log}");
    }

    private void Append(string? line) {
        if (line is null) {
            return;
        }
        lock (_logLock) {
            _log.AppendLine(line);
        }
    }

    private static int FreePort() {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>The repository root, embedded in the test assembly by the build.</summary>
    public static string RepoRoot() => typeof(BrowserHost).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "RepoRoot")
        .Value!;

    private static string LocateApp() {
        string root = RepoRoot();
        string binaries = Path.Combine(root, "artifacts", "bin", "uk.osric.asp.Web");
        if (!Directory.Exists(binaries)) {
            throw new InvalidOperationException($"{binaries} does not exist; run `make build` before the browser suite.");
        }

        FileInfo? newest = new DirectoryInfo(binaries)
            .EnumerateFiles("uk.osric.asp.Web.dll", SearchOption.AllDirectories)
            .Where(file => file.Directory?.Name is "debug" or "release" or "Debug" or "Release")
            .MaxBy(file => file.LastWriteTimeUtc);
        return newest?.FullName
            ?? throw new InvalidOperationException($"No built web app under {binaries}; run `make build` before the browser suite.");
    }
}
