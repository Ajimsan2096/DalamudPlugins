using System;
using System.Collections.Generic;
using LinguaBridge.Translation;

namespace LinguaBridge.Chat;

/// <summary>
/// Parses "/tr [:fr] [/p | /t First Last@World | /t &lt;t&gt;] text" into its parts.
/// </summary>
public sealed record OutgoingCommand(string ChannelPrefix, Language? Target, string Text)
{
    private static readonly HashSet<string> ChannelCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "/s", "/say", "/p", "/party", "/a", "/alliance", "/fc", "/freecompany", "/sh", "/shout", "/y", "/yell",
        "/e", "/echo", "/n", "/novice", "/pvpteam",
        "/l1", "/l2", "/l3", "/l4", "/l5", "/l6", "/l7", "/l8",
        "/linkshell1", "/linkshell2", "/linkshell3", "/linkshell4", "/linkshell5", "/linkshell6", "/linkshell7", "/linkshell8",
        "/cwl1", "/cwl2", "/cwl3", "/cwl4", "/cwl5", "/cwl6", "/cwl7", "/cwl8",
        "/cwlinkshell1", "/cwlinkshell2", "/cwlinkshell3", "/cwlinkshell4",
        "/cwlinkshell5", "/cwlinkshell6", "/cwlinkshell7", "/cwlinkshell8",
        "/r", "/reply",
    };

    private static readonly HashSet<string> TellCommands = new(StringComparer.OrdinalIgnoreCase) { "/t", "/tell" };

    public static OutgoingCommand? Parse(string args, out string? error)
    {
        error = null;
        var rest = args.Trim();
        var prefix = string.Empty;
        Language? target = null;

        // Language and channel can come in either order.
        for (var i = 0; i < 2 && rest.Length > 0; i++)
        {
            if (target == null && TryTakeLanguage(ref rest, out var lang, out error))
            {
                target = lang;
                continue;
            }
            if (error != null)
                return null;
            if (prefix.Length == 0 && rest.StartsWith('/'))
            {
                if (!TryTakeChannel(ref rest, out prefix, out error))
                    return null;
            }
        }

        if (rest.Length == 0)
        {
            error = "Nothing to translate.";
            return null;
        }

        return new OutgoingCommand(prefix, target, rest);
    }

    private static bool TryTakeLanguage(ref string rest, out Language? lang, out string? error)
    {
        lang = null;
        error = null;
        if (!rest.StartsWith(':'))
            return false;
        var token = NextToken(rest, out var after);
        lang = Languages.TryGet(token[1..]);
        if (lang == null)
        {
            error = $"Unknown language '{token[1..]}'. Use one of: {string.Join(", ", Languages.All.ConvertAll(l => ":" + l.Code.ToLowerInvariant()))}";
            return false;
        }
        rest = after;
        return true;
    }

    private static bool TryTakeChannel(ref string rest, out string prefix, out string? error)
    {
        prefix = string.Empty;
        error = null;
        var cmd = NextToken(rest, out var after);

        if (ChannelCommands.Contains(cmd))
        {
            prefix = cmd + " ";
            rest = after;
            return true;
        }

        if (TellCommands.Contains(cmd))
        {
            // "/t <t> text"  or  "/t First Last@World text"
            var first = NextToken(after, out var afterFirst);
            if (first.StartsWith('<') && first.EndsWith('>'))
            {
                prefix = $"{cmd} {first} ";
                rest = afterFirst;
                return true;
            }
            var second = NextToken(afterFirst, out var afterSecond);
            if (first.Length > 0 && second.Length > 0)
            {
                prefix = $"{cmd} {first} {second} ";
                rest = afterSecond;
                return true;
            }
            error = "Usage: /tr /t First Last@World message";
            return false;
        }

        error = $"Unsupported channel '{cmd}'. Use /s /p /a /fc /sh /y /l1-8 /cwl1-8 /t /r.";
        return false;
    }

    private static string NextToken(string s, out string rest)
    {
        s = s.TrimStart();
        var space = s.IndexOf(' ');
        if (space < 0)
        {
            rest = string.Empty;
            return s;
        }
        rest = s[(space + 1)..].TrimStart();
        return s[..space];
    }
}

internal static class ListExtensions
{
    public static List<TOut> ConvertAll<TIn, TOut>(this IReadOnlyList<TIn> list, Func<TIn, TOut> f)
    {
        var result = new List<TOut>(list.Count);
        foreach (var item in list)
            result.Add(f(item));
        return result;
    }
}
