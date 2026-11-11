using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Patch
{
    private static TypeDefinition Type(AssemblyDefinition a, string name)
    {
        return a.MainModule.Types.Single(t => t.FullName == name);
    }
    private static MethodDefinition Method(TypeDefinition t, string name, params string[] parameters)
    {
        return t.Methods.Single(m => m.Name == name && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters));
    }
    private static void Insert(MethodDefinition method, bool post, params Instruction[] operations)
    {
        var il = method.Body.GetILProcessor();
        if (!post)
        {
            var first = method.Body.Instructions[0];
            foreach (var op in operations) il.InsertBefore(first, op);
            return;
        }
        foreach (var ret in method.Body.Instructions.Where(x=>x.OpCode==OpCodes.Ret).ToArray())
        {
            // Preserve any existing branch to the return by turning the return into a no-op.
            ret.OpCode = OpCodes.Nop;
            var cur = ret;
            foreach (var op in operations)
            {
                var copy = op.Operand == null ? Instruction.Create(op.OpCode) :
                    op.Operand is FieldReference ? Instruction.Create(op.OpCode, (FieldReference)op.Operand) :
                    Instruction.Create(op.OpCode, (MethodReference)op.Operand);
                il.InsertAfter(cur, copy);
                cur = copy;
            }
            il.InsertAfter(cur, Instruction.Create(OpCodes.Ret));
        }
    }
    private static Instruction Call(MethodDefinition m, TypeDefinition helper, string method)
    {
        return Instruction.Create(OpCodes.Call,m.Module.ImportReference(helper.Methods.Single(x=>x.Name==method)));
    }
    private static void Main(string[] args)
    {
        using (var resolver = new DefaultAssemblyResolver())
        using (var helper = AssemblyDefinition.ReadAssembly(args[2]))
        {
            resolver.AddSearchDirectory(args[3]);
            resolver.AddSearchDirectory(args[0]);
            var h = Type(helper,"TakeoverChinese");
            foreach (var name in new[]{"Assembly-CSharp.dll","Unity.TextMeshPro.dll","UnityEngine.UI.dll"})
            using (var a = AssemblyDefinition.ReadAssembly(Path.Combine(args[0],name), new ReaderParameters { AssemblyResolver = resolver }))
            {
                if (name=="Assembly-CSharp.dll")
                {
                    var t = Type(a,"DialogueLocalizationManager");
                    var m = Method(t,"LoadLanguageFile","System.String");
                    Insert(m,true,Instruction.Create(OpCodes.Ldarg_0),
                        Instruction.Create(OpCodes.Ldfld,t.Fields.Single(f=>f.Name=="dialogueTable")),
                        Instruction.Create(OpCodes.Ldarg_1),Call(m,h,"OnLanguage"));
                }
                else if (name=="Unity.TextMeshPro.dll")
                {
                    var t=Type(a,"TMPro.TMP_Text");
                    foreach (var m in new[]{Method(t,"set_text","System.String"),Method(t,"SetText","System.String","System.Boolean")})
                        Insert(m,false,Instruction.Create(OpCodes.Ldarga,m.Parameters[0]),Call(m,h,"OnText"));
                    foreach (var cls in new[]{"TMPro.TextMeshProUGUI","TMPro.TextMeshPro"})
                    {
                        var m=Method(Type(a,cls),"OnEnable");
                        Insert(m,true,Instruction.Create(OpCodes.Ldarg_0),Call(m,h,"OnTmpEnabled"));
                    }
                }
                else
                {
                    var t=Type(a,"UnityEngine.UI.Text");
                    var m=Method(t,"set_text","System.String");
                    Insert(m,false,Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldarga,m.Parameters[0]),Call(m,h,"OnLegacyText"));
                    m=Method(t,"OnEnable");
                    Insert(m,true,Instruction.Create(OpCodes.Ldarg_0),Call(m,h,"OnLegacyEnabled"));
                }
                a.Write(Path.Combine(args[1],name));
                Console.WriteLine(name);
            }
        }
    }
}
