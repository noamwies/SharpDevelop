// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Daniel Grunwald" email="daniel@danielgrunwald.de"/>
//     <version>$Revision$</version>
// </file>

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ICSharpCode.Core;
using ICSharpCode.Core.Presentation;
using ICSharpCode.NRefactory.Visitors;
using ICSharpCode.SharpDevelop.Bookmarks;
using ICSharpCode.SharpDevelop.Dom;
using ICSharpCode.SharpDevelop.Dom.NRefactoryResolver;
using ICSharpCode.SharpDevelop.Dom.Refactoring;
using ICSharpCode.SharpDevelop.Editor;
using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.SharpDevelop.Gui.ClassBrowser;
using ICSharpCode.SharpDevelop.Project;
using Ast = ICSharpCode.NRefactory.Ast;

namespace ICSharpCode.SharpDevelop.Refactoring
{
	public class RefactoringMenuContext
	{
		public ITextEditor Editor;
		public ExpressionResult ExpressionResult;
		public ResolveResult ResolveResult;
		public bool IsDefinition;
	}
	
	public interface IRefactoringMenuItemFactory
	{
		MenuItem Create(RefactoringMenuContext context);
	}
	
	/// <summary>
	/// Build refactoring commands for the item that has been clicked on in the text editor.
	/// The commands are inserted to the top level of the context menu.
	/// Path:
	/// /SharpDevelop/ViewContent/TextEditor/ContextMenu, id=Refactoring
	/// </summary>
	public class RefactoringMenuBuilder : IMenuItemBuilder
	{
		public ICollection BuildItems(Codon codon, object owner)
		{
			MenuItem item;
			
			ITextEditor textEditor = (ITextEditor)owner;
			if (string.IsNullOrEmpty(textEditor.FileName))
				return new object[0];
			List<object> resultItems = new List<object>();
			IDocument doc = textEditor.Document;
			int caretLine = textEditor.Caret.Line;
			
			// list of dotnet names that have definitions in this line
			List<string> definitions = new List<string>();
			
			// Include menu for member that has been clicked on
			IExpressionFinder expressionFinder = ParserService.GetExpressionFinder(textEditor.FileName);
			ExpressionResult expressionResult;
			ResolveResult rr;
			int insertIndex = resultItems.Count;	// Insert items at this position to get the outermost expression first, followed by the inner expressions (if any).
			expressionResult = FindFullExpressionAtCaret(textEditor, expressionFinder);
		repeatResolve:
			rr = ResolveExpressionAtCaret(textEditor, expressionResult);
			RefactoringMenuContext context = new RefactoringMenuContext {
				Editor = textEditor,
				ResolveResult = rr,
				ExpressionResult = expressionResult
			};
			item = null;
			
			if (rr is MethodGroupResolveResult) {
				item = MakeItem(definitions, ((MethodGroupResolveResult)rr).GetMethodIfSingleOverload());
			} else if (rr is MemberResolveResult) {
				MemberResolveResult mrr = (MemberResolveResult)rr;
				item = MakeItem(definitions, mrr.ResolvedMember);
				if (RefactoringService.FixIndexerExpression(expressionFinder, ref expressionResult, mrr)) {
					if (item != null) {
						resultItems.Insert(insertIndex, item);
					}
					// Include menu for the underlying expression of the
					// indexer expression as well.
					goto repeatResolve;
				}
			} else if (rr is TypeResolveResult) {
				item = MakeItem(definitions, ((TypeResolveResult)rr).ResolvedClass);
			} else if (rr is LocalResolveResult) {
				context.IsDefinition = caretLine == ((LocalResolveResult)rr).VariableDefinitionRegion.BeginLine;
				item = MakeItem((LocalResolveResult)rr, context);
				insertIndex = 0;	// Insert local variable menu item at the topmost position.
			} else if (rr is UnknownIdentifierResolveResult) {
				item = MakeItemForResolveError((UnknownIdentifierResolveResult)rr, expressionResult.Context, textEditor);
				insertIndex = 0;	// Insert menu item at the topmost position.
			} else if (rr is UnknownConstructorCallResolveResult) {
				item = MakeItemForResolveError((UnknownConstructorCallResolveResult)rr, expressionResult.Context, textEditor);
				insertIndex = 0;	// Insert menu item at the topmost position.
			}
			if (item != null) {
				resultItems.Insert(insertIndex, item);
			}
			
			AddRefactoringItemsToTheBeginning(resultItems, context);
			
			// Include menu for current class and method
			ICompilationUnit cu = null;
			IMember callingMember = null;
			if (rr != null && rr.CallingMember != null) {
				callingMember = rr.CallingMember;
			} else {
				ParseInformation parseInfo = ParserService.GetParseInformation(textEditor.FileName);
				if (parseInfo != null) {
					cu = parseInfo.CompilationUnit;
					if (cu != null) {
						IClass callingClass = cu.GetInnermostClass(caretLine, textEditor.Caret.Column);
						callingMember = GetCallingMember(callingClass, caretLine, textEditor.Caret.Column);
					}
				}
			}
			if (callingMember != null) {
				item = MakeItem(definitions, callingMember);
				if (item != null) {
					item.Header = StringParser.Parse("${res:SharpDevelop.Refactoring.CurrentMethod}: ") + callingMember.Name;
					resultItems.Add(item);
				}
			}
			
			if (resultItems.Count > 0) {
				resultItems.Add(new Separator());
			}
			
			AddContextItems(resultItems, context);
			
			return resultItems;
		}
		
		void AddRefactoringItemsToTheBeginning(List<object> resultItems, RefactoringMenuContext refactoringContext)
		{
			List<IRefactoringMenuItemFactory> refactorings = AddInTree.BuildItems<IRefactoringMenuItemFactory>("/SharpDevelop/ViewContent/TextEditor/ContextMenu/Refactorings", null, false);
			
			foreach (IRefactoringMenuItemFactory r in refactorings) {
				MenuItem refactoringItem = r.Create(refactoringContext);
				if (refactoringItem != null)
					resultItems.Insert(0, refactoringItem);
			}
		}
		
		#region AddTopLevelContextItems
		
		/// <summary>
		/// Adds top-level context items like "Go to definition", "Find references", "Find derived classes", "Find overrides"
		/// </summary>
		void AddContextItems(List<object> resultItems, RefactoringMenuContext context)
		{
			var contextItems = MakeContextItems(context);
			resultItems.AddRange(contextItems);
			if (contextItems.Count > 0)
				resultItems.Add(new Separator());
		}
		
		List<object> MakeContextItems(RefactoringMenuContext context)
		{
			var contextItems = new List<object>();
			if (context.ResolveResult is TypeResolveResult) {
				var clickedClass = ((TypeResolveResult)context.ResolveResult).ResolvedClass;
				contextItems.AddIfNotNull(MakeFindDerivedClassesItem(clickedClass, context));
				contextItems.AddIfNotNull(MakeFindBaseClassesItem(clickedClass, context));
			}
			if (context.ResolveResult is MemberResolveResult) {
				IMember member = ((MemberResolveResult)context.ResolveResult).ResolvedMember as IMember;
				if (member != null && member.IsOverridable) {
					contextItems.AddIfNotNull(MakeFindOverridesItem(member, context));
				}
			}
			return contextItems;
		}
		
		MenuItem MakeFindDerivedClassesItem(IClass baseClass, RefactoringMenuContext context)
		{
			if (baseClass == null)
				return null;
			var item = new MenuItem { Header = MenuService.ConvertLabel(StringParser.Parse("${res:SharpDevelop.Refactoring.FindDerivedClassesCommand}")) };
			item.Icon = ClassBrowserIconService.Class.CreateImage();
			item.InputGestureText = new KeyGesture(Key.F9).GetDisplayStringForCulture(Thread.CurrentThread.CurrentUICulture);
			item.Click += delegate {
				ContextActionsHelper.MakePopupWithDerivedClasses(baseClass).OpenAtCaretAndFocus(context.Editor);
			};
			return item;
		}
		
		MenuItem MakeFindBaseClassesItem(IClass @class, RefactoringMenuContext context)
		{
			if (@class == null)
				return null;
			var item = new MenuItem { Header = MenuService.ConvertLabel("${res:SharpDevelop.Refactoring.FindBaseClassesCommand}") };
			item.Icon = ClassBrowserIconService.Interface.CreateImage();
			//item.InputGestureText = new KeyGesture(Key.F10).GetDisplayStringForCulture(Thread.CurrentThread.CurrentUICulture);
			item.Click += delegate {
				ContextActionsHelper.MakePopupWithBaseClasses(@class).OpenAtCaretAndFocus(context.Editor);
			};
			return item;
		}
		
		MenuItem MakeFindOverridesItem(IMember member, RefactoringMenuContext context)
		{
			if (member == null)
				return null;
			var item = new MenuItem { Header = MenuService.ConvertLabel(StringParser.Parse("${res:SharpDevelop.Refactoring.FindOverridesCommand}")) };
			item.Icon = ClassBrowserIconService.Method.CreateImage();
			item.InputGestureText = new KeyGesture(Key.F9).GetDisplayStringForCulture(Thread.CurrentThread.CurrentUICulture);
			item.Click += delegate {
				ContextActionsHelper.MakePopupWithOverrides(member).OpenAtCaretAndFocus(context.Editor);
			};
			return item;
		}
		
		#endregion
		
		MenuItem MakeItemForResolveError(UnknownIdentifierResolveResult unknownIdent, ExpressionContext context, ITextEditor textArea)
		{
			return MakeItemForActions(RefactoringService.GetAddUsingActions(unknownIdent, textArea), unknownIdent.Identifier, unknownIdent.CallingClass);
		}
		
		MenuItem MakeItemForResolveError(UnknownConstructorCallResolveResult unknownConstructor, ExpressionContext context, ITextEditor textArea)
		{
			return MakeItemForActions(RefactoringService.GetAddUsingActions(unknownConstructor, textArea), unknownConstructor.TypeName, unknownConstructor.CallingClass);
		}
		
		MenuItem MakeItemForActions(IEnumerable<RefactoringService.AddUsingAction> menuActions, string unknownClassName, IClass callingClass)
		{
			var actions = menuActions.ToList();
			if (actions.Count == 0)
				return null;
			MenuItem item = MakeItemInternal(unknownClassName, ClassBrowserIconService.GotoArrow, callingClass.CompilationUnit, DomRegion.Empty);
			foreach (var action in actions) {
				MenuItem subItem = new MenuItem();
				subItem.Header = "using " + action.NewNamespace;
				subItem.Icon = ClassBrowserIconService.Namespace.CreateImage();
				item.Items.Add(subItem);
				subItem.Click += delegate {
					action.Execute();
				};
			}
			return item;
		}
		
		IMember GetCallingMember(IClass callingClass, int caretLine, int caretColumn)
		{
			if (callingClass == null) {
				return null;
			}
			foreach (IMethod method in callingClass.Methods) {
				if (method.BodyRegion.IsInside(caretLine, caretColumn)) {
					return method;
				}
			}
			foreach (IProperty property in callingClass.Properties) {
				if (property.BodyRegion.IsInside(caretLine, caretColumn)) {
					return property;
				}
			}
			return null;
		}
		
		MenuItem MakeItem(LocalResolveResult local, RefactoringMenuContext context)
		{
			Debug.Assert(local == context.ResolveResult);
			MenuItem item = MakeItemInternal(local.VariableName,
			                                 local.IsParameter ? ClassBrowserIconService.Parameter : ClassBrowserIconService.LocalVariable,
			                                 local.CallingClass.CompilationUnit,
			                                 context.IsDefinition ? DomRegion.Empty : local.VariableDefinitionRegion);
			string treePath = "/SharpDevelop/ViewContent/DefaultTextEditor/Refactoring/";
			treePath += local.IsParameter ? "Parameter" : "LocalVariable";
			if (context.IsDefinition) treePath += "Definition";
			foreach (object obj in MenuService.CreateMenuItems(null, context, treePath))
				item.Items.Add(obj);
			return item;
		}
		
		MenuItem MakeItem(List<string> definitions, IMember member)
		{
			if (member == null) return null;
			if (definitions.Contains(member.DotNetName)) return null;
			definitions.Add(member.DotNetName);
			MenuItem item = MakeItem(MemberNode.Create(member), member.DeclaringType.CompilationUnit, member.Region);
			MenuItem declaringType = MakeItem(null, member.DeclaringType);
			if (declaringType != null) {
				item.Items.Add(new Separator());
				declaringType.Header = StringParser.Parse("${res:SharpDevelop.Refactoring.DeclaringType}: ") + declaringType.Header;
				item.Items.Add(declaringType);
			}
			return item;
		}
		
		MenuItem MakeItem(List<string> definitions, IClass c)
		{
			if (c == null) return null;
			if (definitions != null) {
				if (definitions.Contains(c.DotNetName)) return null;
				definitions.Add(c.DotNetName);
			}
			return MakeItem(new ClassNode((IProject)c.ProjectContent.Project, c), c.CompilationUnit, c.Region);
		}
		
		MenuItem MakeItemInternal(string title, IImage image, ICompilationUnit cu, DomRegion region)
		{
			MenuItem item = new MenuItem();
			item.Header = title;
			item.Icon = image.CreateImage();
			
			//ToolStripMenuItem titleItem = new ToolStripMenuItem(title);
			//titleItem.Enabled = false;
			//item.DropDown.Items.Add(titleItem);
			//item.DropDown.Items.Add(new ToolStripSeparator());
			
			if (cu != null && cu.FileName != null && !region.IsEmpty) {
				MenuItem gotoDefinitionItem = new MenuItem();
				gotoDefinitionItem.Header = MenuService.ConvertLabel(StringParser.Parse("${res:ICSharpCode.NAntAddIn.GotoDefinitionMenuLabel}"));
				gotoDefinitionItem.Icon = ClassBrowserIconService.GotoArrow.CreateImage();
				gotoDefinitionItem.InputGestureText = new KeyGesture(Key.Enter, ModifierKeys.Control).GetDisplayStringForCulture(Thread.CurrentThread.CurrentUICulture);
				gotoDefinitionItem.Click += delegate {
					FileService.JumpToFilePosition(cu.FileName, region.BeginLine, region.BeginColumn);
				};
				item.Items.Add(gotoDefinitionItem);
				item.Items.Add(new Separator());
			}
			return item;
		}
		
		MenuItem MakeItem(ExtTreeNode classBrowserTreeNode, ICompilationUnit cu, DomRegion region)
		{
			MenuItem item = MakeItemInternal(classBrowserTreeNode.Text, ClassBrowserIconService.GetImageByIndex(classBrowserTreeNode.ImageIndex), cu, region);
			foreach (object obj in MenuService.CreateMenuItems(null, classBrowserTreeNode, classBrowserTreeNode.ContextmenuAddinTreePath))
				item.Items.Add(obj);
			return item;
		}
		
		static ExpressionResult FindFullExpressionAtCaret(ITextEditor textArea, IExpressionFinder expressionFinder)
		{
			if (expressionFinder != null) {
				return expressionFinder.FindFullExpression(textArea.Document.Text, textArea.Caret.Offset);
			} else {
				return new ExpressionResult(null);
			}
		}
		
		static ResolveResult ResolveExpressionAtCaret(ITextEditor textArea, ExpressionResult expressionResult)
		{
			if (expressionResult.Expression != null) {
				return ParserService.Resolve(expressionResult, textArea.Caret.Line, textArea.Caret.Column, textArea.FileName, textArea.Document.Text);
			}
			return null;
		}
	}
}





