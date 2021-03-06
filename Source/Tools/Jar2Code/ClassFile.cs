/*
  Copyright (C) 2002, 2003, 2004, 2005 Jeroen Frijters

  This software is provided 'as-is', without any express or implied
  warranty.  In no event will the authors be held liable for any damages
  arising from the use of this software.

  Permission is granted to anyone to use this software for any purpose,
  including commercial applications, and to alter it and redistribute it
  freely, subject to the following restrictions:

  1. The origin of this software must not be misrepresented; you must not
     claim that you wrote the original software. If you use this software
     in a product, an acknowledgment in the product documentation would be
     appreciated but is not required.
  2. Altered source versions must be plainly marked as such, and must not be
     misrepresented as being the original software.
  3. This notice may not be removed or altered from any source distribution.

  Jeroen Frijters
  jeroen@frijters.net
  
*/
#define FUZZ_HACK

using System;
using System.Collections;

using __Modifiers = IKVM.Attributes.Modifiers;

internal sealed class ClassFile
{
	private ConstantPoolItem[] constantpool;
	private string[] utf8_cp;
	private __Modifiers access_flags;
	private ushort this_class;
	private ushort super_class;
	private ConstantPoolItemClass[] interfaces;
	private Field[] fields;
	private Method[] methods;
	private string sourceFile;
	private ClassFile outerClass;
	private ushort majorVersion;
	private bool deprecated;
	private string ikvmAssembly;
	private InnerClass[] innerClasses;

	private class SupportedVersions
	{
		internal static readonly int Minimum = 45;
		internal static readonly int Maximum = Environment.GetEnvironmentVariable("IKVM_EXPERIMENTAL_JDK_5_0") == null ? 48 : 49;
	}

	internal ClassFile(byte[] buf, int offset, int length, string inputClassName, bool allowJavaLangObject)
	{
		try
		{
			BigEndianBinaryReader br = new BigEndianBinaryReader(buf, offset, length);
			if (br.ReadUInt32() != 0xCAFEBABE)
			{
				throw new ClassFormatError("{0} (Bad magic number)", inputClassName);
			}
			int minorVersion = br.ReadUInt16();
			majorVersion = br.ReadUInt16();
			if (majorVersion < SupportedVersions.Minimum || majorVersion > SupportedVersions.Maximum)
			{
				throw new UnsupportedClassVersionError(inputClassName + " (" + majorVersion + "." + minorVersion + ")");
			}
			int constantpoolcount = br.ReadUInt16();
			constantpool = new ConstantPoolItem[constantpoolcount];
			utf8_cp = new string[constantpoolcount];
			for (int i = 1; i < constantpoolcount; i++)
			{
				Constant tag = (Constant) br.ReadByte();
				switch (tag)
				{
					case Constant.Class:
						constantpool[i] = new ConstantPoolItemClass(br);
						break;
					case Constant.Double:
						constantpool[i] = new ConstantPoolItemDouble(br);
						i++;
						break;
					case Constant.Fieldref:
						constantpool[i] = new ConstantPoolItemFieldref(br);
						break;
					case Constant.Float:
						constantpool[i] = new ConstantPoolItemFloat(br);
						break;
					case Constant.Integer:
						constantpool[i] = new ConstantPoolItemInteger(br);
						break;
					case Constant.InterfaceMethodref:
						constantpool[i] = new ConstantPoolItemInterfaceMethodref(br);
						break;
					case Constant.Long:
						constantpool[i] = new ConstantPoolItemLong(br);
						i++;
						break;
					case Constant.Methodref:
						constantpool[i] = new ConstantPoolItemMethodref(br);
						break;
					case Constant.NameAndType:
						constantpool[i] = new ConstantPoolItemNameAndType(br);
						break;
					case Constant.String:
						constantpool[i] = new ConstantPoolItemString(br);
						break;
					case Constant.Utf8:
						utf8_cp[i] = br.ReadString(inputClassName);
						break;
					default:
						throw new ClassFormatError("{0} (Illegal constant pool type 0x{1:X})", inputClassName, tag);
				}
			}
			for (int i = 1; i < constantpoolcount; i++)
			{
				if (constantpool[i] != null)
				{
					try
					{
						constantpool[i].Resolve(this);
					}
					catch (ClassFormatError x)
					{
						// HACK at this point we don't yet have the class name, so any exceptions throw
						// are missing the class name
						throw new ClassFormatError("{0} ({1})", inputClassName, x.Message);
					}
					catch (IndexOutOfRangeException)
					{
						throw new ClassFormatError("{0} (Invalid constant pool item #{1})", inputClassName, i);
					}
					catch (InvalidCastException)
					{
						throw new ClassFormatError("{0} (Invalid constant pool item #{1})", inputClassName, i);
					}
				}
			}
			access_flags = (__Modifiers) br.ReadUInt16();
			// NOTE although the vmspec says (in 4.1) that interfaces must be marked abstract, earlier versions of
			// javac (JDK 1.1) didn't do this, so the VM doesn't enforce this rule
			// NOTE although the vmspec implies (in 4.1) that ACC_SUPER is illegal on interfaces, it doesn't enforce this
			if ((IsInterface && IsFinal) || (IsAbstract && IsFinal))
			{
				throw new ClassFormatError("{0} (Illegal class modifiers 0x{1:X})", inputClassName, access_flags);
			}
			this_class = br.ReadUInt16();
			ValidateConstantPoolItemClass(inputClassName, this_class);
			super_class = br.ReadUInt16();
			// NOTE for convenience we allow parsing java/lang/Object (which has no super class), so
			// we check for super_class != 0
			if (super_class != 0)
			{
				ValidateConstantPoolItemClass(inputClassName, super_class);
			}
			else
			{
				if (this.Name != "java.lang.Object" || !allowJavaLangObject)
				{
					throw new ClassFormatError("{0} (Bad superclass index)", Name);
				}
			}
			if (IsInterface && (super_class == 0 || this.SuperClass != "java.lang.Object"))
			{
				throw new ClassFormatError("{0} (Interfaces must have java.lang.Object as superclass)", Name);
			}
			// most checks are already done by ConstantPoolItemClass.Resolve, but since it allows
			// array types, we do need to check for that
			if (this.Name[0] == '[')
			{
				throw new ClassFormatError("Bad name");
			}
			int interfaces_count = br.ReadUInt16();
			interfaces = new ConstantPoolItemClass[interfaces_count];
			for (int i = 0; i < interfaces.Length; i++)
			{
				int index = br.ReadUInt16();
				if (index == 0 || index >= constantpool.Length)
				{
					throw new ClassFormatError("{0} (Illegal constant pool index)", Name);
				}
				ConstantPoolItemClass cpi = constantpool[index] as ConstantPoolItemClass;
				if (cpi == null)
				{
					throw new ClassFormatError("{0} (Interface name has bad constant type)", Name);
				}
				interfaces[i] = cpi;
				for (int j = 0; j < i; j++)
				{
					if (interfaces[j].Name == cpi.Name)
					{
						throw new ClassFormatError("{0} (Repetitive interface name)", Name);
					}
				}
			}
			int fields_count = br.ReadUInt16();
			fields = new Field[fields_count];
			Hashtable fieldNameSigs = new Hashtable();
			for (int i = 0; i < fields_count; i++)
			{
				fields[i] = new Field(this, br);
				string name = fields[i].Name;
				if (!IsValidIdentifier(name))
				{
					throw new ClassFormatError("{0} (Illegal field name \"{1}\")", Name, name);
				}
				try
				{
					fieldNameSigs.Add(name + fields[i].Signature, null);
				}
				catch (ArgumentException)
				{
					throw new ClassFormatError("{0} (Repetitive field name/signature)", Name);
				}
			}
			int methods_count = br.ReadUInt16();
			methods = new Method[methods_count];
			Hashtable methodNameSigs = new Hashtable();
			for (int i = 0; i < methods_count; i++)
			{
				methods[i] = new Method(this, br);
				string name = methods[i].Name;
				string sig = methods[i].Signature;
				if (!IsValidIdentifier(name) && name != "<init>" && name != "<clinit>")
				{
					throw new ClassFormatError("{0} (Illegal method name \"{1}\")", Name, name);
				}
				if ((name == "<init>" || name == "<clinit>") && !sig.EndsWith("V"))
				{
					throw new ClassFormatError("{0} (Method \"{1}\" has illegal signature \"{2}\")", Name, name, sig);
				}
				try
				{
					methodNameSigs.Add(name + sig, null);
				}
				catch (ArgumentException)
				{
					throw new ClassFormatError("{0} (Repetitive method name/signature)", Name);
				}
			}
			int attributes_count = br.ReadUInt16();
			for (int i = 0; i < attributes_count; i++)
			{
				switch (GetConstantPoolUtf8String(br.ReadUInt16()))
				{
					case "Deprecated":
						deprecated = true;
#if FUZZ_HACK
						br.Skip(br.ReadUInt32());
#else
						if(br.ReadUInt32() != 0)
						{
							throw new ClassFormatError("Deprecated attribute has non-zero length");
						}
#endif
						break;
					case "SourceFile":
						if (br.ReadUInt32() != 2)
						{
							throw new ClassFormatError("SourceFile attribute has incorrect length");
						}
						sourceFile = GetConstantPoolUtf8String(br.ReadUInt16());
						break;
					case "InnerClasses":
#if FUZZ_HACK
						// Sun totally ignores the length of InnerClasses attribute,
						// so when we're running Fuzz this shows up as lots of differences.
						// To get rid off these differences define the FUZZ_HACK symbol.
						BigEndianBinaryReader rdr = br;
						br.ReadUInt32();
#else
						BigEndianBinaryReader rdr = br.Section(br.ReadUInt32());
#endif
						ushort count = rdr.ReadUInt16();
						innerClasses = new InnerClass[count];
						for (int j = 0; j < innerClasses.Length; j++)
						{
							innerClasses[j].innerClass = rdr.ReadUInt16();
							innerClasses[j].outerClass = rdr.ReadUInt16();
							innerClasses[j].name = rdr.ReadUInt16();
							innerClasses[j].accessFlags = (__Modifiers) rdr.ReadUInt16();
							if (innerClasses[j].innerClass != 0 && !(GetConstantPoolItem(innerClasses[j].innerClass) is ConstantPoolItemClass))
							{
								throw new ClassFormatError("{0} (inner_class_info_index has bad constant pool index)", this.Name);
							}
							if (innerClasses[j].outerClass != 0 && !(GetConstantPoolItem(innerClasses[j].outerClass) is ConstantPoolItemClass))
							{
								throw new ClassFormatError("{0} (outer_class_info_index has bad constant pool index)", this.Name);
							}
							if (innerClasses[j].name != 0 && utf8_cp[innerClasses[j].name] == null)
							{
								throw new ClassFormatError("{0} (inner class name has bad constant pool index)", this.Name);
							}
							if (innerClasses[j].innerClass == innerClasses[j].outerClass)
							{
								throw new ClassFormatError("{0} (Class is both inner and outer class)", this.Name);
							}
						}
#if !FUZZ_HACK
						if(!rdr.IsAtEnd)
						{
							throw new ClassFormatError("{0} (InnerClasses attribute has wrong length)", this.Name);
						}
#endif
						break;
					case "IKVM.NET.Assembly":
						if (br.ReadUInt32() != 2)
						{
							throw new ClassFormatError("IKVM.NET.Assembly attribute has incorrect length");
						}
						ikvmAssembly = GetConstantPoolUtf8String(br.ReadUInt16());
						break;
					default:
						br.Skip(br.ReadUInt32());
						break;
				}
			}
			if (br.Position != offset + length)
			{
				throw new ClassFormatError("Extra bytes at the end of the class file");
			}
		}
		catch (OverflowException)
		{
			throw new ClassFormatError("Truncated class file (or section)");
		}
		catch (IndexOutOfRangeException)
		{
			// TODO we should throw more specific errors
			throw new ClassFormatError("Unspecified class file format error");
		}
//		catch(Exception x)
//		{
//			Console.WriteLine(x);
//			FileStream fs = File.Create(inputClassName + ".broken");
//			fs.Write(buf, offset, length);
//			fs.Close();
//			throw;
//		}
	}

	private void ValidateConstantPoolItemClass(string classFile, ushort index)
	{
		if (index >= constantpool.Length || !(constantpool[index] is ConstantPoolItemClass))
		{
			throw new ClassFormatError("{0} (Bad constant pool index #{1})", classFile, index);
		}
	}

	private static bool IsValidIdentifier(string name)
	{
		if (name.Length == 0)
		{
			return false;
		}
		if (!Char.IsLetter(name[0]) && name[0] != '$' && name[0] != '_')
		{
			return false;
		}
		for (int i = 1; i < name.Length; i++)
		{
			if (!Char.IsLetterOrDigit(name[i]) && name[i] != '$' && name[i] != '_')
			{
				return false;
			}
		}
		return true;
	}

	private static bool IsValidFieldSig(string sig)
	{
		return IsValidFieldSigImpl(sig, 0, sig.Length);
	}

	private static bool IsValidFieldSigImpl(string sig, int start, int end)
	{
		if (start >= end)
		{
			return false;
		}
		switch (sig[start])
		{
			case 'L':
				// TODO we might consider doing more checking here
				return sig.IndexOf(';', start + 1) == end - 1;
			case '[':
				while (sig[start] == '[')
				{
					start++;
					if (start == end)
					{
						return false;
					}
				}
				return IsValidFieldSigImpl(sig, start, end);
			case 'B':
			case 'Z':
			case 'C':
			case 'S':
			case 'I':
			case 'J':
			case 'F':
			case 'D':
				return start == end - 1;
			default:
				return false;
		}
	}

	private static bool IsValidMethodSig(string sig)
	{
		if (sig.Length < 3 || sig[0] != '(')
		{
			return false;
		}
		int end = sig.IndexOf(')');
		if (end == -1)
		{
			return false;
		}
		if (!sig.EndsWith(")V") && !IsValidFieldSigImpl(sig, end + 1, sig.Length))
		{
			return false;
		}
		for (int i = 1; i < end; i++)
		{
			switch (sig[i])
			{
				case 'B':
				case 'Z':
				case 'C':
				case 'S':
				case 'I':
				case 'J':
				case 'F':
				case 'D':
					break;
				case 'L':
					i = sig.IndexOf(';', i);
					break;
				case '[':
					while (sig[i] == '[')
					{
						i++;
					}
					if ("BZCSIJFDL".IndexOf(sig[i]) == -1)
					{
						return false;
					}
					if (sig[i] == 'L')
					{
						i = sig.IndexOf(';', i);
					}
					break;
				default:
					return false;
			}
			if (i == -1 || i >= end)
			{
				return false;
			}
		}
		return true;
	}

	internal int MajorVersion
	{
		get { return majorVersion; }
	}

	// NOTE this property is only used when statically compiling
	// (and it is set by the static compiler's class loader in vm.cs)
	internal ClassFile OuterClass
	{
		get { return outerClass; }
		set { outerClass = value; }
	}

	internal __Modifiers Modifiers
	{
		get { return access_flags; }
	}

	internal bool IsAbstract
	{
		get
		{
			// interfaces are implicitly abstract
			return (access_flags & (__Modifiers.Abstract | __Modifiers.Interface)) != 0;
		}
	}

	internal bool IsFinal
	{
		get { return (access_flags & __Modifiers.Final) != 0; }
	}

	internal bool IsPublic
	{
		get { return (access_flags & __Modifiers.Public) != 0; }
	}

	internal bool IsInterface
	{
		get { return (access_flags & __Modifiers.Interface) != 0; }
	}

	internal bool IsSuper
	{
		get { return (access_flags & __Modifiers.Super) != 0; }
	}

	internal ConstantPoolItemFieldref GetFieldref(int index)
	{
		return (ConstantPoolItemFieldref) constantpool[index];
	}

	// NOTE this returns an MI, because it used for both normal methods and interface methods
	internal ConstantPoolItemMI GetMethodref(int index)
	{
		return (ConstantPoolItemMI) constantpool[index];
	}

	private ConstantPoolItem GetConstantPoolItem(int index)
	{
		return constantpool[index];
	}

	internal string GetConstantPoolClass(int index)
	{
		return ((ConstantPoolItemClass) constantpool[index]).Name;
	}

	internal string GetConstantPoolUtf8String(int index)
	{
		string s = utf8_cp[index];
		if (s == null)
		{
			if (this_class == 0)
			{
				throw new ClassFormatError("Bad constant pool index #{0}", index);
			}
			else
			{
				throw new ClassFormatError("{0} (Bad constant pool index #{1})", this.Name, index);
			}
		}
		return s;
	}

	internal ConstantType GetConstantPoolConstantType(int index)
	{
		return constantpool[index].GetConstantType();
	}

	internal double GetConstantPoolConstantDouble(int index)
	{
		return ((ConstantPoolItemDouble) constantpool[index]).Value;
	}

	internal float GetConstantPoolConstantFloat(int index)
	{
		return ((ConstantPoolItemFloat) constantpool[index]).Value;
	}

	internal int GetConstantPoolConstantInteger(int index)
	{
		return ((ConstantPoolItemInteger) constantpool[index]).Value;
	}

	internal long GetConstantPoolConstantLong(int index)
	{
		return ((ConstantPoolItemLong) constantpool[index]).Value;
	}

	internal string GetConstantPoolConstantString(int index)
	{
		return ((ConstantPoolItemString) constantpool[index]).Value;
	}

	internal string Name
	{
		get { return GetConstantPoolClass(this_class); }
	}

	internal string SuperClass
	{
		get { return GetConstantPoolClass(super_class); }
	}

	internal Field[] Fields
	{
		get { return fields; }
	}

	internal Method[] Methods
	{
		get { return methods; }
	}

	internal ConstantPoolItemClass[] Interfaces
	{
		get { return interfaces; }
	}

	internal string SourceFileAttribute
	{
		get { return sourceFile; }
	}

	internal string IKVMAssemblyAttribute
	{
		get { return ikvmAssembly; }
	}

	internal bool DeprecatedAttribute
	{
		get { return deprecated; }
	}

	internal struct InnerClass
	{
		internal ushort innerClass; // ConstantPoolItemClass
		internal ushort outerClass; // ConstantPoolItemClass
		internal ushort name; // ConstantPoolItemUtf8
		internal __Modifiers accessFlags;
	}

	internal InnerClass[] InnerClasses
	{
		get { return innerClasses; }
	}

	internal enum ConstantType
	{
		Integer,
		Long,
		Float,
		Double,
		String,
		Class
	}

	internal abstract class ConstantPoolItem
	{
		internal virtual void Resolve(ClassFile classFile)
		{
		}

		internal virtual ConstantType GetConstantType()
		{
			throw new InvalidOperationException();
		}
	}

	internal sealed class ConstantPoolItemClass : ConstantPoolItem
	{
		private ushort name_index;
		private string name;

		internal ConstantPoolItemClass(BigEndianBinaryReader br)
		{
			name_index = br.ReadUInt16();
		}

		internal override void Resolve(ClassFile classFile)
		{
			name = classFile.GetConstantPoolUtf8String(name_index);
			if (name.Length > 0)
			{
				char prev = name[0];
				if (Char.IsLetter(prev) || prev == '$' || prev == '_' || prev == '[')
				{
					int skip = 1;
					int end = name.Length;
					if (prev == '[')
					{
						// TODO optimize this
						if (!IsValidFieldSig(name))
						{
							goto barf;
						}
						while (name[skip] == '[')
						{
							skip++;
						}
						if (name.EndsWith(";"))
						{
							end--;
						}
					}
					for (int i = skip; i < end; i++)
					{
						char c = name[i];
						if (!Char.IsLetterOrDigit(c) && c != '$' && c != '_' && (c != '/' || prev == '/'))
						{
							goto barf;
						}
						prev = c;
					}
					name = String.Intern(name.Replace('/', '.'));
					return;
				}
			}
			barf:
			throw new ClassFormatError("Invalid class name \"{0}\"", name);
		}

		internal string Name
		{
			get { return name; }
		}

		internal override ConstantType GetConstantType()
		{
			return ConstantType.Class;
		}
	}

	private sealed class ConstantPoolItemDouble : ConstantPoolItem
	{
		private double d;

		internal ConstantPoolItemDouble(BigEndianBinaryReader br)
		{
			d = br.ReadDouble();
		}

		internal override ConstantType GetConstantType()
		{
			return ConstantType.Double;
		}

		internal double Value
		{
			get { return d; }
		}
	}

	internal abstract class ConstantPoolItemFMI : ConstantPoolItem
	{
		private ushort class_index;
		private ushort name_and_type_index;
		private ConstantPoolItemClass clazz;
		private string name;
		private string descriptor;

		internal ConstantPoolItemFMI(BigEndianBinaryReader br)
		{
			class_index = br.ReadUInt16();
			name_and_type_index = br.ReadUInt16();
		}

		internal override void Resolve(ClassFile classFile)
		{
			ConstantPoolItemNameAndType name_and_type = (ConstantPoolItemNameAndType) classFile.GetConstantPoolItem(name_and_type_index);
			clazz = (ConstantPoolItemClass) classFile.GetConstantPoolItem(class_index);
			// if the constant pool items referred to were strings, GetConstantPoolItem returns null
			if (name_and_type == null || clazz == null)
			{
				throw new ClassFormatError("Bad index in constant pool");
			}
			name = String.Intern(classFile.GetConstantPoolUtf8String(name_and_type.name_index));
			descriptor = classFile.GetConstantPoolUtf8String(name_and_type.descriptor_index);
			Validate(name, descriptor);
			descriptor = String.Intern(descriptor.Replace('/', '.'));
		}

		protected abstract void Validate(string name, string descriptor);

		internal string Name
		{
			get { return name; }
		}

		internal string Signature
		{
			get { return descriptor; }
		}

		internal string Class
		{
			get { return clazz.Name; }
		}
	}

	internal sealed class ConstantPoolItemFieldref : ConstantPoolItemFMI
	{
		internal ConstantPoolItemFieldref(BigEndianBinaryReader br) : base(br)
		{
		}

		protected override void Validate(string name, string descriptor)
		{
			if (!IsValidFieldSig(descriptor))
			{
				throw new ClassFormatError("Invalid field signature \"{0}\"", descriptor);
			}
			if (!IsValidIdentifier(name))
			{
				throw new ClassFormatError("Invalid field name \"{0}\"", name);
			}
		}
	}

	internal class ConstantPoolItemMI : ConstantPoolItemFMI
	{
		internal ConstantPoolItemMI(BigEndianBinaryReader br) : base(br)
		{
		}

		protected override void Validate(string name, string descriptor)
		{
			if (!IsValidMethodSig(descriptor))
			{
				throw new ClassFormatError("Method {0} has invalid signature {1}", name, descriptor);
			}
			if (name == "<init>" || name == "<clinit>")
			{
				if (!descriptor.EndsWith("V"))
				{
					throw new ClassFormatError("Method {0} has invalid signature {1}", name, descriptor);
				}
			}
			else if (!IsValidIdentifier(name))
			{
				throw new ClassFormatError("Invalid method name \"{0}\"", name);
			}
		}
	}

	internal sealed class ConstantPoolItemMethodref : ConstantPoolItemMI
	{
		internal ConstantPoolItemMethodref(BigEndianBinaryReader br) : base(br)
		{
		}
	}

	internal sealed class ConstantPoolItemInterfaceMethodref : ConstantPoolItemMI
	{
		internal ConstantPoolItemInterfaceMethodref(BigEndianBinaryReader br) : base(br)
		{
		}
	}

	private sealed class ConstantPoolItemFloat : ConstantPoolItem
	{
		private float v;

		internal ConstantPoolItemFloat(BigEndianBinaryReader br)
		{
			v = br.ReadSingle();
		}

		internal override ConstantType GetConstantType()
		{
			return ConstantType.Float;
		}

		internal float Value
		{
			get { return v; }
		}
	}

	private sealed class ConstantPoolItemInteger : ConstantPoolItem
	{
		private int v;

		internal ConstantPoolItemInteger(BigEndianBinaryReader br)
		{
			v = br.ReadInt32();
		}

		internal override ConstantType GetConstantType()
		{
			return ConstantType.Integer;
		}

		internal int Value
		{
			get { return v; }
		}
	}

	private sealed class ConstantPoolItemLong : ConstantPoolItem
	{
		private long l;

		internal ConstantPoolItemLong(BigEndianBinaryReader br)
		{
			l = br.ReadInt64();
		}

		internal override ConstantType GetConstantType()
		{
			return ConstantType.Long;
		}

		internal long Value
		{
			get { return l; }
		}
	}

	internal sealed class ConstantPoolItemNameAndType : ConstantPoolItem
	{
		internal ushort name_index;
		internal ushort descriptor_index;

		internal ConstantPoolItemNameAndType(BigEndianBinaryReader br)
		{
			name_index = br.ReadUInt16();
			descriptor_index = br.ReadUInt16();
		}
	}

	private sealed class ConstantPoolItemString : ConstantPoolItem
	{
		private ushort string_index;
		private string s;

		internal ConstantPoolItemString(BigEndianBinaryReader br)
		{
			string_index = br.ReadUInt16();
		}

		internal override void Resolve(ClassFile classFile)
		{
			s = classFile.GetConstantPoolUtf8String(string_index);
		}

		internal override ConstantType GetConstantType()
		{
			return ConstantType.String;
		}

		internal string Value
		{
			get { return s; }
		}
	}

	internal enum Constant
	{
		Utf8 = 1,
		Integer = 3,
		Float = 4,
		Long = 5,
		Double = 6,
		Class = 7,
		String = 8,
		Fieldref = 9,
		Methodref = 10,
		InterfaceMethodref = 11,
		NameAndType = 12
	}

	internal abstract class FieldOrMethod
	{
		protected __Modifiers access_flags;
		private string name;
		private string descriptor;
		protected bool deprecated;

		internal FieldOrMethod(ClassFile classFile, BigEndianBinaryReader br)
		{
			access_flags = (__Modifiers) br.ReadUInt16();
			name = String.Intern(classFile.GetConstantPoolUtf8String(br.ReadUInt16()));
			descriptor = classFile.GetConstantPoolUtf8String(br.ReadUInt16());
			ValidateSig(classFile, descriptor);
			descriptor = String.Intern(descriptor.Replace('/', '.'));
		}

		protected abstract void ValidateSig(ClassFile classFile, string descriptor);

		internal string Name
		{
			get { return name; }
		}

		internal string Signature
		{
			get { return descriptor; }
		}

		internal __Modifiers Modifiers
		{
			get { return (__Modifiers) access_flags; }
		}

		internal bool IsAbstract
		{
			get { return (access_flags & __Modifiers.Abstract) != 0; }
		}

		internal bool IsFinal
		{
			get { return (access_flags & __Modifiers.Final) != 0; }
		}

		internal bool IsPublic
		{
			get { return (access_flags & __Modifiers.Public) != 0; }
		}

		internal bool IsPrivate
		{
			get { return (access_flags & __Modifiers.Private) != 0; }
		}

		internal bool IsProtected
		{
			get { return (access_flags & __Modifiers.Protected) != 0; }
		}

		internal bool IsStatic
		{
			get { return (access_flags & __Modifiers.Static) != 0; }
		}

		internal bool IsSynchronized
		{
			get { return (access_flags & __Modifiers.Synchronized) != 0; }
		}

		internal bool IsVolatile
		{
			get { return (access_flags & __Modifiers.Volatile) != 0; }
		}

		internal bool IsTransient
		{
			get { return (access_flags & __Modifiers.Transient) != 0; }
		}

		internal bool IsNative
		{
			get { return (access_flags & __Modifiers.Native) != 0; }
		}

		internal bool DeprecatedAttribute
		{
			get { return deprecated; }
		}
	}

	internal sealed class Field : FieldOrMethod
	{
		private object constantValue;

		internal Field(ClassFile classFile, BigEndianBinaryReader br) : base(classFile, br)
		{
			if ((IsPrivate && IsPublic) || (IsPrivate && IsProtected) || (IsPublic && IsProtected)
			    || (IsFinal && IsVolatile)
			    || (classFile.IsInterface && (!IsPublic || !IsStatic || !IsFinal || IsTransient)))
			{
				throw new ClassFormatError("{0} (Illegal field modifiers: 0x{1:X})", classFile.Name, access_flags);
			}
			int attributes_count = br.ReadUInt16();
			for (int i = 0; i < attributes_count; i++)
			{
				switch (classFile.GetConstantPoolUtf8String(br.ReadUInt16()))
				{
					case "Deprecated":
						deprecated = true;
#if FUZZ_HACK
						br.Skip(br.ReadUInt32());
#else
						if(br.ReadUInt32() != 0)
						{
							throw new ClassFormatError("Deprecated attribute has non-zero length");
						}
#endif
						break;
					case "ConstantValue":
						{
							if (br.ReadUInt32() != 2)
							{
								throw new ClassFormatError("Invalid ConstantValue attribute length");
							}
							ushort index = br.ReadUInt16();
							try
							{
								switch (Signature)
								{
									case "I":
										constantValue = classFile.GetConstantPoolConstantInteger(index);
										break;
									case "S":
										constantValue = (short) classFile.GetConstantPoolConstantInteger(index);
										break;
									case "B":
										constantValue = (byte) classFile.GetConstantPoolConstantInteger(index);
										break;
									case "C":
										constantValue = (char) classFile.GetConstantPoolConstantInteger(index);
										break;
									case "Z":
										constantValue = classFile.GetConstantPoolConstantInteger(index) != 0;
										break;
									case "J":
										constantValue = classFile.GetConstantPoolConstantLong(index);
										break;
									case "F":
										constantValue = classFile.GetConstantPoolConstantFloat(index);
										break;
									case "D":
										constantValue = classFile.GetConstantPoolConstantDouble(index);
										break;
									case "Ljava.lang.String;":
										constantValue = classFile.GetConstantPoolConstantString(index);
										break;
									default:
										throw new ClassFormatError("{0} (Invalid signature for constant)", classFile.Name);
								}
							}
							catch (InvalidCastException)
							{
								throw new ClassFormatError("{0} (Bad index into constant pool)", classFile.Name);
							}
							catch (IndexOutOfRangeException)
							{
								throw new ClassFormatError("{0} (Bad index into constant pool)", classFile.Name);
							}
							catch (InvalidOperationException)
							{
								throw new ClassFormatError("{0} (Bad index into constant pool)", classFile.Name);
							}
							catch (NullReferenceException)
							{
								throw new ClassFormatError("{0} (Bad index into constant pool)", classFile.Name);
							}
							break;
						}
					default:
						br.Skip(br.ReadUInt32());
						break;
				}
			}
		}

		protected override void ValidateSig(ClassFile classFile, string descriptor)
		{
			if (!IsValidFieldSig(descriptor))
			{
				throw new ClassFormatError("{0} (Field \"{1}\" has invalid signature \"{2}\")", classFile.Name, this.Name, descriptor);
			}
		}

		internal object ConstantValue
		{
			get { return constantValue; }
		}
	}

	internal sealed class Method : FieldOrMethod
	{
		private Code code;
		private string[] exceptions;

		internal Method(ClassFile classFile, BigEndianBinaryReader br) : base(classFile, br)
		{
			// vmspec 4.6 says that all flags, except ACC_STRICT are ignored on <clinit>
			if (Name == "<clinit>" && Signature == "()V")
			{
				access_flags &= __Modifiers.Strictfp;
				access_flags |= (__Modifiers.Static | __Modifiers.Private);
			}
			else
			{
				// LAMESPEC: vmspec 4.6 says that abstract methods can not be strictfp (and this makes sense), but
				// javac (pre 1.5) is broken and marks abstract methods as strictfp (if you put the strictfp on the class)
				if ((Name == "<init>" && (IsStatic || IsSynchronized || IsFinal || IsAbstract || IsNative))
				    || (IsPrivate && IsPublic) || (IsPrivate && IsProtected) || (IsPublic && IsProtected)
				    || (IsAbstract && (IsFinal || IsNative || IsPrivate || IsStatic || IsSynchronized))
				    || (classFile.IsInterface && (!IsPublic || !IsAbstract)))
				{
					throw new ClassFormatError("{0} (Illegal method modifiers: 0x{1:X})", classFile.Name, access_flags);
				}
			}
			int attributes_count = br.ReadUInt16();
			for (int i = 0; i < attributes_count; i++)
			{
				switch (classFile.GetConstantPoolUtf8String(br.ReadUInt16()))
				{
					case "Deprecated":
						deprecated = true;
#if FUZZ_HACK
						br.Skip(br.ReadUInt32());
#else
						if(br.ReadUInt32() != 0)
						{
							throw new ClassFormatError("{0} (Deprecated attribute has non-zero length)", classFile.Name);
						}
#endif
						break;
					case "Code":
						{
							if (!code.IsEmpty)
							{
								throw new ClassFormatError("{0} (Duplicate Code attribute)", classFile.Name);
							}
							BigEndianBinaryReader rdr = br.Section(br.ReadUInt32());
							code.Read(classFile, this, rdr);
							if (!rdr.IsAtEnd)
							{
								throw new ClassFormatError("{0} (Code attribute has wrong length)", classFile.Name);
							}
							break;
						}
					case "Exceptions":
						{
							if (exceptions != null)
							{
								throw new ClassFormatError("{0} (Duplicate Exceptions attribute)", classFile.Name);
							}
							BigEndianBinaryReader rdr = br.Section(br.ReadUInt32());
							ushort count = rdr.ReadUInt16();
							exceptions = new string[count];
							for (int j = 0; j < count; j++)
							{
								exceptions[j] = classFile.GetConstantPoolClass(rdr.ReadUInt16());
							}
							if (!rdr.IsAtEnd)
							{
								throw new ClassFormatError("{0} (Exceptions attribute has wrong length)", classFile.Name);
							}
							break;
						}
					default:
						br.Skip(br.ReadUInt32());
						break;
				}
			}
			if (IsAbstract || IsNative)
			{
				if (!code.IsEmpty)
				{
					throw new ClassFormatError("Abstract or native method cannot have a Code attribute");
				}
			}
			else
			{
				if (code.IsEmpty)
				{
#if FUZZ_HACK
					if (this.Name == "<clinit>")
					{
						code.verifyError = string.Format("Class {0}, method {1} signature {2}: No Code attribute", classFile.Name, this.Name, this.Signature);
						return;
					}
#endif
					throw new ClassFormatError("Method has no Code attribute");
				}
			}
		}

		protected override void ValidateSig(ClassFile classFile, string descriptor)
		{
			if (!IsValidMethodSig(descriptor))
			{
				throw new ClassFormatError("{0} (Method \"{1}\" has invalid signature \"{2}\")", classFile.Name, this.Name, descriptor);
			}
		}

		internal bool IsStrictfp
		{
			get { return (access_flags & __Modifiers.Strictfp) != 0; }
		}

		// Is this the <clinit>()V method?
		internal bool IsClassInitializer
		{
			get { return Name == "<clinit>" && Signature == "()V"; }
		}

		internal string[] ExceptionsAttribute
		{
			get { return exceptions; }
		}

		internal string VerifyError
		{
			get { return code.verifyError; }
		}

		// maps argument 'slot' (as encoded in the xload/xstore instructions) into the ordinal
		internal int[] ArgMap
		{
			get { return code.argmap; }
		}

		internal int MaxStack
		{
			get { return code.max_stack; }
		}

		internal int MaxLocals
		{
			get { return code.max_locals; }
		}

		internal Instruction[] Instructions
		{
			get { return code.instructions; }
		}

		// maps a PC to an index in the Instruction[], invalid PCs return -1
		internal int[] PcIndexMap
		{
			get { return code.pcIndexMap; }
		}

		internal ExceptionTableEntry[] ExceptionTable
		{
			get { return code.exception_table; }
		}

		private struct Code
		{
			internal string verifyError;
			internal ushort max_stack;
			internal ushort max_locals;
			internal Instruction[] instructions;
			internal int[] pcIndexMap;
			internal ExceptionTableEntry[] exception_table;
			internal int[] argmap;

			internal void Read(ClassFile classFile, Method method, BigEndianBinaryReader br)
			{
				max_stack = br.ReadUInt16();
				max_locals = br.ReadUInt16();
				uint code_length = br.ReadUInt32();
				if (code_length > 65536)
				{
					throw new ClassFormatError("{0} (Invalid Code length {1})", classFile.Name, code_length);
				}
				Instruction[] instructions = new Instruction[code_length + 1];
				int basePosition = br.Position;
				int instructionIndex = 0;
				try
				{
					BigEndianBinaryReader rdr = br.Section(code_length);
					while (!rdr.IsAtEnd)
					{
						instructions[instructionIndex++].Read((ushort) (rdr.Position - basePosition), rdr);
					}
					// we add an additional nop instruction to make it easier for consumers of the code array
					instructions[instructionIndex++].SetTermNop((ushort) (rdr.Position - basePosition));
				}
				catch (ClassFormatError x)
				{
					// any class format errors in the code block are actually verify errors
					verifyError = x.Message;
				}
				this.instructions = new Instruction[instructionIndex];
				Array.Copy(instructions, 0, this.instructions, 0, instructionIndex);
				ushort exception_table_length = br.ReadUInt16();
				exception_table = new ExceptionTableEntry[exception_table_length];
				for (int i = 0; i < exception_table_length; i++)
				{
					exception_table[i] = new ExceptionTableEntry();
					exception_table[i].start_pc = br.ReadUInt16();
					exception_table[i].end_pc = br.ReadUInt16();
					exception_table[i].handler_pc = br.ReadUInt16();
					exception_table[i].catch_type = br.ReadUInt16();
					exception_table[i].ordinal = i;
				}
				ushort attributes_count = br.ReadUInt16();
				for (int i = 0; i < attributes_count; i++)
				{
					switch (classFile.GetConstantPoolUtf8String(br.ReadUInt16()))
					{
						case "LineNumberTable":
							br.Skip(br.ReadUInt32());
							break;
						case "LocalVariableTable":
							br.Skip(br.ReadUInt32());
							break;
						default:
							br.Skip(br.ReadUInt32());
							break;
					}
				}
				// build the pcIndexMap
				pcIndexMap = new int[this.instructions[instructionIndex - 1].PC + 1];
				for (int i = 0; i < pcIndexMap.Length; i++)
				{
					pcIndexMap[i] = -1;
				}
				for (int i = 0; i < instructionIndex - 1; i++)
				{
					pcIndexMap[this.instructions[i].PC] = i;
				}
				// build the argmap
				string sig = method.Signature;
				ArrayList args = new ArrayList();
				int pos = 0;
				if (!method.IsStatic)
				{
					args.Add(pos++);
				}
				for (int i = 1; sig[i] != ')'; i++)
				{
					args.Add(pos++);
					switch (sig[i])
					{
						case 'L':
							i = sig.IndexOf(';', i);
							break;
						case 'D':
						case 'J':
							args.Add(-1);
							break;
						case '[':
							{
								while (sig[i] == '[')
								{
									i++;
								}
								if (sig[i] == 'L')
								{
									i = sig.IndexOf(';', i);
								}
								break;
							}
					}
				}
				argmap = new int[args.Count];
				args.CopyTo(argmap);
				if (args.Count > max_locals)
				{
					throw new ClassFormatError("{0} (Arguments can't fit into locals)", classFile.Name);
				}
			}

			internal bool IsEmpty
			{
				get { return instructions == null; }
			}
		}

		internal sealed class ExceptionTableEntry
		{
			internal ushort start_pc;
			internal ushort end_pc;
			internal ushort handler_pc;
			internal ushort catch_type;
			internal int ordinal;
		}

		internal struct Instruction
		{
			private ushort pc;
			private ByteCode opcode;
			private NormalizedByteCode normopcode;
			private int arg1;
			private short arg2;
			private SwitchEntry[] switch_entries;

			private struct SwitchEntry
			{
				internal int value;
				internal int target_offset;
			}

			internal void SetTermNop(ushort pc)
			{
				// TODO what happens if we already have exactly the maximum number of instructions?
				this.pc = pc;
				this.opcode = ByteCode.__nop;
			}

			internal void Read(ushort pc, BigEndianBinaryReader br)
			{
				this.pc = pc;
				ByteCode bc = (ByteCode) br.ReadByte();
				switch (ByteCodeMetaData.GetMode(bc))
				{
					case ByteCodeMode.Simple:
						break;
					case ByteCodeMode.Constant_1:
					case ByteCodeMode.Local_1:
						arg1 = br.ReadByte();
						break;
					case ByteCodeMode.Constant_2:
						arg1 = br.ReadUInt16();
						break;
					case ByteCodeMode.Branch_2:
						arg1 = br.ReadInt16();
						break;
					case ByteCodeMode.Branch_4:
						arg1 = br.ReadInt32();
						break;
					case ByteCodeMode.Constant_2_1_1:
						arg1 = br.ReadUInt16();
						arg2 = br.ReadByte();
						if (br.ReadByte() != 0)
						{
							throw new ClassFormatError("invokeinterface filler must be zero");
						}
						break;
					case ByteCodeMode.Immediate_1:
						arg1 = br.ReadSByte();
						break;
					case ByteCodeMode.Immediate_2:
						arg1 = br.ReadInt16();
						break;
					case ByteCodeMode.Local_1_Immediate_1:
						arg1 = br.ReadByte();
						arg2 = br.ReadSByte();
						break;
					case ByteCodeMode.Constant_2_Immediate_1:
						arg1 = br.ReadUInt16();
						arg2 = br.ReadSByte();
						break;
					case ByteCodeMode.Tableswitch:
						{
							// skip the padding
							uint p = pc + 1u;
							uint align = ((p + 3) & 0x7ffffffc) - p;
							br.Skip(align);
							int default_offset = br.ReadInt32();
							this.arg1 = default_offset;
							int low = br.ReadInt32();
							int high = br.ReadInt32();
							if (low > high || high > 16384L + low)
							{
								throw new ClassFormatError("Incorrect tableswitch");
							}
							SwitchEntry[] entries = new SwitchEntry[high - low + 1];
							for (int i = low; i <= high; i++)
							{
								entries[i - low].value = i;
								entries[i - low].target_offset = br.ReadInt32();
							}
							this.switch_entries = entries;
							break;
						}
					case ByteCodeMode.Lookupswitch:
						{
							// skip the padding
							uint p = pc + 1u;
							uint align = ((p + 3) & 0x7ffffffc) - p;
							br.Skip(align);
							int default_offset = br.ReadInt32();
							this.arg1 = default_offset;
							int count = br.ReadInt32();
							if (count < 0 || count > 16384)
							{
								throw new ClassFormatError("Incorrect lookupswitch");
							}
							SwitchEntry[] entries = new SwitchEntry[count];
							for (int i = 0; i < count; i++)
							{
								entries[i].value = br.ReadInt32();
								entries[i].target_offset = br.ReadInt32();
							}
							this.switch_entries = entries;
							break;
						}
					case ByteCodeMode.WidePrefix:
						bc = (ByteCode) br.ReadByte();
						// NOTE the PC of a wide instruction is actually the PC of the
						// wide prefix, not the following instruction (vmspec 4.9.2)
						switch (ByteCodeMetaData.GetWideMode(bc))
						{
							case ByteCodeModeWide.Local_2:
								arg1 = br.ReadUInt16();
								break;
							case ByteCodeModeWide.Local_2_Immediate_2:
								arg1 = br.ReadUInt16();
								arg2 = br.ReadInt16();
								break;
							default:
								throw new ClassFormatError("Invalid wide prefix on opcode: {0}", bc);
						}
						break;
					default:
						throw new ClassFormatError("Invalid opcode: {0}", bc);
				}
				this.opcode = bc;
				this.normopcode = ByteCodeMetaData.GetNormalizedByteCode(bc);
				arg1 = ByteCodeMetaData.GetArg(opcode, arg1);
			}

			internal int PC
			{
				get { return pc; }
			}

			internal ByteCode OpCode
			{
				get { return opcode; }
			}

			internal NormalizedByteCode NormalizedOpCode
			{
				get { return normopcode; }
			}

			internal int Arg1
			{
				get { return arg1; }
			}

			internal int Arg2
			{
				get { return arg2; }
			}

			internal int NormalizedArg1
			{
				get { return arg1; }
			}

			internal int DefaultOffset
			{
				get { return arg1; }
			}

			internal int SwitchEntryCount
			{
				get { return switch_entries.Length; }
			}

			internal int GetSwitchValue(int i)
			{
				return switch_entries[i].value;
			}

			internal int GetSwitchTargetOffset(int i)
			{
				return switch_entries[i].target_offset;
			}
		}
	}
}