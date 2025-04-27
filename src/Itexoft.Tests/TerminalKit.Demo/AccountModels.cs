// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.TerminalKit;
using Itexoft.TerminalKit.Validation;

namespace TerminalKit.Demo;

internal sealed class AccountCatalog
{
    [TerminalDisplay("Accounts")] public List<AccountRecord> Accounts { get; set; } = [];

    [TerminalDisplay("Refresh All")]
    public async Task RefreshAllAsync()
    {
        foreach (var account in this.Accounts)
            await account.RefreshAsync();
    }
}

internal enum AccountStatus
{
    Online,
    Offline,
    Failed,
}

[TerminalDisplay("Account")]
internal sealed class AccountRecord
{
    [TerminalDisplay("Login"), TerminalCharacterSet(Message = "Login must be alphanumeric."),
     TerminalRegex(@"^[A-Za-z0-9]{3,}$", Message = "Login must contain at least 3 characters.")]
    public string Login { get; set; } = string.Empty;

    [TerminalDisplay("Status")] public AccountStatus Status { get; set; } = AccountStatus.Offline;

    [TerminalDisplay("Last Attempt")] public DateTime LastAttemptUtc { get; set; } = DateTime.UtcNow;

    [TerminalDisplay("Notes"), TerminalRegex(@"^.{0,140}$", Message = "Notes must be 140 characters or less.")]
    public string Notes { get; set; } = string.Empty;

    [TerminalDisplay("Refresh Status")]
    public async Task RefreshAsync()
    {
        await Task.Delay(50);
        this.Status = AccountStatus.Online;
        this.LastAttemptUtc = DateTime.UtcNow;
    }
}

internal static class AccountRepository
{
    public static List<AccountRecord> Seed()
    {
        var now = DateTime.UtcNow;
        var statuses = Enum.GetValues<AccountStatus>();
        var records = new List<AccountRecord>(capacity: 100);

        for (var i = 0; i < 100; i++)
        {
            var login = $"user{i + 1:D3}";
            var status = statuses[i % statuses.Length];

            records.Add(
                new()
                {
                    Login = login,
                    Status = status,
                    LastAttemptUtc = now.AddMinutes(-(i * 3 % 240)),
                    Notes = $"Synthetic entry #{i + 1} ({status})",
                });
        }

        return records;
    }
}
