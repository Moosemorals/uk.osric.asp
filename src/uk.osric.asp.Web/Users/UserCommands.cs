// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Users;

using Microsoft.Extensions.DependencyInjection;

/// <summary>The account command that runs before the web host starts.</summary>
internal static class UserCommands {

    /// <summary>The command word that selects the account tool.</summary>
    public const string Name = "users";

    private const string PasswordStdinOption = "--password-stdin";

    /// <summary>Whether the process arguments are an account command.</summary>
    /// <param name="args">The process arguments.</param>
    public static bool IsCommand(string[] args) {
        return args.Length > 0 && string.Equals(args[0], Name, StringComparison.Ordinal);
    }

    /// <summary>Runs an account command against the application's services.</summary>
    /// <param name="args">The arguments after <c>users</c>.</param>
    /// <param name="services">The application's service provider.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> RunAsync(string[] args, IServiceProvider services, CancellationToken cancellationToken) {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        return await RunAsync(
            args,
            scope.ServiceProvider.GetRequiredService<IUserStore>(),
            scope.ServiceProvider.GetRequiredService<IPasswordService>(),
            scope.ServiceProvider.GetRequiredService<IUserConsole>(),
            cancellationToken);
    }

    internal static async Task<int> RunAsync(
        string[] args,
        IUserStore users,
        IPasswordService passwords,
        IUserConsole console,
        CancellationToken cancellationToken) {

        if (args.Length == 0) {
            return Usage(console);
        }

        string command = args[0];
        string[] rest = args[1..];

        try {
            return command switch {
                "add" => await AddAsync(rest, users, passwords, console, cancellationToken),
                "passwd" => await PasswdAsync(rest, users, passwords, console, cancellationToken),
                "list" => await ListAsync(rest, users, console, cancellationToken),
                "remove" => await RemoveAsync(rest, users, console, cancellationToken),
                _ => Usage(console),
            };
        } catch (ArgumentException exception) {
            console.WriteError(exception.Message);
            return 1;
        }
    }

    private static async Task<int> AddAsync(
        string[] args,
        IUserStore users,
        IPasswordService passwords,
        IUserConsole console,
        CancellationToken cancellationToken) {

        if (!TryGetAccount(args, console, out string name, out bool passwordFromStdin)) {
            return 2;
        }

        string normalized = UserName.Normalize(name);
        string password = ReadNewPassword(console, passwordFromStdin);
        PasswordPolicy.Validate(password);

        LocalUser candidate = new() {
            UserName = normalized,
            PasswordHash = string.Empty,
            SecurityStamp = SecurityStamp.Create(),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        LocalUser user = new() {
            UserName = candidate.UserName,
            PasswordHash = passwords.Hash(candidate, password),
            SecurityStamp = candidate.SecurityStamp,
            CreatedAt = candidate.CreatedAt,
        };

        if (!await users.TryAddAsync(user, cancellationToken)) {
            console.WriteError($"An account named '{normalized}' already exists.");
            return 1;
        }

        console.WriteLine($"Added '{normalized}'.");
        return 0;
    }

    private static async Task<int> PasswdAsync(
        string[] args,
        IUserStore users,
        IPasswordService passwords,
        IUserConsole console,
        CancellationToken cancellationToken) {

        if (!TryGetAccount(args, console, out string name, out bool passwordFromStdin)) {
            return 2;
        }

        string normalized = UserName.Normalize(name);
        LocalUser? user = await users.FindAsync(normalized, cancellationToken);

        if (user is null) {
            console.WriteError($"No account named '{normalized}'.");
            return 1;
        }

        string password = ReadNewPassword(console, passwordFromStdin);
        PasswordPolicy.Validate(password);

        bool changed = await users.ChangePasswordAsync(
            normalized,
            passwords.Hash(user, password),
            SecurityStamp.Create(),
            cancellationToken);

        if (!changed) {
            console.WriteError($"No account named '{normalized}'.");
            return 1;
        }

        console.WriteLine($"Password changed for '{normalized}'.");
        return 0;
    }

    private static async Task<int> ListAsync(
        string[] args,
        IUserStore users,
        IUserConsole console,
        CancellationToken cancellationToken) {

        if (args.Length > 0) {
            return Usage(console);
        }

        IReadOnlyList<LocalUser> accounts = await users.ListAsync(cancellationToken);

        if (accounts.Count == 0) {
            console.WriteLine("No accounts.");
            return 0;
        }

        foreach (LocalUser user in accounts) {
            console.WriteLine($"{user.UserName} (created {user.CreatedAt:yyyy-MM-dd})");
        }

        return 0;
    }

    private static async Task<int> RemoveAsync(
        string[] args,
        IUserStore users,
        IUserConsole console,
        CancellationToken cancellationToken) {

        if (!TryGetAccount(args, console, out string name, out _)) {
            return 2;
        }

        string normalized = UserName.Normalize(name);

        if (!await users.RemoveAsync(normalized, cancellationToken)) {
            console.WriteError($"No account named '{normalized}'.");
            return 1;
        }

        console.WriteLine($"Removed '{normalized}'.");
        return 0;
    }

    private static bool TryGetAccount(string[] args, IUserConsole console, out string name, out bool passwordFromStdin) {
        passwordFromStdin = args.Contains(PasswordStdinOption, StringComparer.Ordinal);

        string[] positional = [.. args.Where(argument => !string.Equals(argument, PasswordStdinOption, StringComparison.Ordinal))];

        if (positional.Length != 1 || string.IsNullOrWhiteSpace(positional[0])) {
            name = string.Empty;
            _ = Usage(console);
            return false;
        }

        name = positional[0];
        return true;
    }

    private static string ReadNewPassword(IUserConsole console, bool passwordFromStdin) {
        string password = console.ReadPassword("Password: ") ?? string.Empty;

        if (passwordFromStdin) {
            return password;
        }

        string confirmation = console.ReadPassword("Confirm password: ") ?? string.Empty;

        if (!string.Equals(password, confirmation, StringComparison.Ordinal)) {
            throw new ArgumentException("The passwords do not match.");
        }

        return password;
    }

    private static int Usage(IUserConsole console) {
        console.WriteError(
            $"""
            usage: {Name} <command>
              {Name} add <name> [--password-stdin]
              {Name} passwd <name> [--password-stdin]
              {Name} list
              {Name} remove <name>
            """);

        return 2;
    }
}
