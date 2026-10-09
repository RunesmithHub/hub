using System.Diagnostics.CodeAnalysis;

namespace RunesmithHub.Protocol.Manifests;

/// <summary>Checks SPDX license expressions with the operators <c>AND</c>, <c>OR</c>, <c>WITH</c> and parentheses.</summary>
public static class SpdxExpression
{
    /// <summary>Checks an expression's syntax and that every license in it passes <paramref name="isApproved"/>.</summary>
    /// <returns>Null when valid, otherwise why not.</returns>
    public static string? Check(string expression, Func<string, bool> isApproved)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(isApproved);
        var parser = new Parser(Tokenize(expression), isApproved);
        return parser.TryParse(out var error) ? null : error;
    }

    private static List<string> Tokenize(string expression)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var c in expression)
        {
            if (c is '(' or ')' || char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                    tokens.Add(current.ToString());
                current.Clear();
                if (!char.IsWhiteSpace(c))
                    tokens.Add(c.ToString());
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());
        return tokens;
    }

    private sealed class Parser(List<string> tokens, Func<string, bool> isApproved)
    {
        private int _position;

        public bool TryParse([NotNullWhen(false)] out string? error)
        {
            if (tokens.Count == 0)
            {
                error = "The license expression is empty.";
                return false;
            }

            if (!TryExpression(out error))
                return false;
            if (_position < tokens.Count)
            {
                error = $"Unexpected \"{tokens[_position]}\" in the license expression.";
                return false;
            }

            return true;
        }

        private bool TryExpression([NotNullWhen(false)] out string? error)
        {
            if (!TryAnd(out error))
                return false;
            while (Accept("OR"))
            {
                if (!TryAnd(out error))
                    return false;
            }

            return true;
        }

        private bool TryAnd([NotNullWhen(false)] out string? error)
        {
            if (!TryWith(out error))
                return false;
            while (Accept("AND"))
            {
                if (!TryWith(out error))
                    return false;
            }

            return true;
        }

        private bool TryWith([NotNullWhen(false)] out string? error)
        {
            if (!TryAtom(out error))
                return false;
            if (!Accept("WITH"))
                return true;
            if (_position < tokens.Count && IsIdentifier(tokens[_position]))
            {
                _position++;
                return true;
            }

            error = "WITH must be followed by a license exception ID.";
            return false;
        }

        private bool TryAtom([NotNullWhen(false)] out string? error)
        {
            if (Accept("("))
            {
                if (!TryExpression(out error))
                    return false;
                if (Accept(")"))
                    return true;
                error = "A parenthesis in the license expression is not closed.";
                return false;
            }

            if (_position >= tokens.Count)
            {
                error = "The license expression ends too early.";
                return false;
            }

            var token = tokens[_position];
            if (!IsIdentifier(token) || token is "AND" or "OR" or "WITH")
            {
                error = $"\"{token}\" is not an SPDX license ID.";
                return false;
            }

            _position++;
            var id = token.EndsWith('+') ? token[..^1] : token;
            if (!isApproved(id))
            {
                error = $"\"{id}\" is not an OSI-approved license.";
                return false;
            }

            error = null;
            return true;
        }

        private bool Accept(string token)
        {
            if (_position >= tokens.Count || !string.Equals(tokens[_position], token, StringComparison.Ordinal))
                return false;
            _position++;
            return true;
        }

        private static bool IsIdentifier(string token)
        {
            var body = token.EndsWith('+') ? token[..^1] : token;
            return body.Length > 0 && body.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.');
        }
    }
}
