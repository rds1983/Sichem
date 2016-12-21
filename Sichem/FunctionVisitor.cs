using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ClangSharp;
using SealangSharp;

namespace Sichem
{
	internal class FunctionVisitor : BaseVisitor
	{
		private readonly ConversionParameters _parameters;

		public ConversionParameters Parameters
		{
			get { return _parameters; }
		}

		private CXCursor _functionStatement;
		private CXType _returnType;
		private string _functionName;

		public FunctionVisitor(ConversionParameters parameters, CXTranslationUnit translationUnit, TextWriter writer)
			: base(translationUnit, writer)
		{
			if (parameters == null)
			{
				throw new ArgumentNullException("parameters");
			}

			_parameters = parameters;
		}

		private CXChildVisitResult VisitEnums(CXCursor cursor, CXCursor parent, IntPtr data)
		{
			if (cursor.IsInSystemHeader())
			{
				return CXChildVisitResult.CXChildVisit_Continue;
			}

			var curKind = clang.getCursorKind(cursor);

			if (curKind == CXCursorKind.CXCursor_EnumDecl)
			{
				var i = 0;

				var count = cursor.GetChildrenCount();
				cursor.VisitWithAction(c =>
				{
					var name = clang.getCursorSpelling(c).ToString();

					var child = ProcessPossibleChildByIndex(c, 0);
					var value = child != null ? int.Parse(child.Expression) : i;

					var expr = "private const int " + name + " = " + value + ";";
					IndentedWriteLine(expr);

					i = value + 1;

					return CXChildVisitResult.CXChildVisit_Continue;
				});
			}

			return CXChildVisitResult.CXChildVisit_Continue;
		}

		private CXChildVisitResult VisitGlobalVariables(CXCursor cursor, CXCursor parent, IntPtr data)
		{
			if (cursor.IsInSystemHeader())
			{
				return CXChildVisitResult.CXChildVisit_Continue;
			}

			var curKind = clang.getCursorKind(cursor);
			var spelling = clang.getCursorSpelling(cursor).ToString();

			// look only at function decls
			if (curKind == CXCursorKind.CXCursor_VarDecl)
			{
				if (Parameters.SkipGlobalVariables.Contains(spelling))
				{
					return CXChildVisitResult.CXChildVisit_Continue;
				}

				var res = Process(cursor);

				res.Expression = "static " + res.Expression + ";";

				if (!string.IsNullOrEmpty(res.Expression))
				{
					IndentedWriteLine(res.Expression);
				}

				Logger.Info("Processing global variable {0}", spelling);
			}

			return CXChildVisitResult.CXChildVisit_Continue;
		}

		private CXChildVisitResult VisitFunctions(CXCursor cursor, CXCursor parent, IntPtr data)
		{
			if (cursor.IsInSystemHeader())
			{
				return CXChildVisitResult.CXChildVisit_Continue;
			}

			var curKind = clang.getCursorKind(cursor);

			// look only at function decls
			if (curKind == CXCursorKind.CXCursor_FunctionDecl)
			{
				// Skip empty declarations
				var body = cursor.FindChild(CXCursorKind.CXCursor_CompoundStmt);
				if (!body.HasValue)
				{
					return CXChildVisitResult.CXChildVisit_Continue;
				}

				_functionStatement = body.Value;

				_functionName = clang.getCursorSpelling(cursor).ToString();

				if (Parameters.SkipFunctions.Contains(_functionName))
				{
					return CXChildVisitResult.CXChildVisit_Continue;
				}

				Logger.Info("Processing function {0}", _functionName);

				ProcessFunction(cursor);
			}

			return CXChildVisitResult.CXChildVisit_Recurse;
		}

		private CursorProcessResult ProcessChildByIndex(CXCursor cursor, int index)
		{
			return Process(cursor.EnsureChildByIndex(index));
		}

		private CursorProcessResult ProcessPossibleChildByIndex(CXCursor cursor, int index)
		{
			var childCursor = cursor.GetChildByIndex(index);
			if (childCursor == null)
			{
				return null;
			}

			return Process(childCursor.Value);
		}

		internal void AppendGZ(CursorProcessResult crp)
		{
			var info = crp.Info;

			if (info.Kind == CXCursorKind.CXCursor_BinaryOperator)
			{
				var type = sealang.cursor_getBinaryOpcode(info.Cursor);
				if (type != BinaryOperatorKind.Or && type != BinaryOperatorKind.And)
				{
					return;
				}
			}

			if (info.Kind == CXCursorKind.CXCursor_ParenExpr)
			{
				return;
			}

			if (info.Kind == CXCursorKind.CXCursor_UnaryOperator)
			{
				var child = ProcessChildByIndex(info.Cursor, 0);
				if (child.Info.IsPointer)
				{
					return;
				}

				if (child.Info.Kind == CXCursorKind.CXCursor_ParenExpr)
				{
					var child2 = ProcessChildByIndex(child.Info.Cursor, 0);
					if (child2.Info.Kind == CXCursorKind.CXCursor_BinaryOperator &&
					    sealang.cursor_getBinaryOpcode(child2.Info.Cursor).IsBinaryOperator())
					{
					}
					else
					{
						return;
					}
				}

				var type = sealang.cursor_getUnaryOpcode(info.Cursor);
				if (type == UnaryOperatorKind.LNot)
				{
					var sub = ProcessChildByIndex(crp.Info.Cursor, 0);
					crp.Expression = sub.Expression + "== 0";

					return;
				}
			}

			if (info.Type.kind.IsPrimitiveNumericType())
			{
				crp.Expression = "(" + crp.Expression + ") != 0";
			}

			if (info.Type.IsPointer())
			{
				if (info.Type.IsRecord() || !info.Type.GetPointeeType().kind.IsPrimitiveNumericType())
				{
					crp.Expression = "(" + crp.Expression + ") != null";
				}
				else
				{
					crp.Expression = "!" + crp.Expression + ".IsNull";
				}
			}
		}

		private string ReplaceNullWithPointerByte(string expr)
		{
			if (expr == "null" || expr == "(null)")
			{
				return "Pointer<byte>.Null";
			}

			return expr;
		}


		private string ReplaceNullWithPointerByte2(string expr, string type)
		{
			if (expr == "null" || expr == "(null)" || expr == "0" || expr == "(0)")
			{
				return type + ".Null";
			}

			return expr;
		}

		private string ReplaceCommas(CursorProcessResult info)
		{
			var executionExpr = info.GetExpression();
			if (info != null && info.Info.Kind == CXCursorKind.CXCursor_BinaryOperator)
			{
				var type = sealang.cursor_getBinaryOpcode(info.Info.Cursor);
				if (type == BinaryOperatorKind.Comma)
				{
					var a = ReplaceCommas(ProcessChildByIndex(info.Info.Cursor, 0));
					var b = ReplaceCommas(ProcessChildByIndex(info.Info.Cursor, 1));

					executionExpr = a + ";" + b;
				}
			}

			return executionExpr;
		}

		private string InternalProcess(CursorInfo info)
		{
			switch (info.Kind)
			{
				case CXCursorKind.CXCursor_EnumConstantDecl:
				{
					var expr = ProcessPossibleChildByIndex(info.Cursor, 0);

					return info.Spelling + " = " + expr.Expression;
				}

				case CXCursorKind.CXCursor_UnaryExpr:
				{
					var expr = ProcessPossibleChildByIndex(info.Cursor, 0);

					if (expr != null)
					{
						if (info.Type.kind == CXTypeKind.CXType_ULongLong)
						{
							return expr.Expression + ".Size";
						}

						return expr.Expression;
					}

					var tokens = info.Cursor.Tokenize(_translationUnit);
					return string.Join(" ", tokens);
				}
				case CXCursorKind.CXCursor_DeclRefExpr:
					return info.Spelling.FixSpecialWords();
				case CXCursorKind.CXCursor_CompoundAssignOperator:
				case CXCursorKind.CXCursor_BinaryOperator:
				{
					var a = ProcessChildByIndex(info.Cursor, 0);
					var b = ProcessChildByIndex(info.Cursor, 1);
					var type = sealang.cursor_getBinaryOpcode(info.Cursor);

					if (type.IsLogicalBinaryOperator())
					{
						AppendGZ(a);
						AppendGZ(b);
					}

/*					if (type.IsLogicalBooleanOperator())
					{
						a.Expression = "(" + a.Expression + ")";
						b.Expression = "(" + b.Expression + ")";
					}*/

					if (type == BinaryOperatorKind.Assign)
					{
						if (a.Info.IsPointer && !a.Info.IsRecord)
						{
							b.Expression = ReplaceNullWithPointerByte2(b.Expression, a.Info.CsType);
						}

						if (a.Expression.Contains(".GetAndMove()"))
						{
							a.Expression = a.Expression.Replace(".GetAndMove()", ".SetAndMove(");
							a.Expression += "(" + b.Info.CsType + ")(" + b.Expression + "))";
							a.Expression = a.Expression.EnsureStatementFinished();
							return a.Expression;
						}
					}

					if (type.IsAssign() &&
						type != BinaryOperatorKind.ShlAssign &&
						type != BinaryOperatorKind.ShrAssign)
					{
						// Explicity cast right to left
						if (!info.Type.IsPointer())
						{
							if (b.Info.Kind == CXCursorKind.CXCursor_ParenExpr && b.Info.Cursor.GetChildrenCount() > 0)
							{
								var bb = ProcessChildByIndex(b.Info.Cursor, 0);
								if (bb.Info.Kind == CXCursorKind.CXCursor_BinaryOperator &&
								    sealang.cursor_getBinaryOpcode(bb.Info.Cursor).IsLogicalBooleanOperator())
								{
									b = bb;
								}
							}

							if (b.Info.Kind == CXCursorKind.CXCursor_BinaryOperator &&
							    sealang.cursor_getBinaryOpcode(b.Info.Cursor).IsLogicalBooleanOperator())
							{
								b.Expression = "(" + b.Expression + "?1:0)";
							}
							else
							{
								b.Expression = "(" + b.Expression + ")";
							}

							b.Expression = "(" + info.CsType + ")" + b.Expression;
						}
					}

					if (a.Info.IsPointer)
					{
						if (!a.Info.IsRecord)
						{
							switch (type)
							{
								case BinaryOperatorKind.EQ:
									return a.Expression + ".Equals(" + b.Expression + ")";
								case BinaryOperatorKind.AddAssign:
									return a.Expression + ".PlusAssign(" + b.Expression + ")";
								case BinaryOperatorKind.SubAssign:
									return a.Expression + ".MinusAssign(" + b.Expression + ")";
								case BinaryOperatorKind.Add:
									return a.Expression + ".Plus(" + b.Expression + ")";
								case BinaryOperatorKind.Sub:
									return a.Expression + ".Minus(" + b.Expression + ")";
								case BinaryOperatorKind.LT:
									return a.Expression + ".Lesser(" + b.Expression + ")";
								case BinaryOperatorKind.LE:
									return a.Expression + ".LesserEqual(" + b.Expression + ")";
								case BinaryOperatorKind.GT:
									return a.Expression + ".Greater(" + b.Expression + ")";
								case BinaryOperatorKind.GE:
									return a.Expression + ".GreaterEqual(" + b.Expression + ")";
							}

							if (b.Expression.Contains("null"))
							{
								switch (type)
								{
									case BinaryOperatorKind.EQ:
										return a.Expression + ".IsNull";
									case BinaryOperatorKind.NE:
										return "!" + a.Expression + ".IsNull";
								}
							}
						}
						else
						{
							switch (type)
							{
								case BinaryOperatorKind.Add:
									return a.Expression + "[" + b.Expression + "]";
							}
						}
					}

					var str = sealang.cursor_getOperatorString(info.Cursor);
					var result = a.Expression + " " + str + " " + b.Expression;

					return result;
				}
				case CXCursorKind.CXCursor_UnaryOperator:
				{
					var a = ProcessChildByIndex(info.Cursor, 0);

					var type = sealang.cursor_getUnaryOpcode(info.Cursor);

					if (type == UnaryOperatorKind.Deref && a.Info.Kind == CXCursorKind.CXCursor_UnaryOperator)
					{
						// Handle "*ptr++" case
						var aa = ProcessChildByIndex(a.Info.Cursor, 0);
						return aa.Expression + ".GetAndMove()";
					}

					if (a.Info.IsPointer && !a.Info.IsRecord)
					{
						switch (type)
						{
							case UnaryOperatorKind.LNot:
								return a.Expression + ".IsNull";
							case UnaryOperatorKind.Deref:
								a.Expression = a.Expression + ".CurrentValue";
								break;
							case UnaryOperatorKind.PostInc:
							case UnaryOperatorKind.PreInc:
								return a.Expression + ".Move()";
						}
					}

/*					if (type == "*" || type == "&")
					{
						type = string.Empty;
					}*/

					var str = sealang.cursor_getOperatorString(info.Cursor).ToString();
					var left = type.IsUnaryOperatorPre();

					switch (type)
					{
						case UnaryOperatorKind.AddrOf:
							if (a.Info.Kind == CXCursorKind.CXCursor_ArraySubscriptExpr)
							{
								var b = ProcessChildByIndex(a.Info.Cursor, 0);
								var c = ProcessChildByIndex(a.Info.Cursor, 1);

								if (!a.Info.IsRecord)
								{
									a.Expression = b.Expression + ".Plus(" + c.Expression + ")";
								}
								else
								{
									a.Expression = b.Expression + "[" + c.Expression + "]";
								}
							}
							str = string.Empty;

							break;
						case UnaryOperatorKind.Deref:
							str = string.Empty;
							break;
					}

					if (!a.Info.IsPointer)
					{
						// AppendGZ(a);
					}

					if (left)
					{
						return str + a.Expression;
					}

					return a.Expression + str;
				}

				case CXCursorKind.CXCursor_CallExpr:
				{
					var size = info.Cursor.GetChildrenCount();

					var functionExpr = ProcessChildByIndex(info.Cursor, 0);
					var functionName = functionExpr.Expression;

					// Retrieve arguments
					var args = new List<string>();
					for (var i = 1; i < size; ++i)
					{
						var argExpr = ProcessChildByIndex(info.Cursor, i);

						if (!argExpr.Info.IsPointer)
						{
							argExpr.Expression = "(" + argExpr.Info.CsType + ")(" + argExpr.Expression + ")";
						}

						args.Add(argExpr.Expression);
					}

					functionName = functionName.Replace("(", string.Empty).Replace(")", string.Empty);

					var sb = new StringBuilder();
					sb.Append(functionName + "(");
					sb.Append(string.Join(", ", args));
					sb.Append(")");

					return sb.ToString();
				}
				case CXCursorKind.CXCursor_ReturnStmt:
				{
					var child = ProcessPossibleChildByIndex(info.Cursor, 0);

					var ret = child.GetExpression();

					if (_returnType.kind != CXTypeKind.CXType_Void)
					{
						if (!_returnType.IsPointer())
						{
							if (child != null &&
							    child.Info.Kind == CXCursorKind.CXCursor_BinaryOperator &&
							    sealang.cursor_getBinaryOpcode(child.Info.Cursor).IsLogicalBooleanOperator())
							{
								ret = "(" + ret + "?1:0)";
							}
							else
							{
								ret = "(" + ret + ")";
							}

							ret = "(" + _returnType.ToCSharpTypeString() + ")" + ret;
						}
						else
						{
							ret = ReplaceNullWithPointerByte2(ret, _returnType.ToCSharpTypeString());
						}
					}

					var exp = string.IsNullOrEmpty(ret) ? "return" : "return " + ret;

					return exp;
				}
				case CXCursorKind.CXCursor_IfStmt:
				{
					var conditionExpr = ProcessChildByIndex(info.Cursor, 0);
					AppendGZ(conditionExpr);

					var executionExpr = ProcessChildByIndex(info.Cursor, 1);
					var elseExpr = ProcessPossibleChildByIndex(info.Cursor, 2);

					if (executionExpr != null && !string.IsNullOrEmpty(executionExpr.Expression))
					{
						if (executionExpr.Info.Kind == CXCursorKind.CXCursor_BinaryOperator &&
						    sealang.cursor_getBinaryOpcode(executionExpr.Info.Cursor) == BinaryOperatorKind.Comma)
						{
							var a = ProcessChildByIndex(executionExpr.Info.Cursor, 0);
							var b = ProcessChildByIndex(executionExpr.Info.Cursor, 1);
							executionExpr.Expression = "{" + a.Expression + ";" + b.Expression + ";}";
						}
						else
						{
							executionExpr.Expression = executionExpr.Expression.EnsureStatementFinished();
						}
					}

					var expr = "if (" + conditionExpr.Expression + ") " + executionExpr.Expression;

					if (elseExpr != null)
					{
						expr += " else " + elseExpr.Expression;
					}

					return expr;
				}
				case CXCursorKind.CXCursor_ForStmt:
				{
					var size = info.Cursor.GetChildrenCount();

					CursorProcessResult execution = null, start = null, condition = null, it = null;
					switch (size)
					{
						case 1:
							execution = ProcessChildByIndex(info.Cursor, 0);
							break;
						case 2:
							start = ProcessChildByIndex(info.Cursor, 0);
							execution = ProcessChildByIndex(info.Cursor, 1);
							break;
						case 3:
							var first = ProcessChildByIndex(info.Cursor, 0);
							if (first.Info.Kind == CXCursorKind.CXCursor_BinaryOperator &&
							    sealang.cursor_getBinaryOpcode(first.Info.Cursor).IsBooleanOperator())
							{
								condition = first;
							}
							else
							{
								start = first;
							}

							it = ProcessChildByIndex(info.Cursor, 1);
							execution = ProcessChildByIndex(info.Cursor, 2);
							break;
						case 4:
							start = ProcessChildByIndex(info.Cursor, 0);
							condition = ProcessChildByIndex(info.Cursor, 1);
							it = ProcessChildByIndex(info.Cursor, 2);
							execution = ProcessChildByIndex(info.Cursor, 3);
							break;
					}

					var executionExpr = ReplaceCommas(execution);
					executionExpr = executionExpr.EnsureStatementFinished();

					return "for (" + start.GetExpression() + "; " + condition.GetExpression() + "; " + it.GetExpression() + ") {" +
						   executionExpr + "}";
				}

				case CXCursorKind.CXCursor_CaseStmt:
				{
					var expr = ProcessChildByIndex(info.Cursor, 0);
					var execution = ProcessChildByIndex(info.Cursor, 1);
					return "case " + expr.Expression + ":" + execution.Expression;
				}

				case CXCursorKind.CXCursor_DefaultStmt:
				{
					var execution = ProcessChildByIndex(info.Cursor, 0);
					if (string.IsNullOrEmpty(execution.Expression))
					{
						return string.Empty;
					}
					return "default: " + execution.Expression;
				}

				case CXCursorKind.CXCursor_SwitchStmt:
				{
					var expr = ProcessChildByIndex(info.Cursor, 0);
					var execution = ProcessChildByIndex(info.Cursor, 1);
					return "switch (" + expr.Expression + ")" + execution.Expression;
				}

				case CXCursorKind.CXCursor_DoStmt:
				{
					var execution = ProcessChildByIndex(info.Cursor, 0);
					var expr = ProcessChildByIndex(info.Cursor, 1);
					AppendGZ(expr);

					return "do { " + execution.Expression.EnsureStatementFinished() + " } while (" + expr.Expression + ")";
				}

				case CXCursorKind.CXCursor_WhileStmt:
				{
					var expr = ProcessChildByIndex(info.Cursor, 0);
					AppendGZ(expr);
					var execution = ProcessChildByIndex(info.Cursor, 1);

					return "while (" + expr.Expression + ") { " + execution.Expression.EnsureStatementFinished() + " }";
				}

				case CXCursorKind.CXCursor_LabelRef:
					return info.Spelling;
				case CXCursorKind.CXCursor_GotoStmt:
				{
					var label = ProcessChildByIndex(info.Cursor, 0);

					return "goto " + label.Expression;
				}

				case CXCursorKind.CXCursor_LabelStmt:
				{
					var sb = new StringBuilder();

					sb.Append(info.Spelling);
					sb.Append(":;\n");

					var size = info.Cursor.GetChildrenCount();
					for (var i = 0; i < size; ++i)
					{
						var child = ProcessChildByIndex(info.Cursor, i);
						sb.Append(child.Expression);
					}

					return sb.ToString();
				}

				case CXCursorKind.CXCursor_ConditionalOperator:
				{
					var condition = ProcessChildByIndex(info.Cursor, 0);
					var a = ProcessChildByIndex(info.Cursor, 1);
					var b = ProcessChildByIndex(info.Cursor, 2);

					if (condition.Info.IsPrimitiveNumericType)
					{
						var gz = true;

						if (condition.Info.Kind == CXCursorKind.CXCursor_ParenExpr)
						{
							gz = false;
						} else if (condition.Info.Kind == CXCursorKind.CXCursor_BinaryOperator)
						{
							var op = sealang.cursor_getBinaryOpcode(condition.Info.Cursor);

							if (op == BinaryOperatorKind.Or || op == BinaryOperatorKind.And)
							{
							}
							else
							{
								gz = false;
							}
						}

						if (gz)
						{
							condition.Expression = "(" + condition.Expression + ") > 0";
						}
					}

					a.Expression = ReplaceNullWithPointerByte(a.Expression);
					b.Expression = ReplaceNullWithPointerByte(b.Expression);

					return condition.Expression + "?" + a.Expression + ":" + b.Expression;
				}
				case CXCursorKind.CXCursor_MemberRefExpr:
				{
					var a = ProcessChildByIndex(info.Cursor, 0);

					var op = (a.Info.IsPointer && !a.Info.IsRecord) ? ".CurrentValue." : ".";

					return a.Expression + op + info.Spelling.FixSpecialWords();
				}
				case CXCursorKind.CXCursor_IntegerLiteral:
				case CXCursorKind.CXCursor_FloatingLiteral:
				{
					var tokens = info.Cursor.Tokenize(_translationUnit);
					if (tokens.Length == 0)
					{
						return sealang.cursor_getLiteralString(info.Cursor).ToString();
					}

					return tokens[0];
				}
				case CXCursorKind.CXCursor_CharacterLiteral:
					return "'" + sealang.cursor_getLiteralString(info.Cursor) + "'";
				case CXCursorKind.CXCursor_StringLiteral:
					return info.Spelling.StartsWith("L") ? info.Spelling.Substring(1) : info.Spelling;
				case CXCursorKind.CXCursor_VarDecl:
				{
					CursorProcessResult rvalue = null;
					var size = info.Cursor.GetChildrenCount();

					if (size > 0)
					{
						rvalue = ProcessPossibleChildByIndex(info.Cursor, size - 1);

						if (info.Type.IsArray())
						{
							if (rvalue.Info.Kind == CXCursorKind.CXCursor_TypeRef ||
							    rvalue.Info.Kind == CXCursorKind.CXCursor_IntegerLiteral)
							{
								//
								rvalue.Expression = "new " + info.CsType + "(" + info.Type.GetArraySize() + ")";
							}
							else if (rvalue.Info.Kind == CXCursorKind.CXCursor_BinaryOperator)
							{
								rvalue.Expression = "new " + info.CsType + "(" + rvalue.Expression + ")";
							}
						}
					}

					var expr = info.CsType + " " + info.Spelling.FixSpecialWords();
					if (rvalue != null && !string.IsNullOrEmpty(rvalue.Expression))
					{
						if (!info.IsPointer)
						{
							if (rvalue.Info.Kind == CXCursorKind.CXCursor_BinaryOperator)
							{
								var op = sealang.cursor_getBinaryOpcode(rvalue.Info.Cursor);
								if (op.IsLogicalBooleanOperator())
								{
									rvalue.Expression = rvalue.Expression + "?1:0";
								}
							}
							expr += " = (" + info.CsType + ")(" + rvalue.Expression + ")";
						}
						else
						{
							if (rvalue.Info.Kind == CXCursorKind.CXCursor_InitListExpr)
							{
								rvalue.Expression = "new " + info.CsType + "( new " + info.Type.GetPointeeType().ToCSharpTypeString() + "[] " + rvalue.Expression + ")";
							}

							expr += " = " + rvalue.Expression;
						}
					}
					else if (info.IsRecord && !info.IsPointer)
					{
						expr += " =  new " + info.CsType + "()";
					}

					return expr;
				}
				case CXCursorKind.CXCursor_DeclStmt:
				{
					var sb = new StringBuilder();
					var size = info.Cursor.GetChildrenCount();
					for (var i = 0; i < size; ++i)
					{
						var exp = ProcessChildByIndex(info.Cursor, i);
						exp.Expression = exp.Expression.EnsureStatementFinished();
						sb.Append(exp.Expression);
					}

					return sb.ToString();
				}
				case CXCursorKind.CXCursor_CompoundStmt:
				{
					var sb = new StringBuilder();
					sb.Append("{\n");

					var size = info.Cursor.GetChildrenCount();
					for (var i = 0; i < size; ++i)
					{
						var exp = ProcessChildByIndex(info.Cursor, i);
						exp.Expression = exp.Expression.EnsureStatementFinished();
						sb.Append(exp.Expression);
					}

					sb.Append("}\n");

					var fullExp = sb.ToString();

					return fullExp;
				}

				case CXCursorKind.CXCursor_ArraySubscriptExpr:
				{
					var var = ProcessChildByIndex(info.Cursor, 0);
					var expr = ProcessChildByIndex(info.Cursor, 1);

					return var.Expression + "[" + expr.Expression + "]";
				}

				case CXCursorKind.CXCursor_InitListExpr:
				{
					var sb = new StringBuilder();

					sb.Append("{ ");
					var size = info.Cursor.GetChildrenCount();
					for (var i = 0; i < size; ++i)
					{
						var exp = ProcessChildByIndex(info.Cursor, i);

						if (!exp.Info.IsPointer)
						{
							exp.Expression = "(" + exp.Info.CsType + ")" + exp.Expression;
						}

						sb.Append(exp.Expression);

						if (i < size - 1)
						{
							sb.Append(", ");
						}
					}

					sb.Append(" }");
					return sb.ToString();
				}

				case CXCursorKind.CXCursor_ParenExpr:
				{
					var expr = ProcessPossibleChildByIndex(info.Cursor, 0);
					var e = expr.GetExpression();

					if (info.IsPointer || expr.Info.Kind == CXCursorKind.CXCursor_BinaryOperator)
					{
						e = "(" + e + ")";
					}
					else
					{
						e = "(" + info.CsType + ")(" + expr.GetExpression() + ")";
					}

					return e;
				}

				case CXCursorKind.CXCursor_BreakStmt:
					return "break";

				case CXCursorKind.CXCursor_CStyleCastExpr:
				{
					var size = info.Cursor.GetChildrenCount();
					var child = ProcessChildByIndex(info.Cursor, size - 1);

					var expr = child.Expression;

					if (info.IsPointer && child.Info.IsPointer)
					{
						var ap = info.Type.GetPointeeType();
						var bp = child.Info.Type.GetPointeeType();

						if (ap.kind.IsPrimitiveNumericType() && ap.kind != bp.kind)
						{
							expr += ".Cast<" + ap.ToCSharpTypeString() + ">()";
							return expr;
						}
					}

					if (!info.IsPointer || !child.Info.IsPrimitiveNumericType) return expr;

					if (info.IsRecord)
					{
						expr = "new " + info.CsType + "(" + expr + ")";
					}
					else if (expr == "0")
					{
						expr = "null";
					} 

					return expr;
				}

/*				case CXCursorKind.CXCursor_UnexposedExpr:
				{
					var size = info.Cursor.GetChildrenCount();

					if (size == 0)
					{
						return string.Empty;
					}

					var expr = ProcessPossibleChildByIndex(info.Cursor, size - 1);
					if (info.IsPointer)
					{
						return expr.GetExpression();
					}

					return "(" + info.CsType + ")(" + expr.GetExpression() + ")";
				}*/

				default:
				{
					// Return last child
					var size = info.Cursor.GetChildrenCount();

					if (size == 0)
					{
						return string.Empty;
					}

					var expr = ProcessPossibleChildByIndex(info.Cursor, size - 1);

					return expr.GetExpression();
				}
			}
		}

		private CursorProcessResult Process(CXCursor cursor)
		{
			var info = new CursorInfo(cursor);

			var expr = InternalProcess(info);

			return new CursorProcessResult(info)
			{
				Expression = expr
			};
		}

		private CXChildVisitResult VisitFunctionBody(CXCursor cursor, CXCursor parent, IntPtr data)
		{
			var res = Process(cursor);

			if (!string.IsNullOrEmpty(res.Expression))
			{
				IndentedWriteLine(res.Expression.EnsureStatementFinished());
			}

			return CXChildVisitResult.CXChildVisit_Continue;
		}

		private void ProcessFunction(CXCursor cursor)
		{
			WriteFunctionStart(cursor);

			_indentLevel++;

			clang.visitChildren(_functionStatement, VisitFunctionBody, new CXClientData(IntPtr.Zero));

			// DumpCursor(cursor);

			_indentLevel--;

			IndentedWriteLine("}");
			_writer.WriteLine();
		}

		private void WriteFunctionStart(CXCursor cursor)
		{
			var functionType = clang.getCursorType(cursor);
			var functionName = clang.getCursorSpelling(cursor).ToString();
			_returnType = clang.getCursorResultType(cursor).Desugar();

			IndentedWrite("public static " + _returnType.ToCSharpTypeString());

			_writer.Write(" " + functionName + "(");

			var numArgTypes = clang.getNumArgTypes(functionType);
			for (uint i = 0; i < numArgTypes; ++i)
			{
				ArgumentHelper(functionType, clang.Cursor_getArgument(cursor, i), i);
			}

			_writer.WriteLine(")");
			IndentedWriteLine("{");
		}

		private void ArgumentHelper(CXType functionType, CXCursor paramCursor, uint index)
		{
			var numArgTypes = clang.getNumArgTypes(functionType);
			var type = clang.getArgType(functionType, index);

			var spelling = clang.getCursorSpelling(paramCursor).ToString();

			var name = spelling.FixSpecialWords();
			var typeName = type.ToCSharpTypeString();

			_writer.Write(typeName);
			_writer.Write(" ");

			_writer.Write(name);

			if (index != numArgTypes - 1)
			{
				_writer.Write(", ");
			}
		}

		public override void Run()
		{
			clang.visitChildren(clang.getTranslationUnitCursor(_translationUnit), VisitEnums, new CXClientData(IntPtr.Zero));
			clang.visitChildren(clang.getTranslationUnitCursor(_translationUnit), VisitGlobalVariables, new CXClientData(IntPtr.Zero));
			clang.visitChildren(clang.getTranslationUnitCursor(_translationUnit), VisitFunctions, new CXClientData(IntPtr.Zero));
		}
	}
}