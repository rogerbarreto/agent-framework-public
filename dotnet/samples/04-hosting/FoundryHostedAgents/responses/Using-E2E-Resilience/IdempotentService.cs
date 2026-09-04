// Copyright (c) Microsoft. All rights reserved.

internal static class IdempotentService
{
    private static readonly Dictionary<string, string> s_executedOperations = [];

    public static string ExecuteOperation(string operationId)
    {
        if (s_executedOperations.TryGetValue(operationId, out string? result))
        {
            return result;
        }

        result = $"result:{operationId}";
        s_executedOperations.Add(operationId, result);

        return result;
    }
}
