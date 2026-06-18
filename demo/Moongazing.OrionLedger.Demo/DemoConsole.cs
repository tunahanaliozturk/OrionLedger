namespace Moongazing.OrionLedger.Demo;

using Moongazing.OrionLedger.Keys;

/// <summary>
/// Small console-formatting helpers so every feature demo prints the same way.
/// </summary>
internal static class DemoConsole
{
    public static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 72));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('=', 72));
    }

    public static void Step(string message) => Console.WriteLine($"  - {message}");

    public static void Detail(string label, string value) =>
        Console.WriteLine($"      {label,-16}: {value}");

    public static void Result(string message) => Console.WriteLine($"  => {message}");

    public static void PrintVerification(string presented, ApiKeyVerification verification)
    {
        Console.WriteLine($"      presented       : {Mask(presented)}");
        Console.WriteLine($"      status          : {verification.Status}");
        Console.WriteLine($"      isValid         : {verification.IsValid}");
        Console.WriteLine($"      record returned : {(verification.Record is not null ? $"yes ({verification.Record.Name})" : "no")}");
    }

    /// <summary>
    /// Show a token without dumping its full secret to the console: keep the display prefix and
    /// elide the rest. The plaintext is shown in full only at issuance, deliberately.
    /// </summary>
    public static string Mask(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return "(empty)";
        }

        return token.Length <= ApiKeyGenerator.DisplayPrefixLength
            ? token
            : $"{token[..ApiKeyGenerator.DisplayPrefixLength]}...({token.Length} chars)";
    }
}
