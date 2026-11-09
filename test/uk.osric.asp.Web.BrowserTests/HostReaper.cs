// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.BrowserTests;

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

/// <summary>
/// Takes the host down when the test process goes away without being asked. Teardown runs on a
/// failure or on a timeout the runner is still in charge of, but a runner that kills or crashes the
/// test process leaves the host behind: it is a process of its own, holding a port, a database file
/// and a model queue, and the next run neither knows nor cares that it is there. So the host gets a
/// watcher that outlives this process by a second and does what the teardown did not get to.
/// </summary>
internal static class HostReaper {

    /// <summary>
    /// Waits for this process to go, then kills the host if the pid still names it; a pid that has
    /// been reused for something else is left alone. The output is sent to /dev/null first, so the
    /// watcher never holds the runner's pipe open after this process has gone.
    /// </summary>
    private const string WatchScript = """
        exec >/dev/null 2>&1
        while kill -0 "$2" 2>/dev/null; do
            kill -0 "$1" 2>/dev/null || exit 0
            sleep 1
        done
        case "$(ps -p "$1" -o args= 2>/dev/null)" in
            *uk.osric.asp.Web.dll*) kill -9 "$1" ;;
        esac
        """;

    /// <summary>
    /// Starts the watcher for <paramref name="host"/>; the caller disposes it once it has taken the
    /// host down itself. Null on a machine with no POSIX shell, where only the normal teardown runs.
    /// </summary>
    /// <param name="host">The host process to kill if this process dies first.</param>
    public static Process? Watch(Process host) {
        if (OperatingSystem.IsWindows()) {
            return null;
        }

        ProcessStartInfo start = new("/bin/sh") { UseShellExecute = false };
        start.ArgumentList.Add("-c");
        // $0 names the script in the shell's own error messages; the two arguments are the host and
        // this process, in the order the script reads them.
        start.ArgumentList.Add(WatchScript);
        start.ArgumentList.Add("host-reaper");
        start.ArgumentList.Add(host.Id.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

        try {
            return Process.Start(start);
        } catch (Exception exception) when (exception is Win32Exception or InvalidOperationException) {
            // No shell to start: the host is still killed by the teardown on every path that reaches
            // it, which is everything but a runner that kills this process outright.
            return null;
        }
    }
}
