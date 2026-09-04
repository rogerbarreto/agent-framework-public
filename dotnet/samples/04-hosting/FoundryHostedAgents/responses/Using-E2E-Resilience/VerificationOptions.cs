// Copyright (c) Microsoft. All rights reserved.

using System.Globalization;

internal sealed record VerificationOptions(
    int Target,
    int InterruptAfterCount)
{
    public static VerificationOptions Parse(string[] args)
    {
        int target = 20;
        int? interruptAfterCount = null;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case "--target":
                    target = ReadInteger(args, ref index, argument);
                    break;
                case "--interrupt-after-count":
                    interruptAfterCount = ReadInteger(args, ref index, argument);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{argument}'.");
            }
        }

        int resolvedInterruptAfterCount = interruptAfterCount ?? Math.Max(1, target / 2);
        if (target < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(args),
                "Target must be at least 2.");
        }

        if (resolvedInterruptAfterCount < 1 || resolvedInterruptAfterCount >= target)
        {
            throw new ArgumentOutOfRangeException(
                nameof(args),
                "Interrupt count must be greater than zero and less than the target.");
        }

        return new(target, resolvedInterruptAfterCount);
    }

    private static int ReadInteger(
        string[] args,
        ref int index,
        string argument)
    {
        if (++index >= args.Length
            || !int.TryParse(
                args[index],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int value))
        {
            throw new ArgumentException(
                $"Argument '{argument}' requires an integer value.");
        }

        return value;
    }
}
