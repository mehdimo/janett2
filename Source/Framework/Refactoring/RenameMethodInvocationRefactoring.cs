namespace Janett.Framework
{
	using System.Collections;

	using ICSharpCode.NRefactory.Ast;

	public class RenameMethodInvocationRefactoring : Refactoring
	{
		public IRenamer Renamer = new PascalStyleMethodRenamer();

		public override object TrackedVisitInvocationExpression(InvocationExpression invocationExpression, object data)
		{
			if (invocationExpression.TargetObject is IdentifierExpression)
			{
				IdentifierExpression identifierExpression = (IdentifierExpression) invocationExpression.TargetObject;
				TypeDeclaration typeDeclaration = (TypeDeclaration) AstUtil.GetParentOfType(invocationExpression, typeof(TypeDeclaration));

				if (ExistMethodIn(typeDeclaration, invocationExpression))
					identifierExpression.Identifier = Renamer.GetNewName(identifierExpression.Identifier);
			}
			else if (invocationExpression.TargetObject is FieldReferenceExpression)
			{
				FieldReferenceExpression fieldReferenceExpression = (FieldReferenceExpression) invocationExpression.TargetObject;
				Expression invoker = fieldReferenceExpression.TargetObject;
				if (fieldReferenceExpression.FieldName == "CallInternalMethod")
				{
					PrimitiveExpression methodName = (PrimitiveExpression) invocationExpression.Arguments[0];
					if (methodName.Value.ToString().StartsWith("set") || methodName.Value.ToString().StartsWith("get"))
					{
						Expression obj = (Expression) invocationExpression.Arguments[1];
						TypeReference objType = GetExpressionType(obj);
						if (objType != null)
						{
							string fullName = GetFullName(objType);
							if (CodeBase.Types.Contains(fullName))
							{
								TypeDeclaration typeDeclaration = (TypeDeclaration) CodeBase.Types[fullName];
								string propertyName = methodName.Value.ToString().Substring(3);
								if (ContainsProperty(typeDeclaration, propertyName))
									methodName.Value = methodName.Value.ToString().Insert(3, "_");
								else
									methodName.Value = Renamer.GetNewName(methodName.Value.ToString());
							}
						}
					}
					else
						methodName.Value = Renamer.GetNewName(methodName.Value.ToString());
				}
				TypeReference invokerType = GetExpressionType(invoker);
				if (invokerType != null)
				{
					string fullName = GetFullName(invokerType);
					if (CodeBase.Types.Contains(fullName) && !IsInExternalLibraries(fullName))
					{
						TypeDeclaration typeDeclaration = (TypeDeclaration) CodeBase.Types[fullName];

						if (ExistMethodIn(typeDeclaration, invocationExpression))
							fieldReferenceExpression.FieldName = Renamer.GetNewName(fieldReferenceExpression.FieldName);
					}
					else
					{
						TypeMapping mapping = CodeBase.Mappings.GetCounterpart(fullName);
						string mapkey;

						if (ContainsMapping(mapping, invocationExpression, out mapkey))
							fieldReferenceExpression.FieldName = Renamer.GetNewName(fieldReferenceExpression.FieldName);
					}
				}
			}
			return base.TrackedVisitInvocationExpression(invocationExpression, data);
		}

		private bool ExistMethodIn(TypeDeclaration typeDeclaration, InvocationExpression invocationExpression)
		{
			if (GetFullName(typeDeclaration).StartsWith("Helpers."))
				return false;
			bool exist = ContainsMethod(typeDeclaration, invocationExpression);
			if (! exist && typeDeclaration.BaseTypes.Count != 0)
			{
				IList parentTypes = GetParentTypes(typeDeclaration);
				if (parentTypes.Count != 0)
				{
					foreach (TypeDeclaration parentType in parentTypes)
					{
						if (! exist)
							exist = ExistMethodIn(parentType, invocationExpression);
						else break;
					}
				}
			}
			if (!exist && typeDeclaration.Parent is TypeDeclaration
			    && invocationExpression.TargetObject is IdentifierExpression)
			{
				TypeDeclaration parentType = (TypeDeclaration) typeDeclaration.Parent;
				exist = ExistMethodIn(parentType, invocationExpression);
			}
			return exist;
		}

		private bool ContainsMethod(TypeDeclaration typeDeclaration, InvocationExpression invocationExpression)
		{
			string identifier = null;
			if (invocationExpression.TargetObject is IdentifierExpression)
				identifier = ((IdentifierExpression) invocationExpression.TargetObject).Identifier;
			else if (invocationExpression.TargetObject is FieldReferenceExpression)
				identifier = ((FieldReferenceExpression) invocationExpression.TargetObject).FieldName;

			IList methods = AstUtil.GetChildrenWithType(typeDeclaration, typeof(MethodDeclaration));
			foreach (MethodDeclaration method in methods)
			{
				string pascalName = identifier[0].ToString().ToUpper() + identifier.Substring(1);
				if ((method.Name == identifier || method.Name == pascalName) && !IsMethodInExternalTypes(typeDeclaration, method))
				{
					return true;
				}
			}
			return false;
		}

		private IList GetParentTypes(TypeDeclaration typeDeclaration)
		{
			IList types = new ArrayList();
			foreach (TypeReference baseType in typeDeclaration.BaseTypes)
			{
				string fullName = GetFullName(baseType);
				if (CodeBase.Types.Contains(fullName) && !IsInExternalLibraries(fullName))
				{
					TypeDeclaration baseTypeDeclaration = (TypeDeclaration) CodeBase.Types[fullName];
					types.Add(baseTypeDeclaration);
				}
			}
			return types;
		}

		private bool ContainsProperty(TypeDeclaration typeDeclaration, string propertyName)
		{
			IList properties = AstUtil.GetChildrenWithType(typeDeclaration, typeof(PropertyDeclaration));
			foreach (PropertyDeclaration property in properties)
			{
				if (property.Name == propertyName)
					return true;
			}
			return false;
		}
	}
}