using System.Globalization;

namespace Emd.Core.Simulation;

public sealed record ExpressionEvaluationResult(
    bool Success,
    bool Value,
    string? Error,
    string? UnknownSignal);

/// <summary>
/// Small expression evaluator for scenario assertions: numbers, signal names,
/// + - * /, comparisons, parentheses, and <c>abs()</c>.
/// </summary>
public static class ExpressionEvaluator
{
    public static ExpressionEvaluationResult Evaluate(
        string expression,
        IReadOnlyDictionary<string, double> variables)
    {
        try
        {
            var parser = new Parser(expression, variables);
            var value = parser.ParseExpression();
            parser.ExpectEnd();
            return new ExpressionEvaluationResult(true, value != 0, null, null);
        }
        catch (UnknownSignalException exception)
        {
            return new ExpressionEvaluationResult(false, false, exception.Message, exception.Signal);
        }
        catch (FormatException exception)
        {
            return new ExpressionEvaluationResult(false, false, exception.Message, null);
        }
    }

    private sealed class Parser
    {
        private readonly string _text;
        private readonly IReadOnlyDictionary<string, double> _variables;
        private int _position;

        public Parser(string text, IReadOnlyDictionary<string, double> variables)
        {
            _text = text;
            _variables = variables;
        }

        public double ParseExpression()
        {
            var left = ParseAdditive();
            SkipWhitespace();

            if (Match("=="))
                return Compare(left, ParseAdditive(), (a, b) => a == b);
            if (Match("!="))
                return Compare(left, ParseAdditive(), (a, b) => a != b);
            if (Match("<="))
                return Compare(left, ParseAdditive(), (a, b) => a <= b);
            if (Match(">="))
                return Compare(left, ParseAdditive(), (a, b) => a >= b);
            if (Match("<"))
                return Compare(left, ParseAdditive(), (a, b) => a < b);
            if (Match(">"))
                return Compare(left, ParseAdditive(), (a, b) => a > b);

            return left;
        }

        public void ExpectEnd()
        {
            SkipWhitespace();
            if (_position != _text.Length)
                throw new FormatException($"Unexpected token '{_text[_position..]}'.");
        }

        private double ParseAdditive()
        {
            var value = ParseMultiplicative();
            while (true)
            {
                SkipWhitespace();
                if (Match("+"))
                    value += ParseMultiplicative();
                else if (Match("-"))
                    value -= ParseMultiplicative();
                else
                    return value;
            }
        }

        private double ParseMultiplicative()
        {
            var value = ParseUnary();
            while (true)
            {
                SkipWhitespace();
                if (Match("*"))
                    value *= ParseUnary();
                else if (Match("/"))
                    value /= ParseUnary();
                else
                    return value;
            }
        }

        private double ParseUnary()
        {
            SkipWhitespace();
            if (Match("-"))
                return -ParseUnary();
            if (Match("+"))
                return ParseUnary();
            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            SkipWhitespace();
            if (_position >= _text.Length)
                throw new FormatException("Unexpected end of expression.");

            if (Match("("))
            {
                var value = ParseExpression();
                if (!Match(")"))
                    throw new FormatException("Missing ')'.");
                return value;
            }

            var character = _text[_position];
            if (char.IsDigit(character) || character == '.')
                return ParseNumber();

            if (char.IsLetter(character) || character == '_')
                return ParseIdentifier();

            throw new FormatException($"Unexpected character '{character}'.");
        }

        private double ParseNumber()
        {
            var start = _position;
            while (_position < _text.Length &&
                   (char.IsDigit(_text[_position]) || _text[_position] == '.'))
            {
                _position++;
            }

            if (_position < _text.Length && (_text[_position] == 'e' || _text[_position] == 'E'))
            {
                _position++;
                if (_position < _text.Length && (_text[_position] == '+' || _text[_position] == '-'))
                    _position++;
                while (_position < _text.Length && char.IsDigit(_text[_position]))
                    _position++;
            }

            var text = _text[start.._position];
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw new FormatException($"Invalid number '{text}'.");
            return value;
        }

        private double ParseIdentifier()
        {
            var start = _position;
            while (_position < _text.Length &&
                   (char.IsLetterOrDigit(_text[_position]) ||
                    _text[_position] is '_' or '.'))
            {
                _position++;
            }

            var name = _text[start.._position];
            SkipWhitespace();
            if (Match("("))
            {
                if (!string.Equals(name, "abs", StringComparison.Ordinal))
                    throw new FormatException($"Unknown function '{name}'.");

                var argument = ParseExpression();
                if (!Match(")"))
                    throw new FormatException("Missing ')' after abs().");
                return Math.Abs(argument);
            }

            if (_variables.TryGetValue(name, out var value))
                return value;

            throw new UnknownSignalException(name);
        }

        private bool Match(string token)
        {
            SkipWhitespace();
            if (!_text.AsSpan(_position).StartsWith(token, StringComparison.Ordinal))
                return false;
            _position += token.Length;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
                _position++;
        }

        private static double Compare(double left, double right, Func<double, double, bool> comparison) =>
            comparison(left, right) ? 1 : 0;
    }

    private sealed class UnknownSignalException : Exception
    {
        public UnknownSignalException(string signal)
            : base($"Unknown signal '{signal}'.")
        {
            Signal = signal;
        }

        public string Signal { get; }
    }
}
