using System.Globalization;
using System.Text;

namespace AskLucy.Application.Workflows.Expressions;

/// <summary>
/// A hand-written recursive-descent parser for the closed expression grammar
/// (contracts/workflow-expression-engine.md, research.md Decision 6). No <c>eval</c>, no
/// reflection, no dynamic code generation — the grammar this parser accepts is the *entire*
/// surface a workflow author can express; there is no escape hatch to anything else.
/// </summary>
internal static class WorkflowExpressionParser
{
    private enum TokenType
    {
        Reference, String, Number, True, False, Null,
        And, Or, Not, Identifier,
        LParen, RParen, Comma,
        Eq, Neq, Lt, Lte, Gt, Gte,
        EndOfInput,
    }

    private readonly record struct Token(TokenType Type, string Text);

    private static readonly HashSet<string> WhitelistedFunctions = new(StringComparer.Ordinal) { "concat", "length", "contains", "isEmpty" };

    public static WorkflowExpressionNode Parse(string expression)
    {
        var tokens = Tokenize(expression);
        var position = 0;

        var node = ParseLogicalOr(tokens, ref position);

        if (tokens[position].Type != TokenType.EndOfInput)
        {
            throw new WorkflowExpressionParseException($"Unexpected token '{tokens[position].Text}' after a complete expression.");
        }

        return node;
    }

    private static WorkflowExpressionNode ParseLogicalOr(IReadOnlyList<Token> tokens, ref int position)
    {
        var left = ParseLogicalAnd(tokens, ref position);
        while (tokens[position].Type == TokenType.Or)
        {
            position++;
            var right = ParseLogicalAnd(tokens, ref position);
            left = new LogicalExpressionNode("OR", left, right);
        }

        return left;
    }

    private static WorkflowExpressionNode ParseLogicalAnd(IReadOnlyList<Token> tokens, ref int position)
    {
        var left = ParseLogicalNot(tokens, ref position);
        while (tokens[position].Type == TokenType.And)
        {
            position++;
            var right = ParseLogicalNot(tokens, ref position);
            left = new LogicalExpressionNode("AND", left, right);
        }

        return left;
    }

    private static WorkflowExpressionNode ParseLogicalNot(IReadOnlyList<Token> tokens, ref int position)
    {
        if (tokens[position].Type == TokenType.Not)
        {
            position++;
            var operand = ParseLogicalNot(tokens, ref position);
            return new LogicalExpressionNode("NOT", operand, null);
        }

        return ParseComparison(tokens, ref position);
    }

    private static WorkflowExpressionNode ParseComparison(IReadOnlyList<Token> tokens, ref int position)
    {
        var left = ParseTerm(tokens, ref position);

        var op = tokens[position].Type switch
        {
            TokenType.Eq => "==",
            TokenType.Neq => "!=",
            TokenType.Lt => "<",
            TokenType.Lte => "<=",
            TokenType.Gt => ">",
            TokenType.Gte => ">=",
            _ => null,
        };

        if (op is null)
        {
            return left;
        }

        position++;
        var right = ParseTerm(tokens, ref position);
        return new ComparisonExpressionNode(left, op, right);
    }

    private static WorkflowExpressionNode ParseTerm(IReadOnlyList<Token> tokens, ref int position)
    {
        var token = tokens[position];

        switch (token.Type)
        {
            case TokenType.LParen:
                position++;
                var inner = ParseLogicalOr(tokens, ref position);
                Expect(tokens, ref position, TokenType.RParen);
                return inner;

            case TokenType.Reference:
                position++;
                return new ReferenceExpressionNode(token.Text);

            case TokenType.String:
                position++;
                return new LiteralExpressionNode(WorkflowExpressionValue.OfString(token.Text));

            case TokenType.Number:
                position++;
                return new LiteralExpressionNode(WorkflowExpressionValue.OfNumber(double.Parse(token.Text, CultureInfo.InvariantCulture)));

            case TokenType.True:
                position++;
                return new LiteralExpressionNode(WorkflowExpressionValue.OfBoolean(true));

            case TokenType.False:
                position++;
                return new LiteralExpressionNode(WorkflowExpressionValue.OfBoolean(false));

            case TokenType.Null:
                position++;
                return new LiteralExpressionNode(WorkflowExpressionValue.Null);

            case TokenType.Identifier when WhitelistedFunctions.Contains(token.Text):
                return ParseFunctionCall(tokens, ref position);

            case TokenType.Identifier:
                throw new WorkflowExpressionParseException($"'{token.Text}' is not a recognized function. Only concat, length, contains, and isEmpty are allowed.");

            default:
                throw new WorkflowExpressionParseException($"Unexpected token '{token.Text}' — expected a value, reference, function call, or '('.");
        }
    }

    private static WorkflowExpressionNode ParseFunctionCall(IReadOnlyList<Token> tokens, ref int position)
    {
        var functionName = tokens[position].Text;
        position++;
        Expect(tokens, ref position, TokenType.LParen);

        var arguments = new List<WorkflowExpressionNode>();
        if (tokens[position].Type != TokenType.RParen)
        {
            arguments.Add(ParseLogicalOr(tokens, ref position));
            while (tokens[position].Type == TokenType.Comma)
            {
                position++;
                arguments.Add(ParseLogicalOr(tokens, ref position));
            }
        }

        Expect(tokens, ref position, TokenType.RParen);
        return new FunctionCallExpressionNode(functionName, arguments);
    }

    private static void Expect(IReadOnlyList<Token> tokens, ref int position, TokenType expected)
    {
        if (tokens[position].Type != expected)
        {
            throw new WorkflowExpressionParseException($"Expected '{expected}' but found '{tokens[position].Text}'.");
        }

        position++;
    }

    private static List<Token> Tokenize(string expression)
    {
        var tokens = new List<Token>();
        var i = 0;

        while (i < expression.Length)
        {
            var c = expression[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '{' && i + 1 < expression.Length && expression[i + 1] == '{')
            {
                var end = expression.IndexOf("}}", i + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    throw new WorkflowExpressionParseException("Unterminated '{{' reference — missing closing '}}'.");
                }

                var path = expression[(i + 2)..end].Trim();
                tokens.Add(new Token(TokenType.Reference, path));
                i = end + 2;
                continue;
            }

            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < expression.Length && expression[i] != '"')
                {
                    if (expression[i] == '\\' && i + 1 < expression.Length)
                    {
                        i++;
                    }

                    sb.Append(expression[i]);
                    i++;
                }

                if (i >= expression.Length)
                {
                    throw new WorkflowExpressionParseException("Unterminated string literal — missing closing '\"'.");
                }

                i++; // closing quote
                tokens.Add(new Token(TokenType.String, sb.ToString()));
                continue;
            }

            if (char.IsDigit(c) || (c == '-' && i + 1 < expression.Length && char.IsDigit(expression[i + 1])))
            {
                var start = i;
                i++;
                while (i < expression.Length && (char.IsDigit(expression[i]) || expression[i] == '.'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenType.Number, expression[start..i]));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < expression.Length && (char.IsLetterOrDigit(expression[i]) || expression[i] == '_'))
                {
                    i++;
                }

                var word = expression[start..i];
                tokens.Add(word switch
                {
                    "AND" => new Token(TokenType.And, word),
                    "OR" => new Token(TokenType.Or, word),
                    "NOT" => new Token(TokenType.Not, word),
                    "true" => new Token(TokenType.True, word),
                    "false" => new Token(TokenType.False, word),
                    "null" => new Token(TokenType.Null, word),
                    _ => new Token(TokenType.Identifier, word),
                });
                continue;
            }

            switch (c)
            {
                case '(':
                    tokens.Add(new Token(TokenType.LParen, "("));
                    i++;
                    continue;
                case ')':
                    tokens.Add(new Token(TokenType.RParen, ")"));
                    i++;
                    continue;
                case ',':
                    tokens.Add(new Token(TokenType.Comma, ","));
                    i++;
                    continue;
                case '=' when i + 1 < expression.Length && expression[i + 1] == '=':
                    tokens.Add(new Token(TokenType.Eq, "=="));
                    i += 2;
                    continue;
                case '!' when i + 1 < expression.Length && expression[i + 1] == '=':
                    tokens.Add(new Token(TokenType.Neq, "!="));
                    i += 2;
                    continue;
                case '<' when i + 1 < expression.Length && expression[i + 1] == '=':
                    tokens.Add(new Token(TokenType.Lte, "<="));
                    i += 2;
                    continue;
                case '>' when i + 1 < expression.Length && expression[i + 1] == '=':
                    tokens.Add(new Token(TokenType.Gte, ">="));
                    i += 2;
                    continue;
                case '<':
                    tokens.Add(new Token(TokenType.Lt, "<"));
                    i++;
                    continue;
                case '>':
                    tokens.Add(new Token(TokenType.Gt, ">"));
                    i++;
                    continue;
                default:
                    throw new WorkflowExpressionParseException($"Unexpected character '{c}' at position {i}.");
            }
        }

        tokens.Add(new Token(TokenType.EndOfInput, string.Empty));
        return tokens;
    }
}
