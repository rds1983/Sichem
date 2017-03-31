using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using ClangSharp;
using SealangSharp;

namespace Sichem
{
	public enum RecordType
	{
		None,
		Struct,
		Class
	}

	public static class Utility
	{
		public static HashSet<string> Structs = new HashSet<string>();

		private static readonly Stack<Func<CXCursor, CXChildVisitResult>> _visitorActionStack =
			new Stack<Func<CXCursor, CXChildVisitResult>>();

		public static bool IsInSystemHeader(this CXCursor cursor)
		{
			return clang.Location_isInSystemHeader(clang.getCursorLocation(cursor)) != 0;
		}

		public static bool IsPtrToConstChar(this CXType type)
		{
			var pointee = clang.getPointeeType(type);

			if (clang.isConstQualifiedType(pointee) != 0)
			{
				switch (pointee.kind)
				{
					case CXTypeKind.CXType_Char_S:
						return true;
				}
			}

			return false;
		}

		public static string ToPlainTypeString(this CXType type)
		{
			var canonical = clang.getCanonicalType(type);
			switch (type.kind)
			{
				case CXTypeKind.CXType_Bool:
					return "bool";
				case CXTypeKind.CXType_UChar:
				case CXTypeKind.CXType_Char_U:
					return "byte";
				case CXTypeKind.CXType_SChar:
				case CXTypeKind.CXType_Char_S:
					return "sbyte";
				case CXTypeKind.CXType_UShort:
					return "ushort";
				case CXTypeKind.CXType_Short:
					return "short";
				case CXTypeKind.CXType_Float:
					return "float";
				case CXTypeKind.CXType_Double:
					return "double";
				case CXTypeKind.CXType_Int:
					return "int";
				case CXTypeKind.CXType_UInt:
					return "uint";
				case CXTypeKind.CXType_Pointer:
				case CXTypeKind.CXType_NullPtr: // ugh, what else can I do?
					return "IntPtr";
				case CXTypeKind.CXType_Long:
					return "int";
				case CXTypeKind.CXType_ULong:
					return "int";
				case CXTypeKind.CXType_LongLong:
					return "long";
				case CXTypeKind.CXType_ULongLong:
					return "ulong";
				case CXTypeKind.CXType_Void:
					return "void";
				case CXTypeKind.CXType_Unexposed:
					if (canonical.kind == CXTypeKind.CXType_Unexposed)
					{
						return clang.getTypeSpelling(canonical).ToString();
					}
					return canonical.ToPlainTypeString();
				default:
					return type.ToString();
			}
		}

		public static CXType Desugar(this CXType type)
		{
			if (type.kind == CXTypeKind.CXType_Typedef)
			{
				return clang.getCanonicalType(type);
			}

			return type;
		}

		private static string ProcessPointerType(this CXType type, bool treatArrayAsPointer)
		{
			type = type.Desugar();

			if (type.kind == CXTypeKind.CXType_Void)
			{
				return "void *";
			}

			var sb = new StringBuilder();

			sb.Append(ToCSharpTypeString(type, treatArrayAsPointer));

			RecordType recordType;
			string recordName;
			type.ResolveRecord(out recordType, out recordName);
			if (recordType != RecordType.Class)
			{
				sb.Append("*");
			}

			return sb.ToString();
		}

		public static string ToCSharpTypeString(this CXType type, bool treatArrayAsPointer = false)
		{
			var isConstQualifiedType = clang.isConstQualifiedType(type) != 0;
			var spelling = string.Empty;

			var sb = new StringBuilder();
			type = type.Desugar();
			switch (type.kind)
			{
				case CXTypeKind.CXType_Record:
					spelling = clang.getTypeSpelling(type).ToString();
					break;
				case CXTypeKind.CXType_Enum:
					spelling = "int";
					break;
				case CXTypeKind.CXType_IncompleteArray:
					sb.Append(clang.getArrayElementType(type).ToCSharpTypeString());
					spelling = "[]";
					break;
				case CXTypeKind.CXType_Unexposed: // Often these are enums and canonical type gets you the enum spelling
					var canonical = clang.getCanonicalType(type);
					// unexposed decl which turns into a function proto seems to be an un-typedef'd fn pointer
					spelling = canonical.kind == CXTypeKind.CXType_FunctionProto
						? "IntPtr"
						: clang.getTypeSpelling(canonical).ToString();
					break;
				case CXTypeKind.CXType_ConstantArray:
					var t = clang.getArrayElementType(type);
					if (treatArrayAsPointer)
					{
						sb.Append(ProcessPointerType(t, true));
					}
					else
					{
						sb.Append("PinnedArray<" + t.ToCSharpTypeString() + ">");
					}
					break;
				case CXTypeKind.CXType_Pointer:
					sb.Append(ProcessPointerType(clang.getPointeeType(type), treatArrayAsPointer));
					break;
				default:
					spelling = clang.getCanonicalType(type).ToPlainTypeString();
					break;
			}

			if (isConstQualifiedType)
			{
				spelling = spelling.Replace("const ", string.Empty); // ugh
			}

			sb.Append(spelling);
			return sb.ToString();
		}

		public static void ResolveRecord(this CXType type, out RecordType recordType, out string name)
		{
			recordType = RecordType.None;
			name = string.Empty;
			var run = true;
			while (run)
			{
				type = type.Desugar();

				switch (type.kind)
				{
					case CXTypeKind.CXType_Record:
					{
						name = clang.getTypeSpelling(type).ToString();
						var isConstQualifiedType = clang.isConstQualifiedType(type) != 0;
						if (isConstQualifiedType)
						{
							name = name.Replace("const ", string.Empty); // ugh
						}
						recordType = Structs.Contains(name) ? RecordType.Struct : RecordType.Class;
						return;
					}

					case CXTypeKind.CXType_IncompleteArray:
					case CXTypeKind.CXType_ConstantArray:
						type = clang.getArrayElementType(type);
						continue;
					case CXTypeKind.CXType_Pointer:
						type = clang.getPointeeType(type);
						continue;
					default:
						run = false;
						break;
				}
			}
		}

		public static CXType GetPointeeType(this CXType type)
		{
			while (true)
			{
				type = type.Desugar();

				switch (type.kind)
				{
					case CXTypeKind.CXType_IncompleteArray:
					case CXTypeKind.CXType_ConstantArray:
						type = clang.getArrayElementType(type);
						continue;
					case CXTypeKind.CXType_Pointer:
						type = clang.getPointeeType(type);
						continue;
				}

				return type;
			}
		}

		public static string FixSpecialWords(this string name)
		{
			if (name == "out")
			{
				name = "_out_";
			}

			if (name == "in")
			{
				name = "_in_";
			}

			return name;
		}

		private static CXChildVisitResult ActionVisitor(CXCursor cursor, CXCursor parent, IntPtr data)
		{
			return _visitorActionStack.Peek()(cursor);
		}

		public static void VisitWithAction(this CXCursor cursor, Func<CXCursor, CXChildVisitResult> func)
		{
			if (func == null)
			{
				throw new ArgumentNullException("func");
			}

			_visitorActionStack.Push(func);
			clang.visitChildren(cursor, ActionVisitor, new CXClientData(IntPtr.Zero));
			_visitorActionStack.Pop();
		}

		public static CXCursor? FindChild(this CXCursor cursor, CXCursorKind kind)
		{
			CXCursor? _findChildResult = null;

			VisitWithAction(cursor, c =>
			{
				if (clang.getCursorKind(c) != kind) return CXChildVisitResult.CXChildVisit_Recurse;

				_findChildResult = c;
				return CXChildVisitResult.CXChildVisit_Break;
			});

			return _findChildResult;
		}

		public static string[] Tokenize(this CXCursor cursor, CXTranslationUnit translationUnit)
		{
			var range = clang.getCursorExtent(cursor);
			IntPtr nativeTokens;
			uint numTokens;
			clang.tokenize(translationUnit, range, out nativeTokens, out numTokens);

			var result = new List<string>();
			var tokens = new CXToken[numTokens];
			for (uint i = 0; i < numTokens; ++i)
			{
				tokens[i] = (CXToken) Marshal.PtrToStructure(nativeTokens, typeof (CXToken));
				nativeTokens += Marshal.SizeOf(typeof (CXToken));

				var name = clang.getTokenSpelling(translationUnit, tokens[i]).ToString();
				result.Add(name);
			}

			return result.ToArray();
		}

		public static string EnsureStatementFinished(this string statement)
		{
			var trimmed = statement.Trim();

			if (string.IsNullOrEmpty(trimmed))
			{
				return trimmed;
			}

			if (!trimmed.EndsWith(";") && !trimmed.EndsWith("}"))
			{
				return statement + ";";
			}

			return statement;
		}

		public static int GetChildrenCount(this CXCursor cursor)
		{
			var result = 0;

			cursor.VisitWithAction(c =>
			{
				++result;
				return CXChildVisitResult.CXChildVisit_Continue;
			});

			return result;
		}

		public static CXCursor? GetFirstChild(this CXCursor cursor)
		{
			return GetChildByIndex(cursor, 0);
		}

		public static CXCursor? GetChildByIndex(this CXCursor cursor, int index)
		{
			CXCursor? result = null;

			var curIndex = 0;
			cursor.VisitWithAction(c =>
			{
				if (curIndex == index)
				{
					result = c;
					return CXChildVisitResult.CXChildVisit_Break;
				}

				++curIndex;
				return CXChildVisitResult.CXChildVisit_Continue;
			});

			return result;
		}

		public static CXCursor EnsureChildByIndex(this CXCursor cursor, int index)
		{
			CXCursor? result = cursor.GetChildByIndex(index);

			if (result == null)
			{
				throw new Exception(string.Format("Cursor doesnt have {0}'s child", index));
			}

			return result.Value;
		}


		public static bool IsBinaryOperator(this BinaryOperatorKind op)
		{
			return op == BinaryOperatorKind.And || op == BinaryOperatorKind.Or;
		}

		public static bool IsLogicalBinaryOperator(this BinaryOperatorKind op)
		{
			return op == BinaryOperatorKind.LAnd || op == BinaryOperatorKind.LOr;
		}

		public static bool IsLogicalBooleanOperator(this BinaryOperatorKind op)
		{
			return op == BinaryOperatorKind.LAnd || op == BinaryOperatorKind.LOr ||
			       op == BinaryOperatorKind.EQ || op == BinaryOperatorKind.GE ||
			       op == BinaryOperatorKind.GT || op == BinaryOperatorKind.LT;
		}

		public static bool IsBooleanOperator(this BinaryOperatorKind op)
		{
			return op == BinaryOperatorKind.LAnd || op == BinaryOperatorKind.LOr ||
			       op == BinaryOperatorKind.EQ || op == BinaryOperatorKind.NE ||
			       op == BinaryOperatorKind.GE || op == BinaryOperatorKind.LE ||
			       op == BinaryOperatorKind.GT || op == BinaryOperatorKind.LT ||
			       op == BinaryOperatorKind.And || op == BinaryOperatorKind.Or;
		}

		public static bool IsAssign(this BinaryOperatorKind op)
		{
			return op == BinaryOperatorKind.AddAssign || op == BinaryOperatorKind.AndAssign ||
			       op == BinaryOperatorKind.Assign || op == BinaryOperatorKind.DivAssign ||
			       op == BinaryOperatorKind.MulAssign || op == BinaryOperatorKind.OrAssign ||
			       op == BinaryOperatorKind.RemAssign || op == BinaryOperatorKind.ShlAssign ||
			       op == BinaryOperatorKind.ShrAssign || op == BinaryOperatorKind.SubAssign ||
			       op == BinaryOperatorKind.XorAssign;
		}

		internal static string GetExpression(this CursorProcessResult cursorProcessResult)
		{
			return cursorProcessResult != null ? cursorProcessResult.Expression : string.Empty;
		}

		public static bool IsPrimitiveNumericType(this CXTypeKind kind)
		{
			switch (kind)
			{
				case CXTypeKind.CXType_Bool:
				case CXTypeKind.CXType_UChar:
				case CXTypeKind.CXType_Char_U:
				case CXTypeKind.CXType_SChar:
				case CXTypeKind.CXType_Char_S:
				case CXTypeKind.CXType_UShort:
				case CXTypeKind.CXType_Short:
				case CXTypeKind.CXType_Float:
				case CXTypeKind.CXType_Double:
				case CXTypeKind.CXType_Int:
				case CXTypeKind.CXType_UInt:
				case CXTypeKind.CXType_Long:
				case CXTypeKind.CXType_ULong:
				case CXTypeKind.CXType_LongLong:
				case CXTypeKind.CXType_ULongLong:
					return true;
			}

			return false;
		}

		public static bool IsPointer(this CXTypeKind kind)
		{
			return kind == CXTypeKind.CXType_Pointer || kind == CXTypeKind.CXType_ConstantArray;
		}

		public static bool IsPointer(this CXType type)
		{
			return type.kind.IsPointer();
		}

		public static bool IsStruct(this CXType type)
		{
			RecordType rt;
			string name;
			ResolveRecord(type, out rt, out name);

			return rt == RecordType.Struct;
		}

		public static bool IsClass(this CXType type)
		{
			RecordType rt;
			string name;
			ResolveRecord(type, out rt, out name);

			return rt == RecordType.Class;
		}

		public static bool IsArray(this CXType type)
		{
			return type.kind == CXTypeKind.CXType_ConstantArray ||
			       type.kind == CXTypeKind.CXType_DependentSizedArray ||
			       type.kind == CXTypeKind.CXType_VariableArray;
		}

		public static long GetArraySize(this CXType type)
		{
			return clang.getArraySize(type);
		}

		public static bool IsUnaryOperatorPre(this UnaryOperatorKind type)
		{
			switch (type)
			{
				case UnaryOperatorKind.PreInc:
				case UnaryOperatorKind.PreDec:
				case UnaryOperatorKind.Plus:
				case UnaryOperatorKind.Minus:
				case UnaryOperatorKind.Not:
				case UnaryOperatorKind.LNot:
				case UnaryOperatorKind.AddrOf:
				case UnaryOperatorKind.Deref:
					return true;
			}

			return false;
		}

		public static string Parentize(this string expr)
		{
			if (expr.StartsWith("(") && expr.EndsWith(")"))
			{
				var pcount = 1;
				for (var i = 1; i < expr.Length - 1; ++i)
				{
					var c = expr[i];

					if (c == '(')
					{
						++pcount;
					}
					else if (c == ')')
					{
						--pcount;
					}

					if (pcount == 0)
					{
						break;
					}
				}

				if (pcount > 0)
				{
					return expr;
				}

			}

			return "(" + expr + ")";
		}

		public static string ApplyCast(this string expr, string type)
		{
			if (string.IsNullOrEmpty(expr))
			{
				return expr;
			}

			var ptype = type.Parentize();
			if (expr.StartsWith(ptype))
			{
				return expr;
			}

			return ptype + expr.Parentize();
		}

		public static string Curlize(this string expr)
		{
			expr = expr.Trim();

			if (expr.StartsWith("{") && expr.EndsWith("}"))
			{
				return expr;
			}

			return "{" + expr + "}";
		}
	}
}