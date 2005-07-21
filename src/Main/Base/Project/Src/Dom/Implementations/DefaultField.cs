﻿// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Mike Krüger" email="mike@icsharpcode.net"/>
//     <version value="$version"/>
// </file>
using System;
using System.Reflection;

namespace ICSharpCode.SharpDevelop.Dom
{
	[Serializable]
	public class DefaultField : AbstractMember, IField
	{
		public override string DocumentationTag {
			get {
				return "F:" + this.DotNetName;
			}
		}
		
		public DefaultField(IClass declaringType, string name) : base(declaringType, name)
		{
		}
		
		public DefaultField(IReturnType type, string name, ModifierEnum m, IRegion region, IClass declaringType) : base(declaringType, name)
		{
			this.ReturnType = type;
			this.Region = region;
			this.Modifiers = m;
		}
		
		public override IMember Clone()
		{
			return new DefaultField(ReturnType, Name, Modifiers, Region, DeclaringType);
		}
		
		public virtual int CompareTo(IField field)
		{
			int cmp;
			
			cmp = base.CompareTo((IDecoration)field);
			if (cmp != 0) {
				return cmp;
			}
			
			if (FullyQualifiedName != null) {
				return FullyQualifiedName.CompareTo(field.FullyQualifiedName);
			}
			return 0;
		}
		
		int IComparable.CompareTo(object value)
		{
			return CompareTo((IField)value);
		}
	}
}
