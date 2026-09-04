// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Data.Sqlite;

internal static class IdempotentService
{
    public static async Task VerifyOperationsAsync(
        string databasePath,
        int expectedCount,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(
            $"Data Source={databasePath}");
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM countdown_operations;
            """;
        long actualCount =
            (long?)await command.ExecuteScalarAsync(cancellationToken)
            ?? 0;
        if (actualCount != expectedCount)
        {
            throw new InvalidOperationException(
                $"Expected {expectedCount} operations in SQLite, but found {actualCount}.");
        }

        Console.WriteLine(
            $"      SQLite contains all {actualCount} completed operations.");
    }
}
