using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CedarPolicyTool;

/// <summary>Single pass over Cedar text that cuts it into statements and normalizes each one onto a single line.</summary>
internal sealed class CedarLexer(string source)
{
    private const string IdKey = "id";

    private readonly List<AgentPolicy> _policies = [];
    private readonly List<CedarSourceError> _errors = [];
    private readonly Dictionary<string, int> _idLines = new(StringComparer.Ordinal);
    private readonly Stack<(char Open, int Line, int Column)> _open = new();
    private readonly List<Annotation> _annotations = [];
    private readonly StringBuilder _body = new();

    private int _index;
    private int _line = 1;
    private int _column = 1;
    private int _startLine;
    private int _startColumn;
    private int _statements;
    private bool _pendingSpace;
    private bool _failed;

    private sealed record Annotation(string Key, string Raw, int Line);

    public SplitResult Run()
    {
        if (source.Length > 0 && source[0] == '﻿')
        {
            _index = 1;
        }

        while (_index < source.Length)
        {
            Step();
        }

        Finish();
        return new SplitResult(_policies, _errors);
    }

    private char Peek(int offset = 0) => _index + offset < source.Length ? source[_index + offset] : '\0';

    private void Advance()
    {
        if (source[_index] == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }

        _index++;
    }

    private void Error(int line, int column, string message)
    {
        _errors.Add(new CedarSourceError(line, column, message));
        _failed = true;
    }

    private void MarkStart()
    {
        if (_startLine == 0)
        {
            _startLine = _line;
            _startColumn = _column;
        }
    }

    private void Step()
    {
        var c = Peek();
        if (c == '/' && Peek(1) == '/')
        {
            SkipComment();
            _pendingSpace = true;
        }
        else if (char.IsWhiteSpace(c))
        {
            Advance();
            _pendingSpace = true;
        }
        else if (c == '@' && _body.Length == 0)
        {
            ReadAnnotation();
        }
        else if (c == '"')
        {
            ReadStringIntoBody();
        }
        else
        {
            ReadPunctuationOrText(c);
        }
    }

    private void ReadPunctuationOrText(char c)
    {
        MarkStart();
        if (c is '(' or '[' or '{')
        {
            _open.Push((c, _line, _column));
        }
        else if (c is ')' or ']' or '}')
        {
            Close(c);
        }

        AppendToBody(c.ToString());
        Advance();

        if (c == ';' && _open.Count == 0)
        {
            EndStatement();
        }
    }

    private void Close(char closer)
    {
        if (_open.Count == 0)
        {
            Error(_line, _column, $"unexpected '{closer}'");
            return;
        }

        var (open, line, _) = _open.Pop();
        var expected = open switch { '(' => ')', '[' => ']', _ => '}' };
        if (expected != closer)
        {
            Error(_line, _column, string.Create(CultureInfo.InvariantCulture, $"'{closer}' does not match '{open}' opened on line {line}"));
        }
    }

    private void AppendToBody(string text)
    {
        if (_pendingSpace && _body.Length > 0 && _body[^1] is not ('(' or '[') && text[0] is not (')' or ']'))
        {
            _body.Append(' ');
        }

        _pendingSpace = false;
        _body.Append(text);
    }

    private void SkipComment()
    {
        while (_index < source.Length && source[_index] != '\n')
        {
            Advance();
        }
    }

    private void SkipTrivia()
    {
        while (_index < source.Length)
        {
            if (char.IsWhiteSpace(Peek()))
            {
                Advance();
            }
            else if (Peek() == '/' && Peek(1) == '/')
            {
                SkipComment();
            }
            else
            {
                return;
            }
        }
    }

    /// <summary>Reads a string literal starting at the opening quote; returns the raw text between the quotes, or null when unterminated.</summary>
    private string? ReadStringLiteral()
    {
        var line = _line;
        var column = _column;
        Advance();
        var start = _index;
        while (_index < source.Length)
        {
            var c = source[_index];
            if (c == '"')
            {
                var raw = source[start.._index];
                Advance();
                return raw;
            }

            Advance();
            if (c == '\\' && _index < source.Length)
            {
                Advance();
            }
        }

        Error(line, column, "unterminated string literal");
        return null;
    }

    private void ReadStringIntoBody()
    {
        MarkStart();
        var raw = ReadStringLiteral();
        AppendToBody(raw is null ? "\"" : $"\"{raw}\"");
    }

    private void ReadAnnotation()
    {
        var line = _line;
        var column = _column;
        MarkStart();
        Advance();

        var nameStart = _index;
        while (char.IsAsciiLetterOrDigit(Peek()) || Peek() == '_')
        {
            Advance();
        }

        var key = source[nameStart.._index];
        SkipTrivia();
        if (key.Length == 0 || Peek() != '(')
        {
            Error(line, column, key.Length == 0 ? "annotation name expected after '@'" : $"expected '(' after @{key}");
            return;
        }

        Advance();
        SkipTrivia();
        if (Peek() != '"')
        {
            Error(line, column, $"@{key} needs a string value");
            return;
        }

        var raw = ReadStringLiteral();
        SkipTrivia();
        if (raw is null || Peek() != ')')
        {
            if (raw is not null)
            {
                Error(line, column, $"expected ')' to close @{key}");
            }

            return;
        }

        Advance();
        if (_annotations.Exists(a => a.Key == key))
        {
            Error(line, column, $"duplicate annotation @{key}");
            return;
        }

        _annotations.Add(new Annotation(key, raw, line));
    }

    private void EndStatement()
    {
        _statements++;
        if (!_failed)
        {
            Build();
        }

        _annotations.Clear();
        _body.Clear();
        _open.Clear();
        _startLine = 0;
        _pendingSpace = false;
        _failed = false;
    }

    private void Build()
    {
        var body = _body.ToString();
        if (!StartsWithEffect(body))
        {
            Error(_startLine, _startColumn, "a policy must start with permit or forbid");
            return;
        }

        var id = ReadId();
        if (id is null)
        {
            return;
        }

        var content = new StringBuilder();
        foreach (var annotation in _annotations.Where(a => a.Key != IdKey))
        {
            content.Append('@').Append(annotation.Key).Append("(\"").Append(annotation.Raw).Append("\") ");
        }

        _policies.Add(new AgentPolicy(id, content.Append(body).ToString()));
    }

    private static bool StartsWithEffect(string body)
    {
        foreach (var effect in (ReadOnlySpan<string>)["permit", "forbid"])
        {
            if (body.StartsWith(effect, StringComparison.Ordinal)
                && (body.Length == effect.Length || !(char.IsAsciiLetterOrDigit(body[effect.Length]) || body[effect.Length] == '_')))
            {
                return true;
            }
        }

        return false;
    }

    private string? ReadId()
    {
        var annotation = _annotations.Find(a => a.Key == IdKey);
        if (annotation is null)
        {
            Error(_startLine, _startColumn, "policy is missing an @id annotation");
            return null;
        }

        if (!TryUnescape(annotation.Raw, out var id))
        {
            Error(annotation.Line, _startColumn, "@id contains an unsupported escape sequence");
            return null;
        }

        if (id.Length == 0)
        {
            Error(annotation.Line, _startColumn, "@id must not be empty");
        }
        else if (id != id.Trim())
        {
            Error(annotation.Line, _startColumn, "@id must not have leading or trailing whitespace");
        }
        else if (_idLines.TryGetValue(id, out var firstLine))
        {
            Error(annotation.Line, _startColumn, string.Create(CultureInfo.InvariantCulture, $"duplicate id '{id}', first defined on line {firstLine}"));
        }
        else
        {
            _idLines[id] = annotation.Line;
            return id;
        }

        return null;
    }

    private static bool TryUnescape(string raw, out string value)
    {
        var result = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] != '\\')
            {
                result.Append(raw[i]);
                continue;
            }

            i++;
            var unescaped = i < raw.Length ? raw[i] switch
            {
                '\\' => '\\',
                '"' => '"',
                '\'' => '\'',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '0' => '\0',
                _ => (char?)null,
            } : null;
            if (unescaped is null)
            {
                value = string.Empty;
                return false;
            }

            result.Append(unescaped.Value);
        }

        value = result.ToString();
        return true;
    }

    private void Finish()
    {
        if (_startLine != 0)
        {
            var message = _body.Length == 0 && _annotations.Count > 0
                ? "annotation without a policy"
                : "policy is missing the terminating ';'";
            _errors.Add(new CedarSourceError(_startLine, _startColumn, message));
        }
        else if (_statements == 0)
        {
            _errors.Add(new CedarSourceError(1, 1, "no policies found"));
        }
    }
}
