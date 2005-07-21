// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Mike Kr�ger" email="mike@icsharpcode.net"/>
//     <version value="$version"/>
// </file>

using System;
using System.Drawing;
using System.Collections;
using System.Collections.Generic;

using ICSharpCode.Core;
using ICSharpCode.TextEditor.Document;
using ICSharpCode.SharpDevelop.Dom;
using ICSharpCode.SharpDevelop.DefaultEditor.Gui.Editor;
using ICSharpCode.TextEditor;
using ICSharpCode.TextEditor.Gui.InsightWindow;


namespace ICSharpCode.SharpDevelop.DefaultEditor.Gui.Editor
{
	public class MethodInsightDataProvider : IInsightDataProvider
	{
		string    fileName = null;
		IDocument document = null;
		TextArea textArea  = null;
		protected List<IMethodOrIndexer> methods  = new List<IMethodOrIndexer>();
		
		public List<IMethodOrIndexer> Methods {
			get {
				return methods;
			}
		}
		
		public int InsightDataCount {
			get {
				return methods.Count;
			}
		}
		
		int defaultIndex = -1;
		
		public int DefaultIndex {
			get {
				return defaultIndex;
			}
			set {
				defaultIndex = value;
			}
		}
		
		public string GetInsightData(int number)
		{
			IMember method = methods[number];
			IAmbience conv = AmbienceService.CurrentAmbience;
			conv.ConversionFlags = ConversionFlags.StandardConversionFlags;
			string documentation = method.Documentation;
			string text;
			if (method is IMethod) {
				text = conv.Convert(method as IMethod);
			} else if (method is IIndexer) {
				text = conv.Convert(method as IIndexer);
			} else {
				text = method.ToString();
			}
			return text + "\n" + CodeCompletionData.GetDocumentation(documentation);
		}
		
		int lookupOffset;
		bool setupOnlyOnce;
		
		/// <summary>
		/// Creates a MethodInsightDataProvider looking at the caret position.
		/// </summary>
		public MethodInsightDataProvider()
		{
			this.lookupOffset = -1;
		}
		
		/// <summary>
		/// Creates a MethodInsightDataProvider looking at the specified position.
		/// </summary>
		public MethodInsightDataProvider(int lookupOffset, bool setupOnlyOnce)
		{
			this.lookupOffset = lookupOffset;
			this.setupOnlyOnce = setupOnlyOnce;
		}
		
		int initialOffset;
		
		public void SetupDataProvider(string fileName, TextArea textArea)
		{
			if (setupOnlyOnce && this.textArea != null) return;
			IDocument document = textArea.Document;
			this.fileName = fileName;
			this.document = document;
			this.textArea = textArea;
			int useOffset = (lookupOffset < 0) ? textArea.Caret.Offset : lookupOffset;
			initialOffset = useOffset;
			
			
			IExpressionFinder expressionFinder = ParserService.GetExpressionFinder(fileName);
			ExpressionResult expressionResult;
			if (expressionFinder == null)
				expressionResult = new ExpressionResult(TextUtilities.GetExpressionBeforeOffset(textArea, useOffset));
			else
				expressionResult = expressionFinder.FindExpression(textArea.Document.TextContent, useOffset - 1);
			if (expressionResult.Expression == null) // expression is null when cursor is in string/comment
				return;
			expressionResult.Expression = expressionResult.Expression.Trim();
			
			// the parser works with 1 based coordinates
			int caretLineNumber = document.GetLineNumberForOffset(useOffset) + 1;
			int caretColumn     = useOffset - document.GetLineSegment(caretLineNumber).Offset + 1;
			SetupDataProvider(fileName, document, expressionResult, caretLineNumber, caretColumn);
		}
		
		protected virtual void SetupDataProvider(string fileName, IDocument document, ExpressionResult expressionResult, int caretLineNumber, int caretColumn)
		{
			bool constructorInsight = false;
			if (expressionResult.Context == ExpressionContext.ObjectCreation) {
				constructorInsight = true;
				expressionResult.Context = ExpressionContext.Default;
			} else if (expressionResult.Context == ExpressionContext.Attribute) {
				constructorInsight = true;
			}
			ResolveResult results = ParserService.Resolve(expressionResult, caretLineNumber, caretColumn, fileName, document.TextContent);
			if (constructorInsight) {
				TypeResolveResult result = results as TypeResolveResult;
				if (result == null)
					return;
				foreach (IMethod method in result.ResolvedType.GetMethods()) {
					if (method.IsConstructor && !method.IsStatic) {
						methods.Add(method);
					}
				}
				
				if (methods.Count == 0 && result.ResolvedClass != null && !result.ResolvedClass.IsAbstract && !result.ResolvedClass.IsStatic) {
					// add default constructor
					methods.Add(Constructor.CreateDefault(result.ResolvedClass));
				}
			} else {
				MethodResolveResult result = results as MethodResolveResult;
				if (result == null)
					return;
				IProjectContent p = ParserService.CurrentProjectContent;
				foreach (IMethod method in result.ContainingType.GetMethods()) {
					if (p.Language.NameComparer.Equals(method.Name, result.Name)) {
						methods.Add(method);
					}
				}
			}
		}
		
		public bool CaretOffsetChanged()
		{
			bool closeDataProvider = textArea.Caret.Offset <= initialOffset;
			int brackets = 0;
			int curlyBrackets = 0;
			if (!closeDataProvider) {
				bool insideChar   = false;
				bool insideString = false;
				for (int offset = initialOffset; offset < Math.Min(textArea.Caret.Offset, document.TextLength); ++offset) {
					char ch = document.GetCharAt(offset);
					switch (ch) {
						case '\'':
							insideChar = !insideChar;
							break;
						case '(':
							if (!(insideChar || insideString)) {
								++brackets;
							}
							break;
						case ')':
							if (!(insideChar || insideString)) {
								--brackets;
							}
							if (brackets <= 0) {
								return true;
							}
							break;
						case '"':
							insideString = !insideString;
							break;
						case '}':
							if (!(insideChar || insideString)) {
								--curlyBrackets;
							}
							if (curlyBrackets < 0) {
								return true;
							}
							break;
						case '{':
							if (!(insideChar || insideString)) {
								++curlyBrackets;
							}
							break;
						case ';':
							if (!(insideChar || insideString)) {
								return true;
							}
							break;
					}
				}
			}
			
			return closeDataProvider;
		}
		
		public bool CharTyped()
		{
//			int offset = document.Caret.Offset - 1;
//			if (offset >= 0) {
//				return document.GetCharAt(offset) == ')';
//			}
			return false;
		}
	}
}
