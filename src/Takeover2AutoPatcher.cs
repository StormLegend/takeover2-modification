using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// BepInEx 5 preloader patcher. It applies the same Cecil edits as the
// one-time installer, but only to the in-memory assembly loaded for this run.
// The game DLLs on disk are left untouched, so Steam updates do not remove the
// patch. The small helper assembly and translation files are copied into the
// game's Managed folder on startup because the injected calls reference them.
public static class Takeover2AutoPatcher
{
    private static readonly string[] Targets =
    {
        "Assembly-CSharp.dll",
        "Unity.TextMeshPro.dll",
        "UnityEngine.UI.dll"
    };

    private static string managed;
    private static AssemblyDefinition helper;

    public static IEnumerable<string> TargetDLLs { get { return Targets; } }

    public static void Initialize()
    {
        managed = Environment.GetEnvironmentVariable("DOORSTOP_MANAGED_FOLDER_DIR");
        if (String.IsNullOrWhiteSpace(managed))
        {
            string patcher = Path.GetDirectoryName(typeof(Takeover2AutoPatcher).Assembly.Location);
            managed = Path.Combine(Directory.GetParent(Directory.GetParent(patcher).FullName).FullName,
                "Takeover2_Data", "Managed");
        }

        string source = Path.GetDirectoryName(typeof(Takeover2AutoPatcher).Assembly.Location);
        CopyPayload(source, "TakeoverChinese.dll");
        CopyPayload(source, "zh-CN.txt");
        CopyPayload(source, "labels.tsv");
        helper = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "TakeoverChinese.dll"));
        Console.WriteLine("[Takeover2-AutoPatch] Initialized for " + managed);
    }

    private static void CopyPayload(string source, string name)
    {
        string from = Path.Combine(source, name);
        string to = Path.Combine(managed, name);
        if (!File.Exists(from)) throw new FileNotFoundException("Missing auto-patch payload", from);
        if (!File.Exists(to) || !SameFile(from, to)) File.Copy(from, to, true);
    }

    private static bool SameFile(string left, string right)
    {
        if (new FileInfo(left).Length != new FileInfo(right).Length) return false;
        using (FileStream a = File.OpenRead(left))
        using (FileStream b = File.OpenRead(right))
        {
            int x, y;
            do
            {
                x = a.ReadByte();
                y = b.ReadByte();
                if (x != y) return false;
            } while (x >= 0);
        }
        return true;
    }

    public static void Patch(AssemblyDefinition assembly)
    {
        string name = assembly.Name.Name + ".dll";
        if (name == "Assembly-CSharp.dll")
        {
            PatchDialogue(assembly);
            PatchLifeThreshold(assembly);
            using (AssemblyDefinition playMaker = AssemblyDefinition.ReadAssembly(
                Path.Combine(managed, "PlayMaker.dll"),
                new ReaderParameters { AssemblyResolver = new DefaultAssemblyResolver() }))
            {
                PatchPistol(assembly, playMaker.MainModule);
            }
        }
        else if (name == "Unity.TextMeshPro.dll")
        {
            PatchTextMeshPro(assembly);
        }
        else if (name == "UnityEngine.UI.dll")
        {
            PatchLegacyText(assembly);
        }
        Console.WriteLine("[Takeover2-AutoPatch] Patched " + name);
    }

    private static TypeDefinition Type(AssemblyDefinition assembly, string name)
    {
        return assembly.MainModule.Types.Single(t => t.FullName == name);
    }

    private static MethodDefinition Method(TypeDefinition type, string name, params string[] parameters)
    {
        return type.Methods.Single(m => m.Name == name &&
            m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters));
    }

    private static void Insert(MethodDefinition method, bool post, params Instruction[] operations)
    {
        ILProcessor il = method.Body.GetILProcessor();
        if (!post)
        {
            Instruction first = method.Body.Instructions[0];
            foreach (Instruction op in operations) il.InsertBefore(first, op);
            return;
        }
        foreach (Instruction ret in method.Body.Instructions.Where(x => x.OpCode == OpCodes.Ret).ToArray())
        {
            ret.OpCode = OpCodes.Nop;
            Instruction current = ret;
            foreach (Instruction op in operations)
            {
                Instruction copy = op.Operand == null ? Instruction.Create(op.OpCode) :
                    op.Operand is FieldReference ? Instruction.Create(op.OpCode, (FieldReference)op.Operand) :
                    Instruction.Create(op.OpCode, (MethodReference)op.Operand);
                il.InsertAfter(current, copy);
                current = copy;
            }
            il.InsertAfter(current, Instruction.Create(OpCodes.Ret));
        }
    }

    private static Instruction Call(MethodDefinition method, TypeDefinition helperType, string name)
    {
        return Instruction.Create(OpCodes.Call,
            method.Module.ImportReference(helperType.Methods.Single(x => x.Name == name)));
    }

    private static void PatchDialogue(AssemblyDefinition assembly)
    {
        TypeDefinition type = Type(assembly, "DialogueLocalizationManager");
        MethodDefinition method = Method(type, "LoadLanguageFile", "System.String");
        TypeDefinition helperType = helper.MainModule.Types.Single(t => t.FullName == "TakeoverChinese");
        Insert(method, true,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, type.Fields.Single(f => f.Name == "dialogueTable")),
            Instruction.Create(OpCodes.Ldarg_1),
            Call(method, helperType, "OnLanguage"));
    }

    private static void PatchLifeThreshold(AssemblyDefinition assembly)
    {
        TypeDefinition type = Type(assembly, "HutongGames.PlayMaker.Actions.AwardLivesFromScore");
        MethodDefinition method = Method(type, "CheckScore");
        int patched = 0;
        foreach (Instruction load in method.Body.Instructions.Where(i =>
            i.OpCode == OpCodes.Ldfld &&
            ((FieldReference)i.Operand).Name == "pointsPerLife").ToArray())
        {
            Instruction read = load.Next;
            Instruction pushThis = load.Previous;
            if (pushThis.OpCode != OpCodes.Ldarg_0 || read == null ||
                read.OpCode != OpCodes.Callvirt ||
                ((MethodReference)read.Operand).FullName !=
                    "System.Int32 HutongGames.PlayMaker.FsmInt::get_Value()")
                continue;
            pushThis.OpCode = OpCodes.Ldc_I4;
            pushThis.Operand = 2000;
            load.OpCode = OpCodes.Nop;
            load.Operand = null;
            read.OpCode = OpCodes.Nop;
            read.Operand = null;
            patched++;
        }
        Console.WriteLine("[Takeover2-AutoPatch] 2000-point life threshold patched " +
            patched + " time(s).");
    }

    private static void PatchTextMeshPro(AssemblyDefinition assembly)
    {
        TypeDefinition type = Type(assembly, "TMPro.TMP_Text");
        TypeDefinition helperType = helper.MainModule.Types.Single(t => t.FullName == "TakeoverChinese");
        foreach (MethodDefinition method in new[] {
            Method(type, "set_text", "System.String"),
            Method(type, "SetText", "System.String", "System.Boolean")
        })
            Insert(method, false,
                Instruction.Create(OpCodes.Ldarga, method.Parameters[0]),
                Call(method, helperType, "OnText"));

        foreach (string name in new[] { "TMPro.TextMeshProUGUI", "TMPro.TextMeshPro" })
            Insert(Method(Type(assembly, name), "OnEnable"), true,
                Instruction.Create(OpCodes.Ldarg_0),
                Call(Method(Type(assembly, name), "OnEnable"), helperType, "OnTmpEnabled"));
    }

    private static void PatchLegacyText(AssemblyDefinition assembly)
    {
        TypeDefinition type = Type(assembly, "UnityEngine.UI.Text");
        TypeDefinition helperType = helper.MainModule.Types.Single(t => t.FullName == "TakeoverChinese");
        MethodDefinition setter = Method(type, "set_text", "System.String");
        Insert(setter, false,
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldarga, setter.Parameters[0]),
            Call(setter, helperType, "OnLegacyText"));
        MethodDefinition enabled = Method(type, "OnEnable");
        Insert(enabled, true,
            Instruction.Create(OpCodes.Ldarg_0),
            Call(enabled, helperType, "OnLegacyEnabled"));
    }

    private static MethodDefinition Method(TypeDefinition type, string name)
    {
        return type.Methods.Single(m => m.Name == name && !m.HasParameters);
    }

    private static MethodReference NameGetter(ModuleDefinition module, TypeDefinition type)
    {
        Method(type, "get_Name");
        return new MethodReference("get_Name", module.TypeSystem.String,
            module.ImportReference(type)) { HasThis = true };
    }

    private static void OverrideValue(TypeDefinition type, string field, MethodDefinition guard,
        int value, params string[] methods)
    {
        TypeDefinition fsmInt = type.Module.GetType("HutongGames.PlayMaker.FsmInt");
        FieldDefinition variable = type.Fields.Single(f => f.Name == "intVariable" &&
            f.FieldType.FullName == "HutongGames.PlayMaker.FsmInt");
        foreach (string name in methods)
        {
            MethodDefinition method = Method(type, name);
            ILProcessor il = method.Body.GetILProcessor();
            Instruction load = method.Body.Instructions.Single(i => i.OpCode == OpCodes.Ldfld &&
                ((FieldReference)i.Operand).Name == field);
            Instruction read = load.Next;
            Instruction fallback = load.Previous;
            if (fallback.OpCode != OpCodes.Ldarg_0 || read.OpCode != OpCodes.Callvirt ||
                ((MethodReference)read.Operand).FullName != "System.Int32 HutongGames.PlayMaker.FsmInt::get_Value()")
                throw new InvalidOperationException("Unexpected PlayMaker instructions in " + method.FullName);

            foreach (Instruction instruction in new[] {
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Ldfld, variable),
                Instruction.Create(OpCodes.Call, guard),
                Instruction.Create(OpCodes.Brfalse_S, fallback),
                Instruction.Create(OpCodes.Ldc_I4, value),
                Instruction.Create(OpCodes.Br_S, read.Next)
            }) il.InsertBefore(fallback, instruction);
        }
    }

    private static void PatchPistol(AssemblyDefinition game, ModuleDefinition playMaker)
    {
        const string playMakerPrefix = "HutongGames.PlayMaker.";
        ModuleDefinition module = game.MainModule;
        TypeDefinition action = playMaker.GetType(playMakerPrefix + "FsmStateAction");
        TypeDefinition state = playMaker.GetType(playMakerPrefix + "FsmState");
        TypeDefinition fsm = playMaker.GetType(playMakerPrefix + "Fsm");
        TypeDefinition fsmInt = playMaker.GetType(playMakerPrefix + "FsmInt");
        TypeDefinition namedVariable = playMaker.GetType(playMakerPrefix + "NamedVariable");
        TypeDefinition guardType = new TypeDefinition("", "LocalHandgunAmmo",
            TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed,
            module.TypeSystem.Object);
        module.Types.Add(guardType);
        MethodDefinition guard = new MethodDefinition("IsHandgunPickup",
            MethodAttributes.Assembly | MethodAttributes.Static, module.TypeSystem.Boolean);
        guard.Parameters.Add(new ParameterDefinition("action", ParameterAttributes.None,
            module.ImportReference(action)));
        guard.Parameters.Add(new ParameterDefinition("variable", ParameterAttributes.None,
            module.ImportReference(fsmInt)));
        guardType.Methods.Add(guard);

        ILProcessor il = guard.Body.GetILProcessor();
        Instruction no = Instruction.Create(OpCodes.Ldc_I4_0);
        MethodReference equals = module.GetMemberReferences().OfType<MethodReference>().First(m =>
            m.FullName == "System.Boolean System.String::op_Equality(System.String,System.String)");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse_S, no);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, NameGetter(module, namedVariable));
        il.Emit(OpCodes.Ldstr, "bullets");
        il.Emit(OpCodes.Call, equals);
        il.Emit(OpCodes.Brfalse_S, no);
        foreach (var pair in new[] {
            new { Type = state, Getter = "get_State", Name = "bullet pick up" },
            new { Type = fsm, Getter = "get_Fsm", Name = "collision" }
        })
        {
            VariableDefinition local = new VariableDefinition(module.ImportReference(pair.Type));
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

        OverrideValue(module.GetType(playMakerPrefix + "Actions.IntAdd"), "add", guard,
            500, "OnEnter", "OnUpdate");
        OverrideValue(module.GetType(playMakerPrefix + "Actions.IntClamp"), "maxValue", guard,
            10000, "DoClamp");
    }
}
