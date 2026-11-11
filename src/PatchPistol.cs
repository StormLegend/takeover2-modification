using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class PatchPistol
{
    private const int RoundsPerPack = 500;
    private const int MaxRounds = 10000;
    private const string PlayMaker = "HutongGames.PlayMaker.";

    private static MethodDefinition Method(TypeDefinition type, string name)
    {
        return type.Methods.Single(m => m.Name == name && !m.HasParameters);
    }

    private static MethodReference NameGetter(ModuleDefinition module, TypeDefinition type)
    {
        Method(type, "get_Name");
        return new MethodReference("get_Name", module.TypeSystem.String, module.ImportReference(type)) { HasThis = true };
    }

    private static void OverrideValue(TypeDefinition type, string field, MethodDefinition guard,
                                      int value, params string[] methods)
    {
        var variable = type.Fields.Single(f => f.Name == "intVariable" && f.FieldType.FullName == PlayMaker + "FsmInt");
        foreach (var name in methods)
        {
            var method = Method(type, name);
            var il = method.Body.GetILProcessor();
            var load = method.Body.Instructions.Single(i => i.OpCode == OpCodes.Ldfld &&
                ((FieldReference)i.Operand).Name == field);
            var read = load.Next;
            var fallback = load.Previous;
            if (fallback.OpCode != OpCodes.Ldarg_0 || read.OpCode != OpCodes.Callvirt ||
                ((MethodReference)read.Operand).FullName != "System.Int32 " + PlayMaker + "FsmInt::get_Value()")
                throw new InvalidOperationException("Unexpected PlayMaker instructions in " + method.FullName);

            // Both paths leave the chosen integer on the stack; neither edits the FsmInt setting.
            foreach (var instruction in new[] {
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Ldfld, variable),
                Instruction.Create(OpCodes.Call, guard),
                Instruction.Create(OpCodes.Brfalse_S, fallback),
                Instruction.Create(OpCodes.Ldc_I4, value),
                Instruction.Create(OpCodes.Br_S, read.Next)
            })
                il.InsertBefore(fallback, instruction);
        }
    }

    public static void Main(string[] args)
    {
        // Arguments: the local translated Assembly-CSharp.dll, PlayMaker.dll, output DLL.
        using (var resolver = new DefaultAssemblyResolver())
        using (var playMaker = AssemblyDefinition.ReadAssembly(args[1]))
        {
            resolver.AddSearchDirectory(Path.GetDirectoryName(args[1]));
            using (var game = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { AssemblyResolver = resolver }))
                Apply(game, playMaker.MainModule, args[2]);
        }
    }

    private static void Apply(AssemblyDefinition game, ModuleDefinition pm, string output)
    {
        var module = game.MainModule;
        var action = pm.GetType(PlayMaker + "FsmStateAction");
        var state = pm.GetType(PlayMaker + "FsmState");
        var fsm = pm.GetType(PlayMaker + "Fsm");
        var fsmInt = pm.GetType(PlayMaker + "FsmInt");
        var namedVariable = pm.GetType(PlayMaker + "NamedVariable");
        var guardType = new TypeDefinition("", "LocalHandgunAmmo", TypeAttributes.NotPublic |
            TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
        module.Types.Add(guardType);
        var guard = new MethodDefinition("IsHandgunPickup", MethodAttributes.Assembly |
            MethodAttributes.Static, module.TypeSystem.Boolean);
        guard.Parameters.Add(new ParameterDefinition("action", ParameterAttributes.None, module.ImportReference(action)));
        guard.Parameters.Add(new ParameterDefinition("variable", ParameterAttributes.None, module.ImportReference(fsmInt)));
        guardType.Methods.Add(guard);

        var il = guard.Body.GetILProcessor();
        var no = Instruction.Create(OpCodes.Ldc_I4_0);
        var equals = module.GetMemberReferences().OfType<MethodReference>().First(m =>
            m.FullName == "System.Boolean System.String::op_Equality(System.String,System.String)");

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse_S, no);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, NameGetter(module, namedVariable));
        il.Emit(OpCodes.Ldstr, "bullets");
        il.Emit(OpCodes.Call, equals);
        il.Emit(OpCodes.Brfalse_S, no);
        foreach (var pair in new[] { new { Type = state, Getter = "get_State", Name = "bullet pick up" },
                                     new { Type = fsm, Getter = "get_Fsm", Name = "collision" } })
        {
            var local = new VariableDefinition(module.ImportReference(pair.Type));
            guard.Body.Variables.Add(local);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, module.ImportReference(Method(action, pair.Getter)));
            il.Emit(OpCodes.Stloc, local);
            il.Emit(OpCodes.Ldloc, local);
            il.Emit(OpCodes.Brfalse_S, no);
            il.Emit(OpCodes.Ldloc, local);
            il.Emit(OpCodes.Callvirt, NameGetter(module, pair.Type));
            il.Emit(OpCodes.Ldstr, pair.Name);
            il.Emit(OpCodes.Call, equals);
            il.Emit(OpCodes.Brfalse_S, no);
        }
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.Append(no);
        il.Emit(OpCodes.Ret);

        OverrideValue(module.GetType(PlayMaker + "Actions.IntAdd"), "add", guard, RoundsPerPack, "OnEnter", "OnUpdate");
        OverrideValue(module.GetType(PlayMaker + "Actions.IntClamp"), "maxValue", guard, MaxRounds, "DoClamp");
        game.Write(output);
    }
}
