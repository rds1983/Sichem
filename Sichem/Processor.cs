using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ClangSharp;
using SealangSharp;

namespace Sichem
{
	internal class Processor : BaseVisitor
	{
		private enum State
		{
			Structs,
			GlobalVariables,
			Enums,
			Functions
		}

		private readonly ConversionParameters _parameters;

		public ConversionParameters Parameters
		{
			get { return _parameters; }
		}


		private readonly HashSet<string> _globalVariables = new HashSet<string>();
		private CXCursor _functionStatement;
		private CXType _returnType;
		private string _functionName;
		private readonly HashSet<string> _visitedStructs = new HashSet<string>();
		private bool _isStruct;
		private State _state;

		public Processor(ConversionParameters parameters, CXTranslationUnit translationUnit, TextWriter writer)
			: base(translationUnit, writer)
		{
			if (parameters == null)
			{
				throw new ArgumentNullException("parameters");
			}

			_parameters = parameters;
		}

		private CXChildVisitResult VisitStructs(CXCursor cursor, CXCursor parent, IntPtr data)
		{
			if (cursor.IsInSystemHeader())
			{
				return CXChildVisitResult.CXChildVisit_Continue;
			}

			var curKind = clang.getCursorKind(cursor);
			switch (curKind)
			{
				case CXCursorKind.CXCursor_StructDecl:
					var structName = clang.getCursorSpelling(cursor).ToString();

					// struct names can be empty, and so we visit its sibling to find the name
					if (string.IsNullOrEmpty(structName))
					{
						var forwardDeclaringVisitor = new ForwardDeclarationVisitor(cursor);
						clang.visitChildren(clang.getCursorSemanticParent(cursor), forwardDeclaringVisitor.Visit,
							new CXClientData(IntPtr.Zero));
						structName = clang.getCursorSpelling(forwardDeclaringVisitor.ForwardDeclarationCursor).ToString();

						if (string.IsNullOrEmpty(structName))
						{
							structName = "_";
						}
					}

					if (!_visitedStructs.Contains(structName) && !Parameters.SkipStructs.Contains(structName))
					{
						Logger.Info("Processing struct {0}", structName);

						_isStruct = Parameters.Structs.Contains(structName);

						if (_isStruct)
						{
							IndentedWriteLine("[StructLayout(LayoutKind.Sequential)]");
						}

						IndentedWriteLine("public unsafe " + (_isStruct ? "struct" : "class") + " " + structName);
						IndentedWriteLine("{");

						_indentLevel++;
						clang.visitChildren(cursor, VisitStructs, new CXClientData(IntPtr.Zero));

						_indentLevel--;

						IndentedWriteLine("}");
						_writer.WriteLine();

						_visitedStructs.Add(structName);
					}

					return CXChildVisitResult.CXChildVisit_Continue;
				case CXCursorKind.CXCursor_FieldDecl:
					var fieldName = clang.getCursorSpelling(cursor).ToString().FixSpecialWords();
					var expr = Process(cursor);

					var result = "public ";

					if (_isStruct && expr.Info.IsArray && expr.Info.Type.GetPointeeType().kind != CXTypeKind.CXType_Record)
					{
						result += "fixed " + expr.Info.Type.GetPointeeType().ToCSharpTypeString() + " " + fieldName + "[" + expr.Expression + "]";
					}
					else
					{
						result += expr.Info.CsType + " " + fieldName;
					}

					if (!_isStruct)
					{
						if (expr.Info.IsPointer && !string.IsNullOrEmpty(expr.Expression))
						{
							result += " = new " + expr.Info.CsType + expr.Expression.Parentize();
						}
						else if (expr.Info.RecordType != RecordType.None)
						{
							result += " = new " + expr.Info.CsType + "()";
						}
					}

					result += ";";
					IndentedWriteLine(result);

					return CXChildVisitResult.CXChildVisit_Continue;
			}

			return CXChildVisitResult.CXChildVisit_Recurse;
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

				cursor.VisitWithAction(c =>
				{
					var name = clang.getCursorSpelling(c).ToString();

					var child = ProcessPossibleChildByIndex(c, 0);
					var value = child != null ? int.Parse(child.Expression) : i;

					var expr = "public const int " + name + " = " + value + ";";
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

				_globalVariables.Add(spelling);

				var res = Process(cursor);

				res.Expression = "public static " + res.Expression + ";";

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
				var type = sealang.cursor_getUnaryOpcode(info.Cursor);
				if (child.Info.IsPointer)
				{
					if (type == UnaryOperatorKind.LNot)
					{
						crp.Expression = child.Expression + "== null";
					}

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

				if (type == UnaryOperatorKind.LNot)
				{
					var sub = ProcessChildByIndex(crp.Info.Cursor, 0);
					crp.Expression = sub.Expression + "== 0";

					return;
				}
			}

			if (info.Type.kind.IsPrimitiveNumericType())
			{
				crp.Expression = crp.Expression.Parentize() + " != 0";
			}

			if (info.Type.IsPointer())
			{
				crp.Expression = crp.Expression.Parentize()+ " != null";
			}
		}

/*		private string ReplaceNullWithPointerByte(string expr)
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
		}*/

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
					return string.Join(string.Empty, tokens);
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

					if (type.IsLogicalBooleanOperator())
					{
						a.Expression = a.Expression.Parentize();
						b.Expression = b.Expression.Parentize();
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
								b.Expression = b.Expression.Parentize();
							}

							b.Expression = b.Expression.ApplyCast(info.CsType);
						}
					}

					if (a.Info.IsPointer)
					{
						if (a.Info.RecordType == RecordType.Class)
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
					var str = sealang.cursor_getOperatorString(info.Cursor).ToString();

					if (info.RecordType == RecordType.Class && (type == UnaryOperatorKind.AddrOf || type == UnaryOperatorKind.Deref))
					{
						str = string.Empty;
					}

					var left = type.IsUnaryOperatorPre();
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
							argExpr.Expression = argExpr.Expression.ApplyCast(argExpr.Info.CsType);
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

							return "return " + ret.ApplyCast(_returnType.ToCSharpTypeString());
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
						}
						else if (condition.Info.Kind == CXCursorKind.CXCursor_BinaryOperator)
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
							condition.Expression = condition.Expression.Parentize() + " != 0";
						}
					}

					return condition.Expression + "?" + a.Expression + ":" + b.Expression;
				}
				case CXCursorKind.CXCursor_MemberRefExpr:
				{
					var a = ProcessChildByIndex(info.Cursor, 0);

					var op = ".";
					if (a.Info.RecordType != RecordType.Class && a.Info.IsPointer)
					{
						op = "->";
					}

					var result = a.Expression + op + info.Spelling.FixSpecialWords();

					return result;
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
							var arrayType = info.Type.GetPointeeType().ToCSharpTypeString();
							if (_state == State.Functions || info.Type.GetPointeeType().IsClass())
							{
								info.CsType = info.Type.ToCSharpTypeString(true);
							}

							var t = info.Type.GetPointeeType().ToCSharpTypeString();
							if (rvalue.Info.Kind == CXCursorKind.CXCursor_TypeRef ||
							    rvalue.Info.Kind == CXCursorKind.CXCursor_IntegerLiteral)
							{
								if (_state != State.Functions || info.Type.GetPointeeType().IsClass())
								{
									rvalue.Expression = "new PinnedArray<" + t + ">(" + info.Type.GetArraySize() + ")";
								}
								else
								{
									rvalue.Expression = "stackalloc " + arrayType + "[" + info.Type.GetArraySize() + "]";

								}
							}
							else if (rvalue.Info.Kind == CXCursorKind.CXCursor_BinaryOperator)
							{
								rvalue.Expression = "new PinnedArray<" + t + ">(" + rvalue.Expression + ")";
							}
						}
					}

					var name = info.Spelling.FixSpecialWords();
					var expr = info.CsType + " " + name;
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

							expr += " = ";
							expr += rvalue.Expression.ApplyCast(info.CsType);
						}
						else
						{
							var t = info.Type.GetPointeeType().ToCSharpTypeString();
							if (rvalue.Info.Kind == CXCursorKind.CXCursor_InitListExpr)
							{
								if (_state != State.Functions || info.Type.GetPointeeType().IsClass())
								{
									rvalue.Expression = "new PinnedArray<" + t + ">( new " +
									                    info.Type.GetPointeeType().ToCSharpTypeString() + "[] " + rvalue.Expression + ")";
								}
								else
								{
									var arrayType = info.Type.GetPointeeType().ToCSharpTypeString();

									rvalue.Expression = "stackalloc " + arrayType + "[" + info.Type.GetArraySize() + "];\n";
									var size2 = rvalue.Info.Cursor.GetChildrenCount();
									for (var i = 0; i < size2; ++i)
									{
										var exp = ProcessChildByIndex(rvalue.Info.Cursor, i);

										if (!exp.Info.IsPointer)
										{
											exp.Expression = exp.Expression.ApplyCast(exp.Info.CsType);
										}

										rvalue.Expression += name + "[" + i + "] = " + exp.Expression + ";\n";
									}
								}
							}

							if (info.IsPointer && !info.IsArray &&
							    rvalue.Info.IsArray && rvalue.Info.Type.GetPointeeType().kind.IsPrimitiveNumericType() &&
							    rvalue.Info.Kind != CXCursorKind.CXCursor_StringLiteral)
							{
								rvalue.Expression = "((" + info.Type.GetPointeeType().ToCSharpTypeString() + "*)" + rvalue.Expression + ")";
							}
							expr += " = " + rvalue.Expression;
						}
					}
					else if (info.RecordType != RecordType.None && !info.IsPointer)
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
							exp.Expression = exp.Expression.ApplyCast(exp.Info.CsType);
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

					if (info.CsType != expr.Info.CsType)
					{
						e = e.ApplyCast(info.CsType);
					}
					else
					{
						e = e.Parentize();
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

					if (info.CsType != child.Info.CsType)
					{
						expr = expr.ApplyCast(info.CsType);
					}

					if (expr == "0")
					{
						expr = "null";
					}

					return expr;
				}

				case CXCursorKind.CXCursor_UnexposedExpr:
				{
					// Return last child
					var size = info.Cursor.GetChildrenCount();

					if (size == 0)
					{
						return string.Empty;
					}

					var expr = ProcessPossibleChildByIndex(info.Cursor, size - 1);
					if (info.IsPointer && !info.CsType.Contains("PinnedArray") && 
						info.CsType != expr.Info.CsType &&
					    (info.Type.GetPointeeType().IsStruct() ||
						info.Type.GetPointeeType().kind.IsPrimitiveNumericType() ||
					     (expr.Info.Kind == CXCursorKind.CXCursor_IntegerLiteral && info.CsType == "void *")) &&
					    expr.Info.Kind != CXCursorKind.CXCursor_StringLiteral)
					{
						expr.Expression = expr.Expression.ApplyCast(info.CsType).Parentize();
					}

					return expr.Expression;
				}

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

			IndentedWrite("public unsafe static " + _returnType.ToCSharpTypeString());

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
			var typeName = type.ToCSharpTypeString(true);

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
			_globalVariables.Clear();

			_state = State.Structs;
			clang.visitChildren(clang.getTranslationUnitCursor(_translationUnit), VisitStructs, new CXClientData(IntPtr.Zero));

			_state = State.Enums;
			clang.visitChildren(clang.getTranslationUnitCursor(_translationUnit), VisitEnums, new CXClientData(IntPtr.Zero));

			_state = State.GlobalVariables;
			clang.visitChildren(clang.getTranslationUnitCursor(_translationUnit), VisitGlobalVariables,
				new CXClientData(IntPtr.Zero));

			_state = State.Functions;
			clang.visitChildren(clang.getTranslationUnitCursor(_translationUnit), VisitFunctions, new CXClientData(IntPtr.Zero));
		}
	}
}