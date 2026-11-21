// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Formats.Tar;
using System.Text;
using uk.osric.asp.Core.Pages;

/// <summary>The seed command that runs before the web host starts.</summary>
/// <remarks>
/// <para>
/// The pack is edited in production and the repository falls behind it. The command's job is only to
/// get the store's bytes out and into the working tree; git is the merge tool and <c>git diff</c> is
/// the review. Its result is on standard output — a table for <c>list</c>, a tar for <c>pull</c> — and
/// everything said about the run is on standard error, so a log line can never land inside the tar.
/// </para>
/// <para>
/// <c>pull</c> writes only the keys whose stored source differs from the file this release ships.
/// <c>--all</c> writes every key the store has live, and <c>--missing</c> names the keys the pack ships
/// that the store does not have live: a pull cannot fix those, and an install or a restore must.
/// Nothing is deleted; deletion is git's job.
/// </para>
/// </remarks>
internal static class SeedCommands {

    /// <summary>The command word that selects the seed tool.</summary>
    public const string Name = "seeds";

    private const string AllOption = "--all";
    private const string MissingOption = "--missing";

    // The two zero blocks that end a tar. TarWriter writes no archive at all when it has no entry,
    // and an empty stream is not a tar; this is what makes "nothing changed" a valid one.
    private static readonly byte[] EndOfArchive = new byte[1024];

    /// <summary>Whether the process arguments are a seed command.</summary>
    /// <param name="args">The process arguments.</param>
    public static bool IsCommand(string[] args) {
        return args.Length > 0 && string.Equals(args[0], Name, StringComparison.Ordinal);
    }

    /// <summary>Runs a seed command against the application's services.</summary>
    /// <param name="args">The arguments after <c>seeds</c>.</param>
    /// <param name="services">The application's service provider.</param>
    /// <param name="output">The stream the command's result is written to, which is standard output.</param>
    /// <param name="error">Where the command says what it did, which is standard error.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> RunAsync(
        string[] args,
        IServiceProvider services,
        Stream output,
        TextWriter error,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        return await RunAsync(
            args,
            scope.ServiceProvider.GetRequiredService<PageSeeds>(),
            scope.ServiceProvider.GetRequiredService<IPageReadStore>(),
            output,
            error,
            cancellationToken);
    }

    internal static async Task<int> RunAsync(
        string[] args,
        PageSeeds seeds,
        IPageReadStore pages,
        Stream output,
        TextWriter error,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(pages);

        if (args.Length == 0) {
            return await UsageAsync(error, cancellationToken);
        }

        try {
            return args[0] switch {
                "list" => await ListAsync(args[1..], seeds, pages, output, cancellationToken),
                "pull" => await PullAsync(args[1..], seeds, pages, output, error, cancellationToken),
                _ => await UsageAsync(error, cancellationToken),
            };
        } catch (ArgumentException exception) {
            await error.WriteLineAsync(exception.Message, cancellationToken);
            return 1;
        }
    }

    private static async Task<int> ListAsync(
        string[] args,
        PageSeeds seeds,
        IPageReadStore pages,
        Stream output,
        CancellationToken cancellationToken) {
        ThrowIfOptions(args);

        Dictionary<string, string> verdicts = [];

        foreach (SeedFile seed in seeds.Files) {
            PageReadHead? head = await pages.GetHeadAsync(seed.Path.Key, cancellationToken);
            verdicts[seed.Path.Key] = head is null
                ? "new"
                : head.IsDeleted
                    ? "deleted"
                    : SeedComparison.Of(head.SeedHash, seed.Hash, head.ContentHash).Describe(seed.Hash).Verdict;
        }

        // A page whose seed this release no longer ships still has an anchor, and saying so is how
        // the owner sees that the pack moved on without them.
        PageReadSlice slice = await pages.ListAsync("/", after: null, int.MaxValue, cancellationToken: cancellationToken);
        foreach (PageReadHead head in slice.Rows) {
            if (head.SeedHash is not null && !verdicts.ContainsKey(head.Key)) {
                verdicts[head.Key] = SeedComparison.Of(head.SeedHash, null, head.ContentHash).Describe(null).Verdict;
            }
        }

        await using StreamWriter table = new(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);

        foreach ((string key, string verdict) in verdicts.OrderBy(item => item.Key, StringComparer.Ordinal)) {
            await table.WriteLineAsync($"{key}\t{verdict}");
        }

        await table.FlushAsync(cancellationToken);
        return 0;
    }

    private static async Task<int> PullAsync(
        string[] args,
        PageSeeds seeds,
        IPageReadStore pages,
        Stream output,
        TextWriter error,
        CancellationToken cancellationToken) {
        bool all = false;
        bool missing = false;
        foreach (string option in args) {
            switch (option) {
                case AllOption:
                    all = true;
                    break;
                case MissingOption:
                    missing = true;
                    break;
                default:
                    throw new ArgumentException($"'{option}' is not a seeds pull option. Use {AllOption} or {MissingOption}.");
            }
        }

        List<string> absent = [];
        int written = 0;

        using (TarWriter tar = new(output, leaveOpen: true)) {
            foreach (SeedFile seed in seeds.Files) {
                PageReadHead? head = await pages.GetHeadAsync(seed.Path.Key, cancellationToken);
                if (head is null) {
                    absent.Add($"{seed.Path.Key} missing");
                    continue;
                }

                if (head.IsDeleted) {
                    absent.Add($"{seed.Path.Key} deleted");
                    continue;
                }

                PageReadSource? source = await pages.GetSourceAsync(seed.Path.Key, cancellationToken);
                if (source is null) {
                    absent.Add($"{seed.Path.Key} missing");
                    continue;
                }

                if (!all && string.Equals(source.Template, seed.Source, StringComparison.Ordinal)) {
                    continue;
                }

                WriteEntry(tar, seed.Name, source.Template);
                written++;
            }
        }

        if (written == 0) {
            await output.WriteAsync(EndOfArchive, cancellationToken);
        }

        if (missing) {
            foreach (string line in absent) {
                await error.WriteLineAsync(line, cancellationToken);
            }
        }

        await error.WriteLineAsync(
            $"{written} {(written == 1 ? "page" : "pages")} written to the archive.", cancellationToken);
        return 0;
    }

    private static void WriteEntry(TarWriter tar, string name, string source) {
        PaxTarEntry entry = new(TarEntryType.RegularFile, name) {
            DataStream = new MemoryStream(Encoding.UTF8.GetBytes(source), writable: false),
            Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
        };
        tar.WriteEntry(entry);
    }

    private static void ThrowIfOptions(string[] args) {
        if (args.Length > 0) {
            throw new ArgumentException($"seeds list takes no options; '{args[0]}' was not expected.");
        }
    }

    private static async Task<int> UsageAsync(TextWriter error, CancellationToken cancellationToken) {
        await error.WriteLineAsync("usage: seeds list", cancellationToken);
        await error.WriteLineAsync("       seeds pull [--all] [--missing]", cancellationToken);
        return 1;
    }
}
