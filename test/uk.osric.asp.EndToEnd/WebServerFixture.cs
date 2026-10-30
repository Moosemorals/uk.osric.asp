namespace uk.osric.asp.EndToEnd;

using NUnit.Framework;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

/// <summary>Runs the real web app once for the whole end-to-end run.</summary>
[SetUpFixture]
public class WebServerFixture {
    private const int ReadyTimeoutSeconds = 60;

    private static readonly StringBuilder s_serverLog = new();

    private Process? _server;

    public static Uri BaseAddress { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task StartWebApp() {
        InstallBrowsers();

        string repoRoot = ReadRepoRoot();
        string project = Path.Combine(repoRoot, "src", "uk.osric.asp.Web", "uk.osric.asp.Web.csproj");
        BaseAddress = new Uri($"http://127.0.0.1:{FindFreePort()}");

        ProcessStartInfo startInfo = new("dotnet") {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = {
                "run",
                "--project",
                project,
                "--no-launch-profile",
                "--urls",
                BaseAddress.ToString(),
            },
        };

        _server = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the web app.");
        _server.OutputDataReceived += (_, line) => Append(line.Data);
        _server.ErrorDataReceived += (_, line) => Append(line.Data);
        _server.BeginOutputReadLine();
        _server.BeginErrorReadLine();

        await WaitUntilReadyAsync(_server).ConfigureAwait(false);
    }

    [OneTimeTearDown]
    public void StopWebApp() {
        if (_server is null || _server.HasExited) {
            return;
        }

        // dotnet run starts the app as a child process, so the whole tree has to go.
        _server.Kill(entireProcessTree: true);
        _server.WaitForExit();
    }

    private static void InstallBrowsers() {
        // The .NET Playwright installer normally ships as a PowerShell script. Calling the same
        // entry point keeps end-to-end runs working on Linux without requiring pwsh, and Playwright
        // skips the download when the browser is already installed.
        Environment.SetEnvironmentVariable("PLAYWRIGHT_DRIVER_SEARCH_PATH", AppContext.BaseDirectory);

        int exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exitCode != 0) {
            throw new InvalidOperationException(
                $"Playwright could not install Chromium (exit code {exitCode}).");
        }
    }

    private static int FindFreePort() {
        // Binding to port 0 asks the OS for a free port. The window before the app binds it
        // is a fine trade for tests, and avoids a hard-coded port clashing with a dev server.
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }

    private static async Task WaitUntilReadyAsync(Process server) {
        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(5) };
        DateTime deadline = DateTime.UtcNow.AddSeconds(ReadyTimeoutSeconds);

        while (DateTime.UtcNow < deadline) {
            if (server.HasExited) {
                throw new InvalidOperationException(
                    $"The web app exited with code {server.ExitCode} before it accepted requests.{Environment.NewLine}{s_serverLog}");
            }

            try {
                using HttpResponseMessage response = await client.GetAsync(BaseAddress).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) {
                    return;
                }
            } catch (HttpRequestException) {
                // Nothing is listening yet.
            } catch (TaskCanceledException) {
                // A cold start can outlast a single request timeout.
            }

            await Task.Delay(250).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"The web app did not respond within {ReadyTimeoutSeconds} seconds.{Environment.NewLine}{s_serverLog}");
    }

    private static void Append(string? line) {
        if (line is null) {
            return;
        }

        // Output and error arrive on separate threads.
        lock (s_serverLog) {
            s_serverLog.AppendLine(line);
        }
    }

    private static string ReadRepoRoot() {
        string? root = typeof(WebServerFixture).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "RepoRoot")
            ?.Value;

        if (root is null) {
            throw new InvalidOperationException(
                "The RepoRoot assembly metadata is missing; check Directory.Build.targets.");
        }

        return root;
    }
}
