using System;
using System.Collections.Generic;

namespace Kaleidoscope.Ast
{
	public class Parser
	{
		// The internal lexer that delivers our tokens
		private Lexer _lexer;

		// The current token that is inspected by the parser.
		private Token _token;

		// Create a new parser by wrapping a lexer.
		public Parser(Lexer lexer)
		{
			this._lexer = lexer;
			EatToken();
		}

		// Parse a top-level element and return it.
		public TopLevelElement Parse()
		{
			// There are three possibilities for the current token type:
			// A function declaration, a function definition or the end of the file.
			switch (this._token.Type)
			{
				case TokenType.Keyword when (this._token.Keyword == Keyword.Dec): return ParseFunctionDeclaration();
				case TokenType.Keyword when (this._token.Keyword == Keyword.Def): return ParseFunctionDefinition();
				case TokenType.EndOfFile: return null;

				default: throw new FormatException($"Expected function declaration / definition or end of file, but got '{ this._token }'.");
			}
		}

		// Get the precedence of the given operator.
		private static int GetOperatorPrecedence(Operator op)
		{
			switch (op)
			{
				case Operator.Add: return 80;
				case Operator.Subtract: return 80;
				case Operator.Multiply: return 100;
				case Operator.Divide: return 100;
				case Operator.Equal: return 60;
				case Operator.LowerThan: return 60;
				case Operator.LowerThanEqual: return 60;
				case Operator.GreaterThan: return 60;
				case Operator.GreaterThanEqual: return 60;

				default: throw new InvalidOperationException("Invalid operator type.");
			}
		}

		// Consume the current token and load the next one.
		// If the current token is the end of the file, this will throw!
		private void EatToken()
		{
			// Skip comments.
			do
			{
				this._token = this._lexer.Lex();
			} while (this._token.Type == TokenType.Comment);
		}

		// Parse a generic expression (= a primary expression + (op + primary expression)*).
		private Expression ParseExpression()
		{
			// Parse the primary expression on the left side.
			// Then try to extend it with a binary operator RHS.
			var primaryExpr = ParsePrimaryExpression();
			return ExtendPrimaryExpression(primaryExpr, 0);
		}

		// Parse a primary expression (literal, bracket, identifier or conditional expression).
		private Expression ParsePrimaryExpression()
		{
			// Dispatch to the correct parser.
			switch (this._token.Type)
			{
				case TokenType.Number: return ParseLiteralExpression();
				case TokenType.Bracket when (this._token.Bracket == Bracket.RoundStart): return ParseBracketExpression();
				case TokenType.Identifier: return ParseIdentifierExpression();
				case TokenType.Keyword when (this._token.Keyword == Keyword.If): return ParseConditionalExpression();

				default: throw new FormatException($"Expected primary expression, but got '{ this._token }'.");
			}
		}

		// Parse a literal expression.
		private Expression ParseLiteralExpression()
		{
			// Parse the number.
			if (this._token.Type != TokenType.Number)
			{
				throw new FormatException($"Expected number, but got '{ this._token }'.");
			}

			var expr = new LiteralExpression(this._token.Number);
			EatToken();

			return expr;
		}

		// Parse a bracket expression.
		private Expression ParseBracketExpression()
		{
			// Parse the opening bracket.
			if ((this._token.Type != TokenType.Bracket) || (this._token.Bracket != Bracket.RoundStart))
			{
				throw new FormatException($"Expected '(' at start of bracket expression, but got '{ this._token }'.");
			}

			EatToken();

			// Parse the inner expression.
			var expr = ParseExpression();

			// Parse the closing bracket.
			if ((this._token.Type != TokenType.Bracket) || (this._token.Bracket != Bracket.RoundEnd))
			{
				throw new FormatException($"Expected ')' at end of bracket expression, but got '{ this._token }'.");
			}

			EatToken();

			return expr;
		}

		// Parse an identifier expression (parameter or function call).
		private Expression ParseIdentifierExpression()
		{
			// Parse the identifier.
			if (this._token.Type != TokenType.Identifier)
			{
				throw new FormatException($"Expected parameter identifier or function call, but got '{ this._token }'.");
			}

			var name = this._token.Identifier;
			EatToken();

			// If the next token is not an opening round bracket, we only have a parameter expression.
			if ((this._token.Type != TokenType.Bracket) || (this._token.Bracket != Bracket.RoundStart))
			{
				return new ParameterExpression(name);
			}

			EatToken();

			// Start a list of parameters.
			var parameters = new List<Expression>();

			// Check if this is the "no parameter" case as in "func()".
			// *Important*: This condition intentionally differs from `ParseFunctionPrototype()`!
			// The first parameter expression is allowed to start with '('.
			if ((this._token.Type != TokenType.Bracket) || (this._token.Bracket != Bracket.RoundEnd))
			{
				while (true)
				{
					// Parse the current parameter.
					parameters.Add(ParseExpression());

					// If we now have a closing bracket, we are done.
					if ((this._token.Type == TokenType.Bracket) && (this._token.Bracket == Bracket.RoundEnd))
					{
						break;
					}

					// Otherwise, this must be a parameter separator.
					if (this._token.Type != TokenType.ParameterSeparator)
					{
						throw new FormatException($"Expected ')' or ',' in function call, but got '{ this._token }'.");
					}

					EatToken();
				}
			}

			// Eat the bracket that has terminated the list.
			EatToken();

			return new CallExpression(name, parameters);
		}

		// Parse a conditional expression.
		private Expression ParseConditionalExpression()
		{
			// Parse the "if" keyword.
			if ((this._token.Type != TokenType.Keyword) || (this._token.Keyword != Keyword.If))
			{
				throw new FormatException($"Expected 'if' in conditional expression, but got '{ this._token }'.");
			}

			EatToken();

			// Parse the condition.
			var condition = ParseExpression();

			// Parse the "then" keyword.
			if ((this._token.Type != TokenType.Keyword) || (this._token.Keyword != Keyword.Then))
			{
				throw new FormatException($"Expected 'then' in conditional expression, but got '{ this._token }'.");
			}

			EatToken();

			// Parse the "then" expression.
			var thenExpression = ParseExpression();

			// Parse the "else" keyword.
			if ((this._token.Type != TokenType.Keyword) || (this._token.Keyword != Keyword.Else))
			{
				throw new FormatException($"Expected 'else' in conditional expression, but got '{ this._token }'.");
			}

			EatToken();

			// Parse the "else" expression.
			var elseExpression = ParseExpression();

			return new ConditionalExpression(condition, thenExpression, elseExpression);
		}

		// Extend a primary expression that is followed by one or more binary operators.
		// Cut at the first binary operator whose precedence is below "min_precedence".
		private Expression ExtendPrimaryExpression(Expression lhs, int min_precedence)
		{
			// If the lookahead token is not an operator, we are done and return the accumulated expression.
			// In the first run, that's simply the primary expression parameter.
			while (this._token.Type == TokenType.Operator)
			{
				// Get the operator and its precedence.
				var op = this._token.Operator;
				var precedence = Parser.GetOperatorPrecedence(op);

				// If the new precedence is below our minimum precedence,
				// we perform a cut and return the accumulated expression.
				if (precedence < min_precedence)
				{
					break;
				}

				// The operator is strong enough.
				// Eat it and parse the new RHS.
				EatToken();
				var rhs = ParsePrimaryExpression();

				// Now perform a look-ahead and check the token after the RHS.
				// If it is also an operator that is *even* stronger, extend our new RHS to the right recursively.
				if ((this._token.Type == TokenType.Operator) && (Parser.GetOperatorPrecedence(this._token.Operator) > precedence))
				{
					rhs = ExtendPrimaryExpression(rhs, precedence + 1);
				}

				// Merge the LHS and RHS into a binary operator expression and make it the new LHS.
				lhs = new BinaryOperatorExpression(lhs, op, rhs);
			}

			return lhs;
		}

		// Parse a function prototype.
		private FunctionPrototype ParseFunctionPrototype()
		{
			// Parse the function name.
			if (this._token.Type != TokenType.Identifier)
			{
				throw new FormatException($"Expected identifier in function prototype, but got '{ this._token }'.");
			}

			var name = this._token.Identifier;
			EatToken();

			// Parse the opening bracket.
			if ((this._token.Type != TokenType.Bracket) || (this._token.Bracket != Bracket.RoundStart))
			{
				throw new FormatException($"Expected '(' in function prototype, but got '{ this._token }'.");
			}

			EatToken();

			// Start a list of parameter names.
			var parameterNames = new List<string>();

			// Check if this is the "no parameter" case as in "def func()".
			if (this._token.Type != TokenType.Bracket)
			{
				while (true)
				{
					// Parse the current parameter name.
					if (this._token.Type != TokenType.Identifier)
					{
						throw new FormatException($"Expected identifier in function parameter list, but got '{ this._token }'.");
					}

					parameterNames.Add(this._token.Identifier);
					EatToken();

					// If we now have a bracket, we are done.
					if (this._token.Type == TokenType.Bracket)
					{
						break;
					}

					// Otherwise, this must be a parameter separator.
					if (this._token.Type != TokenType.ParameterSeparator)
					{
						throw new FormatException($"Expected ')' or ',' in function parameter list, but got '{ this._token }'.");
					}

					EatToken();
				}
			}

			// Validate and eat the bracket that has terminated the list.
			if (this._token.Bracket != Bracket.RoundEnd)
			{
				throw new FormatException($"Expected ')' at end of function parameter list, but got '{ this._token }'.");
			}

			EatToken();

			return new FunctionPrototype(name, parameterNames);
		}

		// Parse a function declaration.
		private TopLevelElement ParseFunctionDeclaration()
		{
			// Parse the "dec" keyword.
			if ((this._token.Type != TokenType.Keyword) || (this._token.Keyword != Keyword.Dec))
			{
				throw new FormatException($"Expected 'dec' at start of function declaration, but got '{ this._token }'.");
			}

			EatToken();

			// Parse the prototype.
			var prototype = ParseFunctionPrototype();

			// Build the final declaration.
			return new FunctionDeclaration(prototype);
		}

		// Parse a function definition.
		private TopLevelElement ParseFunctionDefinition()
		{
			// Parse the "def" keyword.
			if ((this._token.Type != TokenType.Keyword) || (this._token.Keyword != Keyword.Def))
			{
				throw new FormatException($"Expected 'def' at start of function definition, but got '{ this._token }'.");
			}

			EatToken();

			// Parse the prototype.
			var prototype = ParseFunctionPrototype();

			//Parse the body expression.
			var body = ParseExpression();

			// Build the final definition.
			return new FunctionDefinition(prototype, body);
		}
	}
}
