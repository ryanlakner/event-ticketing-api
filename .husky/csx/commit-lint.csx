// Validates a commit message against Conventional Commits 1.0.0:
// https://www.conventionalcommits.org/en/v1.0.0/
//
// Runs from the commit-msg git hook and from CI, so both enforce identical rules.
// Usage: dotnet husky exec .husky/csx/commit-lint.csx --args <path-to-message-file>

#r "System.Linq"
#r "System.Collections"

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

const int MaxHeaderLength = 100;
const string Scissors = "# ------------------------ >8 ------------------------";

// The spec requires feat and fix; the rest follow the widely used Angular convention.
var types = new[]
{
    "build",
    "chore",
    "ci",
    "docs",
    "feat",
    "fix",
    "perf",
    "refactor",
    "revert",
    "style",
    "test",
};

// Header: type(optional scope)!: description
var header = new Regex(
    @"^(?<type>[A-Za-z]+)(?:\((?<scope>[^()\r\n]+)\))?(?<breaking>!)?: (?<description>\S.*)$"
);

// Messages git writes itself, and autosquash markers, are allowed through untouched.
var generated = new Regex(@"^(Merge |Revert ""|(fixup|squash|amend)! )");

// Drop git's comment lines and anything below the scissors line of `git commit -v`.
var lines = File.ReadAllLines(Args[0])
    .TakeWhile(line => line != Scissors)
    .Where(line => !line.StartsWith('#'))
    .Select(line => line.TrimEnd())
    .ToList();
while (lines.Count > 0 && lines[^1].Length == 0)
{
    lines.RemoveAt(lines.Count - 1);
}

var errors = new List<string>();

if (lines.Count == 0 || lines[0].Length == 0)
{
    errors.Add("The commit message is empty.");
}
else if (!generated.IsMatch(lines[0]))
{
    var subject = lines[0];
    var match = header.Match(subject);

    if (!match.Success)
    {
        errors.Add($"Header must look like 'type(scope): description' but was: '{subject}'");
    }
    else if (match.Groups["type"].Value is var type && type != type.ToLowerInvariant())
    {
        errors.Add($"Write the type in lowercase: '{type.ToLowerInvariant()}', not '{type}'.");
    }
    else if (!types.Contains(match.Groups["type"].Value))
    {
        errors.Add(
            $"Unknown type '{match.Groups["type"].Value}'. Use one of: {string.Join(", ", types)}."
        );
    }

    if (subject.Length > MaxHeaderLength)
    {
        errors.Add(
            $"Header is {subject.Length} characters; keep it to {MaxHeaderLength} or fewer."
        );
    }

    if (lines.Count > 1 && lines[1].Length != 0)
    {
        errors.Add("Leave a blank line between the header and the body.");
    }

    // The spec requires the breaking-change footer token to be uppercase.
    foreach (var line in lines.Skip(1))
    {
        var token = Regex.Match(line, @"^(breaking[ -]change):", RegexOptions.IgnoreCase);
        if (token.Success && token.Groups[1].Value is not ("BREAKING CHANGE" or "BREAKING-CHANGE"))
        {
            errors.Add($"Write '{token.Groups[1].Value}' as 'BREAKING CHANGE' (uppercase).");
        }
    }
}

if (errors.Count == 0)
{
    return 0;
}

Console.ForegroundColor = ConsoleColor.Red;
Console.WriteLine(
    "Commit message does not follow Conventional Commits (https://www.conventionalcommits.org/en/v1.0.0/):"
);
foreach (var error in errors)
{
    Console.WriteLine($"  - {error}");
}
Console.ResetColor();
Console.WriteLine();
Console.WriteLine("Examples:");
Console.WriteLine("  feat(reservations): allow customers to cancel pending holds");
Console.WriteLine("  fix: release seats when an event is cancelled");
Console.WriteLine("  feat(api)!: rename seatsAvailable to availableSeats");
return 1;
