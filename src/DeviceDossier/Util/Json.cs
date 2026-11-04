using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DeviceDossier.Util
{
    public static class Json
    {
        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            var reader = new Reader(text);
            reader.SkipWhitespace();
            var value = reader.ReadValue(0);
            reader.SkipWhitespace();
            if (!reader.End) throw new FormatException("Unexpected data after the JSON value at position " + reader.Position + ".");
            return value;
        }

        public static Dictionary<string, object> ParseObject(string text)
        {
            return Parse(text) as Dictionary<string, object>;
        }

        private sealed class Reader
        {
            private const int MaxDepth = 256;
            private readonly string _s;
            private int _i;

            public Reader(string s)
            {
                _s = s;
                if (_s.Length > 0 && _s[0] == '﻿') _i = 1;
            }

            public bool End { get { return _i >= _s.Length; } }
            public int Position { get { return _i; } }

            public object ReadValue(int depth)
            {
                if (depth > MaxDepth) throw new FormatException("JSON is nested too deeply.");
                if (End) throw new FormatException("Unexpected end of JSON.");
                char c = _s[_i];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw new FormatException("Unexpected character '" + c + "' at position " + _i + ".");
                }
            }

            private Dictionary<string, object> ReadObject(int depth)
            {
                var map = new Dictionary<string, object>(StringComparer.Ordinal);
                _i++;
                SkipWhitespace();
                if (Peek('}')) { _i++; return map; }
                while (true)
                {
                    SkipWhitespace();
                    if (End || _s[_i] != '"') throw new FormatException("Expected a property name at position " + _i + ".");
                    string key = ReadString();
                    SkipWhitespace();
                    Require(':');
                    SkipWhitespace();
                    map[key] = ReadValue(depth + 1);
                    SkipWhitespace();
                    if (Peek(',')) { _i++; SkipWhitespace(); if (Peek('}')) { _i++; return map; } continue; }
                    Require('}');
                    return map;
                }
            }

            private object[] ReadArray(int depth)
            {
                var list = new List<object>();
                _i++;
                SkipWhitespace();
                if (Peek(']')) { _i++; return list.ToArray(); }
                while (true)
                {
                    SkipWhitespace();
                    list.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (Peek(',')) { _i++; SkipWhitespace(); if (Peek(']')) { _i++; return list.ToArray(); } continue; }
                    Require(']');
                    return list.ToArray();
                }
            }

            private string ReadString()
            {
                _i++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (End) throw new FormatException("Unterminated string.");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (End) throw new FormatException("Unterminated escape sequence.");
                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw new FormatException("Invalid unicode escape.");
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: throw new FormatException("Invalid escape '\\" + e + "'.");
                    }
                }
            }

            private object ReadNumber()
            {
                int start = _i;
                if (Peek('-')) _i++;
                while (!End && (char.IsDigit(_s[_i]) || _s[_i] == '.' || _s[_i] == 'e' || _s[_i] == 'E' || _s[_i] == '+' || _s[_i] == '-')) _i++;
                string token = _s.Substring(start, _i - start);
                long whole;
                if (token.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out whole))
                {
                    if (whole >= int.MinValue && whole <= int.MaxValue) return (int)whole;
                    return whole;
                }
                double d;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                throw new FormatException("Invalid number '" + token + "'.");
            }

            public void SkipWhitespace()
            {
                while (!End)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { _i++; continue; }
                    if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/')
                    {
                        while (!End && _s[_i] != '\n') _i++;
                        continue;
                    }
                    if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '*')
                    {
                        int close = _s.IndexOf("*/", _i + 2, StringComparison.Ordinal);
                        _i = close < 0 ? _s.Length : close + 2;
                        continue;
                    }
                    break;
                }
            }

            private bool Peek(char c)
            {
                return !End && _s[_i] == c;
            }

            private void Require(char c)
            {
                if (!Peek(c)) throw new FormatException("Expected '" + c + "' at position " + _i + ".");
                _i++;
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw new FormatException("Invalid literal at position " + _i + ".");
                _i += word.Length;
            }
        }
    }
}
